"""Tier-1 polling loop: periodic listing scans feeding the discovery queue.

Port of C# ``Services/Pipeline/PollService.cs``: immediate first poll unless paused;
each tick races the interval sleep against the check-now signal (and shutdown); check-now
bypasses pause while regular ticks honor it; the interval is re-read every tick; every
cycle's outcome is logged even when nobody else looks (C# ``ReportCycle``).
"""

import asyncio
import contextlib
import time
from collections.abc import Callable, Sequence
from enum import Enum
from typing import Any, Protocol, cast

from mostaql.diagnostics.interaction_log import FailureLike, get_interaction_logger
from mostaql.errors import BackboneError, DomainError, poll_listing_fetch_failed
from mostaql.models import ProjectSummary
from mostaql.pipeline.diff import DiffEngine
from mostaql.pipeline.inflight import InFlightTracker
from mostaql.pipeline.queue import DiscoveryQueue
from mostaql.pipeline.ratelimit import TokenBucketRateLimiter
from mostaql.storage.protocol import ProjectStore

__all__ = ["ListingFetcher", "PollEvents", "PollService", "PollServiceStatus"]

_MIN_INTERVAL_SECONDS = 1


def _log_failure(
    checkpoint: str, error: DomainError, data: dict[str, object] | None = None
) -> None:
    """Domain-failure line through the diagnostics singleton.

    The frozen :class:`DomainError` structurally satisfies the logger's mutable
    ``FailureLike`` protocol; one local cast keeps every call site clean.
    """
    get_interaction_logger().failure(checkpoint, cast(FailureLike, error), data)


class PollServiceStatus(Enum):
    """Pipeline health/activity signal (C# ``PollServiceStatus``, tray-icon mirrored)."""

    IDLE = "idle"
    POLLING = "polling"
    BACKLOG_DRAINING = "backlog_draining"
    ERROR = "error"


class ListingFetcher(Protocol):
    """Structural scraper seam (C# ``IProjectScraper.FetchListingAsync``).

    Defined locally so the pipeline never couples to the HTTP/scraping layers;
    :class:`mostaql.scraping.scraper.MostaqlScraper` satisfies it structurally.
    """

    async def fetch_listing(self, query_params: str | None = None) -> list[ProjectSummary]:
        """Fetch and parse the projects listing feed."""
        ...


class PollEvents(Protocol):
    """Status callbacks consumed by the poller (plan §8 ``PipelineEvents`` slice).

    Replaces the C# ``GlobalAppStatusService`` (ledger 11); its progress fields
    (``IsScanning`` / ``DiscoveryProgress`` / scan counters) were UI-only radar
    plumbing and are intentionally dropped -- the callbacks below carry everything
    the headless service still observes.
    """

    def on_status_changed(self, status: PollServiceStatus) -> None:
        """Raised whenever :attr:`PollService.status` changes."""
        ...

    def on_project_discovered(self, project_id: int, title: str) -> None:
        """Radar detection pulse for a newly discovered project."""
        ...

    def on_queue_count_changed(self, count: int) -> None:
        """Discovery-queue depth changed (C# ``UpdateQueueCount``)."""
        ...

    def on_scan_succeeded(self, seen: int, enqueued: int) -> None:
        """Scan finished: ``seen`` projects on the page, ``enqueued`` genuinely new."""
        ...

    def on_scan_failed(self, error: DomainError) -> None:
        """A poll cycle failed with the given domain error."""
        ...


class PollService:
    """Periodic discovery loop (C# origin: ``Services/Pipeline/PollService.cs``)."""

    def __init__(
        self,
        scraper: ListingFetcher,
        diff_engine: DiffEngine,
        queue: DiscoveryQueue,
        tracker: InFlightTracker,
        store: ProjectStore,
        limiter: TokenBucketRateLimiter,
        events: PollEvents,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self.poll_interval_seconds = 30
        self.query_params = ""
        self.paused = False
        self._scraper = scraper
        self._diff_engine = diff_engine
        self._discovery_queue = queue
        self._in_flight_tracker = tracker
        self._store = store
        self._rate_limiter = limiter
        self._events = events
        self._clock = clock
        self._status = PollServiceStatus.IDLE
        self._check_now_signal = asyncio.Event()
        self._stop_event = asyncio.Event()
        self._parent_cancel: asyncio.Event | None = None
        self._loop_task: asyncio.Task[None] | None = None

    @property
    def status(self) -> PollServiceStatus:
        """Current observable status (mirrored to subscribers via ``on_status_changed``)."""
        return self._status

    def set_paused(self, paused: bool) -> None:
        """Toggle the paused flag (tray icon "Pause / Resume" equivalent)."""
        self.paused = paused

    def request_check_now(self) -> None:
        """Force an immediate cycle outside the timer, bypassing pause (idempotent)."""
        self._check_now_signal.set()

    async def start(self, cancel: asyncio.Event | None = None) -> None:
        """Launch the loop as a background task linked to an optional parent cancel event."""
        self._parent_cancel = cancel
        self._stop_event.clear()
        self._loop_task = asyncio.ensure_future(self._run_loop())

    async def stop(self) -> None:
        """Signal the loop to end and wait for it to finish cleanly."""
        self._stop_event.set()
        if self._loop_task is not None:
            await self._loop_task

    async def poll_once(self) -> int:
        """Run one full discovery cycle; returns the number of newly enqueued projects.

        Failure reporting lives HERE exactly once per failing cycle (C# ``Fail()``):
        the checkpoint-specific log, ERROR status and ``on_scan_failed`` all fire inside
        this method, then the typed error propagates so the loop can add its
        ``PollService.Cycle`` summary line. Unexpected exceptions are wrapped as
        POLL-001 (C# generic ``catch`` -> ``ListingFetchFailed``); cancellation passes
        through untouched.
        """
        self._set_status(PollServiceStatus.POLLING)
        reported = False

        def fail_once(error: DomainError, checkpoint: str) -> None:
            nonlocal reported
            reported = True
            self._report_failure(error, checkpoint)

        try:
            return await self._discover(fail_once)
        except asyncio.CancelledError:
            raise
        except BackboneError as err:
            if not reported:
                self._report_failure(err.error, "PollService.Unexpected")
            raise
        except Exception as exc:
            wrapped = poll_listing_fetch_failed(exc)
            if not reported:
                self._report_failure(wrapped, "PollService.Unexpected")
            raise BackboneError(wrapped) from exc

    async def _discover(self, fail_once: Callable[[DomainError, str], None]) -> int:
        """Token -> listing -> diff -> enqueue-new pipeline of one cycle."""
        await self._rate_limiter.wait_for_token()

        try:
            listing = await self._scraper.fetch_listing(self.query_params)
        except BackboneError as err:
            fail_once(err.error, "PollService.FetchListing")
            raise

        try:
            diffed = await self._diff_engine.diff(listing)
        except BackboneError as err:
            fail_once(err.error, "PollService.Diff")
            raise

        summaries_by_id = _summaries_first_occurrence(listing)
        enqueued = 0
        for project_id in diffed.new_project_ids:
            summary = summaries_by_id.get(project_id)
            if summary is None:
                get_interaction_logger().mark(
                    "PollService.MissingSummary", "B", {"project_id": project_id}
                )
                continue
            if not self._in_flight_tracker.try_mark_in_flight(project_id):
                continue
            # Persistent backlog first, summary persisted immediately, then memory queue.
            await self._store.add_to_backlog(project_id)
            await self._store.insert_summary(summary)
            await self._discovery_queue.enqueue(project_id)
            self._events.on_project_discovered(project_id, summary.title)
            self._events.on_queue_count_changed(self._discovery_queue.count)
            enqueued += 1

        self._set_status(
            PollServiceStatus.BACKLOG_DRAINING if enqueued > 0 else PollServiceStatus.IDLE
        )
        self._events.on_scan_succeeded(len(summaries_by_id), enqueued)
        return enqueued

    async def _run_loop(self) -> None:
        """Background loop body (C# ``RunLoopAsync``)."""
        if not self.paused:
            await self._first_cycle()

        while not self._stop_requested():
            stopped, manual_check_now = await self._wait_next_tick()
            if stopped:
                return
            # Check-now forces a cycle even while paused; regular ticks honor the pause.
            if manual_check_now or not self.paused:
                await self._guarded_cycle()

    async def _first_cycle(self) -> None:
        """Immediate first poll instead of waiting a full interval on startup."""
        with contextlib.suppress(asyncio.CancelledError):
            await self._cycle_with_report()

    async def _guarded_cycle(self) -> None:
        with contextlib.suppress(asyncio.CancelledError):
            await self._cycle_with_report()

    async def _cycle_with_report(self) -> None:
        """One poll cycle plus the last-line-of-defence outcome log (C# ``ReportCycle``).

        Whatever ``poll_once`` raised is accounted for here, so a future failure path
        that forgets to report still leaves a trace. Never swallows real errors.
        """
        try:
            await self.poll_once()
        except asyncio.CancelledError:
            raise
        except BackboneError as err:
            _log_failure("PollService.Cycle", err.error)

    async def _wait_next_tick(self) -> tuple[bool, bool]:
        """Sleep one interval OR wake early on check-now / shutdown.

        Returns ``(stopped, manual_check_now)``. The interval is re-read EVERY tick so
        runtime setting changes apply without a restart (clamped to >= 1s, mirroring
        C# ``Math.Max(1, PollIntervalSeconds)``).
        """
        interval = max(_MIN_INTERVAL_SECONDS, self.poll_interval_seconds)
        sleep_tick = asyncio.ensure_future(asyncio.sleep(interval))
        check_now = asyncio.ensure_future(self._check_now_signal.wait())
        stop_tick = asyncio.ensure_future(self._stop_event.wait())
        racers: list[asyncio.Future[Any]] = [
            sleep_tick,
            check_now,
            stop_tick,
        ]
        if self._parent_cancel is not None:
            racers.append(asyncio.ensure_future(self._parent_cancel.wait()))
        try:
            done, _pending = await asyncio.wait(racers, return_when=asyncio.FIRST_COMPLETED)
        finally:
            for racer in racers:
                racer.cancel()
            await asyncio.gather(*racers, return_exceptions=True)
        self._check_now_signal.clear()
        if self._stop_requested():
            return True, False
        return False, check_now in done

    def _stop_requested(self) -> bool:
        if self._stop_event.is_set():
            return True
        return self._parent_cancel is not None and self._parent_cancel.is_set()

    def _report_failure(self, error: DomainError, checkpoint: str) -> None:
        """Log a failing cycle and publish it (C# ``Fail``). Never swallow a poll failure."""
        self._set_status(PollServiceStatus.ERROR)
        _log_failure(checkpoint, error, {"poll_interval_seconds": self.poll_interval_seconds})
        self._events.on_scan_failed(error)

    def _set_status(self, status: PollServiceStatus) -> None:
        if self._status == status:
            return
        self._status = status
        self._events.on_status_changed(status)


def _summaries_first_occurrence(listing: Sequence[ProjectSummary]) -> dict[int, ProjectSummary]:
    """ID -> first summary bearing it, skipping invalid ids (C# GroupBy -> First)."""
    summaries: dict[int, ProjectSummary] = {}
    for summary in listing:
        if summary.project_id > 0 and summary.project_id not in summaries:
            summaries[summary.project_id] = summary
    return summaries

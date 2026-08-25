"""Single long-lived enrichment consumer draining the discovery queue (plan §8 contract).

Port of C# ``Services/Pipeline/WorkerPool/EnrichmentWorker.cs``: pulls project IDs off the
:class:`~mostaql.pipeline.queue.DiscoveryQueue` one at a time, runs the internal retry
ladder (1m/2m/4m/8m/15m -- five attempts), persists owner + details on success, removes the
persistent backlog entry on the normal return path, and ALWAYS releases the in-flight ID in
``finally``. Unexpected exceptions are logged (ENRICH-002) and never kill the worker.
"""

import asyncio
from collections.abc import Awaitable, Callable
from typing import Protocol, cast

from mostaql.diagnostics.interaction_log import FailureLike, get_interaction_logger
from mostaql.errors import (
    BackboneError,
    DomainError,
    StoreOperationError,
    enrich_max_attempts_exhausted,
    enrich_unexpected,
)
from mostaql.models import ProjectDetails
from mostaql.pipeline.enrich import EnrichmentService
from mostaql.pipeline.inflight import InFlightTracker
from mostaql.pipeline.queue import DiscoveryQueue
from mostaql.storage.protocol import ProjectStore

__all__ = ["RETRY_DELAYS_SECONDS", "EnrichmentWorker", "WorkerEvents"]

SleepFn = Callable[[float], Awaitable[None]]

RETRY_DELAYS_SECONDS = (60, 120, 240, 480, 900)


def _log_failure(
    checkpoint: str, error: DomainError, data: dict[str, object] | None = None
) -> None:
    """Domain-failure line through the diagnostics singleton.

    The frozen :class:`DomainError` structurally satisfies the logger's mutable
    ``FailureLike`` protocol; one local cast keeps every call site clean.
    """
    get_interaction_logger().failure(checkpoint, cast(FailureLike, error), data)


WORKER_STATE_PROCESSING = "processing"
WORKER_STATE_COMPLETED = "completed"
WORKER_STATE_ERROR = "error"
WORKER_STATE_IDLE = "idle"

_IDLE_STATE_DELAY_SECONDS = 2.0


class WorkerEvents(Protocol):
    """Status callbacks consumed by the worker (plan §8 ``PipelineEvents`` slice).

    Replaces the C# ``GlobalAppStatusService`` radar/worker-card surface (ledger 11);
    the runtime composition root provides the logging implementation (Wave D).
    """

    def on_worker_state(self, worker_id: int, state: str) -> None:
        """Radar segment state change for one worker."""
        ...

    def on_queue_count_changed(self, count: int) -> None:
        """Discovery-queue depth changed (C# ``GlobalAppStatusService.UpdateQueueCount``)."""
        ...

    def on_enriched(self, details: ProjectDetails) -> None:
        """A project finished enrichment; extensible notification hook point."""
        ...


class EnrichmentWorker:
    """One enrichment loop instance bound to a radar segment id (C# ``EnrichmentWorker``)."""

    def __init__(
        self,
        worker_id: int,
        queue: DiscoveryQueue,
        enrichment: EnrichmentService,
        tracker: InFlightTracker,
        store: ProjectStore,
        events: WorkerEvents,
        retry_delays: tuple[float, ...] = RETRY_DELAYS_SECONDS,
        sleep: SleepFn = asyncio.sleep,
    ) -> None:
        self._worker_id = worker_id
        self._queue = queue
        self._enrichment = enrichment
        self._tracker = tracker
        self._store = store
        self._events = events
        self._retry_delays = retry_delays
        self._sleep = sleep
        self._idle_tasks: set[asyncio.Task[None]] = set()

    async def run(self, cancel: asyncio.Event) -> None:
        """Consume IDs until the queue is closed-and-drained or ``cancel`` fires.

        C# ``RunAsync``: a single project's failure must not cost the pool a worker --
        every per-item exception is contained here so the loop keeps draining.
        Caller-initiated cancellation rethrows (mirrors the C# OperationCanceledException
        branch that leaves without marking the worker failed).
        """
        async for project_id in self._queue.drain_all(cancel):
            await self._run_one(project_id, cancel)

    async def _run_one(self, project_id: int, cancel: asyncio.Event) -> None:
        """Process exactly one ID with the C# try/catch/finally containment shape."""
        try:
            self._events.on_worker_state(self._worker_id, WORKER_STATE_PROCESSING)
            self._events.on_queue_count_changed(self._queue.count)
            await self._process(project_id)
            # Success (or ladder exhaustion): remove from persistent backlog.
            await self._store.remove_from_backlog(project_id)
            self._events.on_worker_state(self._worker_id, WORKER_STATE_COMPLETED)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            # C# fix note: this used to rethrow and permanently lose the worker while the
            # UI still showed a live pipeline. One project's failure must not do that.
            self._events.on_worker_state(self._worker_id, WORKER_STATE_ERROR)
            error = enrich_unexpected(project_id, exc)
            _log_failure(
                "EnrichmentWorker.Unexpected",
                error,
                {"worker_id": self._worker_id, "project_id": project_id},
            )
        finally:
            self._events.on_queue_count_changed(self._queue.count)
            # Hard rule per In-Flight Tracker spec: released ALWAYS, success or failure.
            self._tracker.mark_complete(project_id)
            self._schedule_idle(cancel)

    async def _process(self, project_id: int) -> None:
        """Retry ladder + persistence for one ID (C# ``ProcessAsync``).

        Ladder exhaustion RETURNS NORMALLY: the row stays Pending while the backlog
        entry is removed by the normal caller path -- the C# nuance, preserved verbatim.
        """
        last_error: DomainError | None = None
        details: ProjectDetails | None = None

        for attempt in range(1, len(self._retry_delays) + 1):
            get_interaction_logger().mark(
                "EnrichmentWorker.AttemptStart", "D", {"project_id": project_id, "attempt": attempt}
            )
            try:
                details = await self._enrichment.enrich(project_id)
                get_interaction_logger().mark(
                    "EnrichmentWorker.FetchSuccess",
                    "D",
                    {"project_id": project_id, "title": details.title},
                )
                break
            except BackboneError as err:
                last_error = err.error
                _log_failure(
                    "EnrichmentWorker.Attempt",
                    last_error,
                    {
                        "worker_id": self._worker_id,
                        "project_id": project_id,
                        "attempt": attempt,
                    },
                )
                if attempt < len(self._retry_delays):
                    await self._sleep(float(self._retry_delays[attempt - 1]))

        if details is None:
            if last_error is not None:
                exhausted = enrich_max_attempts_exhausted(
                    project_id, len(self._retry_delays), last_error
                )
                _log_failure(
                    "EnrichmentWorker.MaxAttemptsExhausted",
                    exhausted,
                    {"worker_id": self._worker_id, "project_id": project_id},
                )
                self._events.on_worker_state(self._worker_id, WORKER_STATE_ERROR)
            return

        try:
            if details.owner.name != "" or details.owner.owner_id > 0:
                await self._store.upsert_owner(details.owner)
            get_interaction_logger().mark(
                "EnrichmentWorker.UpsertStart", "D", {"project_id": project_id}
            )
            await self._store.upsert_details(details)
            get_interaction_logger().mark(
                "EnrichmentWorker.UpsertSuccess", "D", {"project_id": project_id}
            )
        except StoreOperationError as err:
            _log_failure("EnrichmentWorker.UpsertFailed", err.error, {"project_id": project_id})

        self._events.on_enriched(details)

    def _schedule_idle(self, cancel: asyncio.Event) -> None:
        """Fire-and-forget delayed idle transition (C# Task.Delay(2000).ContinueWith).

        Documented exception to the fixed-concurrency rule (idle-state timer); guarded by
        the cancellation flag so shutdown does not resurrect idle states.
        """
        if cancel.is_set():
            return
        task = asyncio.ensure_future(self._delayed_idle(cancel))
        self._idle_tasks.add(task)
        task.add_done_callback(self._idle_tasks.discard)

    async def _delayed_idle(self, cancel: asyncio.Event) -> None:
        await self._sleep(_IDLE_STATE_DELAY_SECONDS)
        if not cancel.is_set():
            self._events.on_worker_state(self._worker_id, WORKER_STATE_IDLE)

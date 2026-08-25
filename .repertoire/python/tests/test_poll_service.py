"""PollService behavioral tests against the C# PollService.cs spec."""

import asyncio
from collections.abc import Callable
from datetime import UTC, datetime

import pytest
from test_worker_pool import FakeStore, RecordingLogger, SpyEvents

import mostaql.pipeline.poller as poller_module
from mostaql.errors import (
    BackboneError,
    DiffStateError,
    DomainError,
    HttpRequestError,
)
from mostaql.models import ProjectSummary
from mostaql.pipeline.diff import DiffResult
from mostaql.pipeline.inflight import InFlightTracker
from mostaql.pipeline.queue import DiscoveryQueue
from mostaql.pipeline.ratelimit import TokenBucketRateLimiter


def summary(pid: int, title: str | None = None) -> ProjectSummary:
    return ProjectSummary(
        project_id=pid,
        title=title if title is not None else f"t{pid}",
        discovered_at=datetime(2026, 1, 1, tzinfo=UTC),
    )


class ScriptedScraper:
    """ListingFetcher double: outcomes are listing lists or exception instances."""

    def __init__(self, *outcomes: object) -> None:
        self.outcomes = list(outcomes)
        self.calls: list[tuple[str, float]] = []

    async def fetch_listing(self, query_params: str | None = None) -> list[ProjectSummary]:
        self.calls.append((query_params or "", asyncio.get_running_loop().time()))
        if not self.outcomes:
            return []
        outcome = self.outcomes.pop(0)
        if isinstance(outcome, BaseException):
            raise outcome
        return outcome  # type: ignore[no-any-return]


class PartitioningDiffEngine:
    """DiffEngine stand-in computing new-vs-known exactly like the real engine."""

    def __init__(self, committed: frozenset[int] | set[int] = frozenset()) -> None:
        self.committed = set(committed)

    async def diff(self, polled: list[ProjectSummary]) -> DiffResult:
        result = DiffResult()
        for item in polled:
            if item.project_id in self.committed:
                result.already_known_ids.append(item.project_id)
            else:
                result.new_project_ids.append(item.project_id)
        return result


class StubDiffEngine:
    def __init__(self, outcome: DiffResult | BaseException) -> None:
        self.outcome = outcome

    async def diff(self, polled: object) -> DiffResult:
        if isinstance(self.outcome, BaseException):
            raise self.outcome
        return self.outcome


class RecordingQueue(DiscoveryQueue):
    """DiscoveryQueue appending enqueue markers onto a shared ordered timeline."""

    def __init__(self, timeline: list[tuple[str, int]]) -> None:
        super().__init__()
        self._timeline = timeline

    async def enqueue(self, project_id: int) -> None:
        self._timeline.append(("enqueue", project_id))
        await super().enqueue(project_id)


def http_error() -> HttpRequestError:
    return HttpRequestError(DomainError("HTTP-001", "listing down", "ar"))


@pytest.fixture
def spy_log(monkeypatch: pytest.MonkeyPatch) -> RecordingLogger:
    recorder = RecordingLogger()
    monkeypatch.setattr(poller_module, "get_interaction_logger", lambda: recorder)
    return recorder


def build_poller(
    scraper: ScriptedScraper,
    diff_engine: object,
    store: FakeStore,
    events: SpyEvents,
    tracker: InFlightTracker | None = None,
    timeline: list[tuple[str, int]] | None = None,
) -> tuple[object, DiscoveryQueue]:
    queue: DiscoveryQueue = RecordingQueue(timeline) if timeline is not None else DiscoveryQueue()
    active_tracker = tracker if tracker is not None else InFlightTracker()
    poller = poller_module.PollService(
        scraper,  # type: ignore[arg-type]
        diff_engine,  # type: ignore[arg-type]
        queue,
        active_tracker,
        store,
        TokenBucketRateLimiter(requests_per_minute=6000, safe_requests=False),
        events,
    )
    return poller, queue


async def wait_until(predicate: Callable[[], bool], timeout: float = 5.0) -> None:
    deadline = asyncio.get_running_loop().time() + timeout
    while not predicate():
        if asyncio.get_running_loop().time() > deadline:
            raise AssertionError("condition not reached before timeout")
        await asyncio.sleep(0.005)


async def test_immediate_first_poll_without_waiting_for_interval(spy_log) -> None:
    scraper = ScriptedScraper([summary(1)])
    events = SpyEvents()
    poller, _queue = build_poller(scraper, StubDiffEngine(DiffResult()), FakeStore(), events)

    await poller.start(asyncio.Event())
    await wait_until(lambda: len(scraper.calls) == 1)
    await poller.stop()

    assert events.scan_ok == [(1, 0)]
    assert events.statuses == [
        poller_module.PollServiceStatus.POLLING,
        poller_module.PollServiceStatus.IDLE,
    ]


async def test_paused_start_skips_first_poll_and_ticks_but_check_now_executes(
    spy_log,
) -> None:
    scraper = ScriptedScraper([])
    events = SpyEvents()
    poller, _queue = build_poller(scraper, StubDiffEngine(DiffResult()), FakeStore(), events)
    poller.poll_interval_seconds = 1
    poller.paused = True

    cancel = asyncio.Event()
    await poller.start(cancel)
    await asyncio.sleep(0.15)  # well past one clamped tick
    assert scraper.calls == []

    poller.request_check_now()
    await wait_until(lambda: len(scraper.calls) == 1)
    await poller.stop()

    assert events.statuses[-1] == poller_module.PollServiceStatus.IDLE


async def test_interval_is_reread_every_tick(spy_log) -> None:
    scraper = ScriptedScraper([], [])
    events = SpyEvents()
    poller, _queue = build_poller(scraper, PartitioningDiffEngine(), FakeStore(), events)
    poller.poll_interval_seconds = 2

    cancel = asyncio.Event()
    await poller.start(cancel)
    await wait_until(lambda: len(scraper.calls) >= 1)

    # Tick N reads the interval at ITS OWN start: tick 2 still owes the old 2s,
    # but tick 3 must shrink to the new 1s -- that contrast proves the re-read.
    poller.poll_interval_seconds = 1
    await wait_until(lambda: len(scraper.calls) >= 3, timeout=6.0)
    gap_old = scraper.calls[1][1] - scraper.calls[0][1]
    gap_new = scraper.calls[2][1] - scraper.calls[1][1]
    await poller.stop()

    assert gap_old > 1.9
    assert 0.6 < gap_new < 1.6


async def test_interval_clamped_to_at_least_one_second(spy_log) -> None:
    scraper = ScriptedScraper([])
    events = SpyEvents()
    poller, _queue = build_poller(scraper, PartitioningDiffEngine(), FakeStore(), events)
    poller.poll_interval_seconds = 0

    started = asyncio.get_running_loop().time()
    stopped, manual = await asyncio.wait_for(poller._wait_next_tick(), timeout=3.0)
    elapsed = asyncio.get_running_loop().time() - started

    assert stopped is False and manual is False
    assert 0.9 <= elapsed < 2.5


async def test_duplicate_id_race_is_skipped_via_in_flight_claim(spy_log) -> None:
    scraper = ScriptedScraper([summary(1001), summary(1002)])
    events = SpyEvents()
    store = FakeStore()
    tracker = InFlightTracker()
    tracker.try_mark_in_flight(1001)  # another actor claimed it first
    poller, queue = build_poller(
        scraper, PartitioningDiffEngine(), store, events, tracker, store.timeline
    )

    enqueued = await poller.poll_once()

    assert enqueued == 1
    assert ("add_backlog", 1002) in store.timeline
    assert ("add_backlog", 1001) not in store.timeline
    assert queue.count == 1


async def test_discovery_persists_summary_before_enqueueing(spy_log) -> None:
    scraper = ScriptedScraper([summary(1001, "first")])
    events = SpyEvents()
    store = FakeStore()
    poller, _queue = build_poller(
        scraper, PartitioningDiffEngine(), store, events, timeline=store.timeline
    )

    await poller.poll_once()

    ops = [name for name, _pid in store.timeline]
    assert ops.index("add_backlog") < ops.index("insert_summary") < ops.index("enqueue")
    assert events.discovered == [(1001, "first")]
    assert events.queue_counts[-1] == 1


async def test_backlog_draining_status_after_successful_enqueue(spy_log) -> None:
    scraper = ScriptedScraper([summary(1), summary(2)])
    events = SpyEvents()
    poller, _queue = build_poller(scraper, PartitioningDiffEngine(), FakeStore(), events)

    enqueued = await poller.poll_once()

    assert enqueued == 2
    assert events.statuses == [
        poller_module.PollServiceStatus.POLLING,
        poller_module.PollServiceStatus.BACKLOG_DRAINING,
    ]
    assert events.scan_ok == [(2, 2)]


async def test_listing_failure_sets_error_logs_cycle_and_recovers_next_tick(
    spy_log,
) -> None:
    scraper = ScriptedScraper(http_error(), [])
    events = SpyEvents()
    poller, _queue = build_poller(scraper, StubDiffEngine(DiffResult()), FakeStore(), events)
    poller.poll_interval_seconds = 1

    cancel = asyncio.Event()
    await poller.start(cancel)
    await wait_until(lambda: len(events.scan_failed) >= 1)
    assert poller.status == poller_module.PollServiceStatus.ERROR
    assert events.scan_failed[0].code == "HTTP-001"
    await wait_until(lambda: len(scraper.calls) >= 2)
    await poller.stop()

    checkpoints = {f[0] for f in spy_log.failures}
    assert "PollService.FetchListing" in checkpoints
    assert "PollService.Cycle" in checkpoints
    assert events.statuses[-1] == poller_module.PollServiceStatus.IDLE


async def test_diff_failure_surfaces_as_diff_001_error(spy_log) -> None:
    scraper = ScriptedScraper([summary(1)])
    events = SpyEvents()
    boom = DiffStateError(DomainError("DIFF-001", "providers failed", "ar"))
    poller, _queue = build_poller(scraper, StubDiffEngine(boom), FakeStore(), events)

    with pytest.raises(DiffStateError):
        await poller.poll_once()

    assert poller.status == poller_module.PollServiceStatus.ERROR
    assert events.scan_failed[0].code == "DIFF-001"
    assert any(f[0] == "PollService.Diff" for f in spy_log.failures)


async def test_unexpected_exception_wrapped_as_poll_001(spy_log) -> None:
    scraper = ScriptedScraper(ValueError("parser exploded"))
    events = SpyEvents()
    poller, _queue = build_poller(scraper, StubDiffEngine(DiffResult()), FakeStore(), events)

    with pytest.raises(BackboneError) as err:
        await poller.poll_once()

    assert err.value.error.code == "POLL-001"
    assert any(f[0] == "PollService.Unexpected" for f in spy_log.failures)


async def test_missing_summary_logged_and_skipped(spy_log) -> None:
    scraper = ScriptedScraper([summary(1001)])
    events = SpyEvents()
    ghost = DiffResult(new_project_ids=[777])
    poller, queue = build_poller(scraper, StubDiffEngine(ghost), FakeStore(), events)

    enqueued = await poller.poll_once()

    assert enqueued == 0
    assert queue.count == 0
    assert any(m[0] == "PollService.MissingSummary" for m in spy_log.marks)


async def test_parent_cancel_terminates_the_loop(spy_log) -> None:
    scraper = ScriptedScraper([])
    events = SpyEvents()
    poller, _queue = build_poller(scraper, StubDiffEngine(DiffResult()), FakeStore(), events)
    poller.poll_interval_seconds = 3600
    parent = asyncio.Event()

    await poller.start(parent)
    await wait_until(lambda: len(scraper.calls) >= 1)
    parent.set()
    await asyncio.wait_for(poller.stop(), timeout=3.0)

    assert poller.status == poller_module.PollServiceStatus.IDLE


async def test_request_check_now_is_idempotent(spy_log) -> None:
    scraper = ScriptedScraper([])
    events = SpyEvents()
    poller, _queue = build_poller(scraper, StubDiffEngine(DiffResult()), FakeStore(), events)
    poller.paused = True
    poller.poll_interval_seconds = 3600

    cancel = asyncio.Event()
    await poller.start(cancel)
    poller.request_check_now()
    poller.request_check_now()  # second press must not queue a duplicate burst
    await wait_until(lambda: len(scraper.calls) == 1)
    await asyncio.sleep(0.05)
    await poller.stop()

    assert len(scraper.calls) == 1

"""EnrichmentWorker + WorkerPool behavioral tests against the C# WorkerPool/* spec."""

import asyncio
from collections.abc import Awaitable, Callable
from datetime import UTC, datetime

import pytest

import mostaql.pipeline.worker as worker_module
from mostaql.errors import (
    BackboneError,
    DomainError,
    HttpRequestError,
    StoreOperationError,
    poll_listing_fetch_failed,
    store_query_failed,
)
from mostaql.models import Owner, ProjectDetails, ProjectSkill
from mostaql.pipeline.enrich import EnrichmentService
from mostaql.pipeline.inflight import InFlightTracker
from mostaql.pipeline.pool import WorkerPool
from mostaql.pipeline.queue import DiscoveryQueue
from mostaql.pipeline.ratelimit import TokenBucketRateLimiter
from mostaql.pipeline.worker import RETRY_DELAYS_SECONDS, EnrichmentWorker

RETRY_LADDER = RETRY_DELAYS_SECONDS


class SpyEvents:
    """Records every PipelineEvents callback (plan §8 surface)."""

    def __init__(self) -> None:
        self.worker_states: list[tuple[int, str]] = []
        self.queue_counts: list[int] = []
        self.discovered: list[tuple[int, str]] = []
        self.enriched: list[ProjectDetails] = []
        self.statuses: list[object] = []
        self.scan_ok: list[tuple[int, int]] = []
        self.scan_failed: list[DomainError] = []

    def on_worker_state(self, worker_id: int, state: str) -> None:
        self.worker_states.append((worker_id, state))

    def on_queue_count_changed(self, count: int) -> None:
        self.queue_counts.append(count)

    def on_project_discovered(self, project_id: int, title: str) -> None:
        self.discovered.append((project_id, title))

    def on_enriched(self, details: ProjectDetails) -> None:
        self.enriched.append(details)

    def on_status_changed(self, status: object) -> None:
        self.statuses.append(status)

    def on_scan_succeeded(self, seen: int, enqueued: int) -> None:
        self.scan_ok.append((seen, enqueued))

    def on_scan_failed(self, error: DomainError) -> None:
        self.scan_failed.append(error)


class FakeStore:
    """In-memory ProjectStore slice recording the exact operation order."""

    def __init__(self) -> None:
        self.timeline: list[tuple[str, int]] = []
        self.backlog: set[int] = set()
        self.details: dict[int, ProjectDetails] = {}
        self.owners: dict[int, Owner] = {}
        self.known_ids: set[int] = set()

    async def insert_summary(self, s: ProjectDetails) -> bool:  # type: ignore[type-arg]
        new = s.project_id not in self.known_ids
        self.timeline.append(("insert_summary", s.project_id))
        self.known_ids.add(s.project_id)
        return new

    async def upsert_details(self, d: ProjectDetails) -> None:
        self.timeline.append(("upsert_details", d.project_id))
        self.details[d.project_id] = d
        self.known_ids.add(d.project_id)

    async def upsert_owner(self, o: Owner) -> None:
        self.timeline.append(("upsert_owner", o.owner_id))
        self.owners[o.owner_id] = o

    async def get_all_known_project_ids(self) -> set[int]:
        return set(self.known_ids)

    async def add_to_backlog(self, project_id: int) -> None:
        self.timeline.append(("add_backlog", project_id))
        self.backlog.add(project_id)

    async def remove_from_backlog(self, project_id: int) -> None:
        self.timeline.append(("remove_backlog", project_id))
        self.backlog.discard(project_id)

    async def get_backlog_ids(self) -> list[int]:
        return sorted(self.backlog)

    async def clean_old_backlog(self, days: int = 30) -> int:
        return 0


class ScriptedScraper:
    """DetailFetcher double with per-id scripted outcomes (exception or details)."""

    def __init__(self) -> None:
        self.plan: dict[int, list[object]] = {}
        self.calls: list[int] = []
        self.active = 0
        self.max_active = 0

    def script(self, pid: int, *outcomes: object) -> None:
        self.plan[pid] = list(outcomes)

    async def fetch_project_details(self, project_id: int) -> ProjectDetails:
        self.calls.append(project_id)
        self.active += 1
        self.max_active = max(self.max_active, self.active)
        try:
            seq = self.plan.get(project_id)
            if not seq:
                raise AssertionError(f"no scripted outcome for {project_id}")
            outcome = seq.pop(0)
            if isinstance(outcome, BaseException):
                raise outcome
            return outcome  # type: ignore[no-any-return]
        finally:
            self.active -= 1


class RecordingLogger:
    def __init__(self) -> None:
        self.marks: list[tuple[str, str, object]] = []
        self.failures: list[tuple[str, str, object]] = []

    def mark(self, checkpoint: str, variant: str, data: object = None) -> None:
        self.marks.append((checkpoint, variant, data))

    def fault(self, checkpoint: str, exc: BaseException, data: object = None) -> None:
        self.failures.append((checkpoint, type(exc).__name__, data))

    def failure(self, checkpoint: str, error: DomainError, data: object = None) -> None:
        self.failures.append((checkpoint, error.code, data))


@pytest.fixture
def spy_log(monkeypatch: pytest.MonkeyPatch) -> RecordingLogger:
    recorder = RecordingLogger()
    monkeypatch.setattr(worker_module, "get_interaction_logger", lambda: recorder)
    return recorder


def make_details(
    pid: int,
    title: str = "t",
    owner: Owner | None = None,
    skills: list[ProjectSkill] | None = None,
) -> ProjectDetails:
    return ProjectDetails(
        project_id=pid,
        title=title,
        owner=owner if owner is not None else Owner(),
        skills=skills if skills is not None else [],
        discovered_at=datetime(2026, 1, 1, tzinfo=UTC),
    )


def http_error(message: str) -> HttpRequestError:
    return HttpRequestError(DomainError("HTTP-001", message, message))


def pacing_sleep(record: list[float]) -> Callable[[float], Awaitable[None]]:
    async def sleep(delay: float) -> None:
        record.append(delay)
        await asyncio.sleep(min(delay, 0.001))

    return sleep


def build_pool(
    store: FakeStore,
    scraper: ScriptedScraper,
    events: SpyEvents,
    worker_count: int = 2,
    sleep: Callable[[float], Awaitable[None]] | None = None,
    retry_delays: tuple[float, ...] = RETRY_LADDER,
) -> tuple[WorkerPool, DiscoveryQueue, InFlightTracker]:
    queue: DiscoveryQueue = DiscoveryQueue()
    tracker = InFlightTracker()
    limiter = TokenBucketRateLimiter(requests_per_minute=6000, safe_requests=False)
    service = EnrichmentService(limiter, scraper)
    pool = WorkerPool(
        queue,
        service,
        tracker,
        store,
        events,
        worker_count=worker_count,
        sleep=sleep if sleep is not None else asyncio.sleep,
        retry_delays=retry_delays,
    )
    return pool, queue, tracker


async def wait_until(predicate: Callable[[], bool], timeout: float = 5.0) -> None:
    deadline = asyncio.get_running_loop().time() + timeout
    while not predicate():
        if asyncio.get_running_loop().time() > deadline:
            raise AssertionError("condition not reached before timeout")
        await asyncio.sleep(0.005)


async def test_retry_ladder_sleeps_between_attempts_then_succeeds(
    spy_log: RecordingLogger,
) -> None:
    store = FakeStore()
    enrichment = ScriptedScraper()
    events = SpyEvents()
    sleeps: list[float] = []
    pool, queue, tracker = build_pool(
        store,
        enrichment,
        events,
        sleep=pacing_sleep(sleeps),
        retry_delays=(0.02, 0.05, 0.09),
    )
    cancel = asyncio.Event()
    details = make_details(1, owner=Owner(name="o"))
    enrichment.script(1, http_error("boom"), http_error("boom2"), details)
    tracker.try_mark_in_flight(1)
    await store.add_to_backlog(1)
    await queue.enqueue(1)
    queue.complete()

    await pool.start(cancel)
    await wait_until(lambda: len(events.enriched) == 1)
    await pool.stop()

    assert sleeps[:2] == [0.02, 0.05]
    assert [f for f in spy_log.failures if f[0] == "EnrichmentWorker.Attempt"][-1][1] == "HTTP-001"
    assert ("remove_backlog", 1) in store.timeline
    assert events.enriched == [details]
    assert any(
        (wid, "processing") in events.worker_states and (wid, "completed") in events.worker_states
        for wid in range(2)
    )
    assert tracker.snapshot() == set()


async def test_max_attempts_exhausted_keeps_row_pending_but_clears_backlog(
    spy_log: RecordingLogger,
) -> None:
    store = FakeStore()
    enrichment = ScriptedScraper()
    events = SpyEvents()
    pool, queue, tracker = build_pool(
        store, enrichment, events, sleep=pacing_sleep([]), retry_delays=(0.0, 0.0)
    )
    cancel = asyncio.Event()
    enrichment.script(5, http_error("e1"), http_error("e2"))
    enrichment.script(6, make_details(6))
    tracker.try_mark_in_flight(5)
    tracker.try_mark_in_flight(6)
    await store.add_to_backlog(5)
    await store.add_to_backlog(6)
    await queue.enqueue(5)
    await queue.enqueue(6)
    queue.complete()

    await pool.start(cancel)
    await wait_until(lambda: len(enrichment.calls) >= 3)
    await pool.stop()

    exhausted = [f for f in spy_log.failures if f[0] == "EnrichmentWorker.MaxAttemptsExhausted"]
    assert exhausted and exhausted[0][1] == "ENRICH-001"
    assert any((wid, "error") in events.worker_states for wid in range(2))
    # C# nuance: normal return path removes the backlog entry even on exhaustion...
    assert ("remove_backlog", 5) in store.timeline
    assert 5 not in store.backlog
    # ...but the row was never persisted as Enriched -- it stays Pending.
    assert 5 not in store.details
    assert 5 not in store.known_ids
    # The worker survived and processed the next id.
    assert len(events.enriched) == 1 and events.enriched[0].project_id == 6


async def test_unexpected_exception_logs_enrich_002_and_keeps_backlog(
    spy_log: RecordingLogger,
) -> None:
    store = FakeStore()
    enrichment = ScriptedScraper()
    events = SpyEvents()
    pool, queue, tracker = build_pool(store, enrichment, events, sleep=pacing_sleep([]))
    cancel = asyncio.Event()
    enrichment.script(7, ValueError("not a backbone error"))
    enrichment.script(8, make_details(8))
    tracker.try_mark_in_flight(7)
    tracker.try_mark_in_flight(8)
    await store.add_to_backlog(7)
    await store.add_to_backlog(8)
    await queue.enqueue(7)
    await queue.enqueue(8)
    queue.complete()

    await pool.start(cancel)
    await wait_until(lambda: len(events.enriched) == 1)
    await pool.stop()

    unexpected = [f for f in spy_log.failures if f[0] == "EnrichmentWorker.Unexpected"]
    assert unexpected and unexpected[0][1] == "ENRICH-002"
    # Restart-retry nuance: backlog KEPT when an exception escapes ProcessAsync.
    assert ("remove_backlog", 7) not in store.timeline
    assert 7 in store.backlog
    # In-flight ID released by finally; worker continued to the next id.
    assert tracker.snapshot() == set()
    assert events.enriched[0].project_id == 8


async def test_owner_upsert_gated_on_name_or_id(spy_log: RecordingLogger) -> None:
    store = FakeStore()
    enrichment = ScriptedScraper()
    events = SpyEvents()
    pool, queue, tracker = build_pool(store, enrichment, events, sleep=pacing_sleep([]))
    cancel = asyncio.Event()
    anonymous = make_details(10, owner=Owner(name="", owner_id=0))
    named = make_details(11, owner=Owner(name="خالد"))
    id_only = make_details(12, owner=Owner(name="", owner_id=77))
    enrichment.script(10, anonymous)
    enrichment.script(11, named)
    enrichment.script(12, id_only)
    for pid in (10, 11, 12):
        tracker.try_mark_in_flight(pid)
        await store.add_to_backlog(pid)
        await queue.enqueue(pid)
    queue.complete()

    await pool.start(cancel)
    await wait_until(lambda: len(events.enriched) == 3)
    await pool.stop()

    upserted = {owner.name or owner.owner_id for owner in store.owners.values()}
    assert any(owner.name == "خالد" for owner in store.owners.values())
    assert any(owner.owner_id == 77 for owner in store.owners.values())
    assert len(upserted) == 2  # the fully-anonymous owner never hit the store
    assert ("upsert_details", 10) in store.timeline


async def test_store_operation_error_swallowed_and_logged_but_on_enriched_fires(
    spy_log: RecordingLogger,
) -> None:
    class BrokenDetailsStore(FakeStore):
        async def upsert_details(self, d: ProjectDetails) -> None:
            raise StoreOperationError(store_query_failed("UpsertDetailsAsync", RuntimeError()))

    store = BrokenDetailsStore()
    enrichment = ScriptedScraper()
    events = SpyEvents()
    pool, queue, tracker = build_pool(store, enrichment, events, sleep=pacing_sleep([]))
    cancel = asyncio.Event()
    enrichment.script(13, make_details(13))
    tracker.try_mark_in_flight(13)
    await store.add_to_backlog(13)
    await queue.enqueue(13)
    queue.complete()

    await pool.start(cancel)
    await wait_until(lambda: bool(events.enriched))
    await pool.stop()

    failed = [f for f in spy_log.failures if f[0] == "EnrichmentWorker.UpsertFailed"]
    assert failed and failed[0][1] == "DB-002"
    assert events.enriched and events.enriched[0].project_id == 13
    assert ("remove_backlog", 13) in store.timeline


async def test_pool_rehydrates_seeded_backlog_into_workers(spy_log: RecordingLogger) -> None:
    store = FakeStore()
    await store.add_to_backlog(20)
    await store.add_to_backlog(21)
    enrichment = ScriptedScraper()
    enrichment.script(20, make_details(20))
    enrichment.script(21, make_details(21))
    events = SpyEvents()
    pool, _queue, _tracker = build_pool(store, enrichment, events, sleep=pacing_sleep([]))
    cancel = asyncio.Event()

    await pool.start(cancel)
    # Re-hydrated items fire discovery pulses with an EMPTY title (no summary yet).
    await wait_until(lambda: len(events.discovered) == 2)
    await pool.stop()

    assert dict(events.discovered) == {20: "", 21: ""}
    assert {d.project_id for d in events.enriched} == {20, 21}
    assert store.backlog == set()


async def test_stop_drains_buffered_items_before_workers_exit(
    spy_log: RecordingLogger,
) -> None:
    store = FakeStore()
    enrichment = ScriptedScraper()
    for pid in range(10):
        enrichment.script(pid, make_details(pid))
    events = SpyEvents()
    pool, queue, _tracker = build_pool(
        store, enrichment, events, worker_count=2, sleep=pacing_sleep([])
    )
    cancel = asyncio.Event()
    await pool.start(cancel)

    for pid in range(10):
        await queue.enqueue(pid)
    await wait_until(lambda: len(enrichment.calls) >= 4)

    await asyncio.wait_for(pool.stop(), timeout=10.0)

    assert sorted(d.project_id for d in events.enriched) == list(range(10))
    assert store.backlog == set()
    assert enrichment.max_active <= 2


async def test_bounded_concurrency_never_exceeds_worker_count(
    spy_log: RecordingLogger,
) -> None:
    store = FakeStore()
    enrichment = ScriptedScraper()
    for pid in range(6):
        enrichment.script(pid, make_details(pid))
    events = SpyEvents()
    pool, queue, _tracker = build_pool(store, enrichment, events, worker_count=2)
    cancel = asyncio.Event()
    await pool.start(cancel)

    for pid in range(6):
        await queue.enqueue(pid)
    await wait_until(lambda: len(enrichment.calls) == 6)
    await pool.stop()

    assert enrichment.max_active <= 2


async def test_idle_state_timer_fires_after_delay_when_not_cancelled(
    spy_log: RecordingLogger,
) -> None:
    store = FakeStore()
    enrichment = ScriptedScraper()
    enrichment.script(30, make_details(30))
    events = SpyEvents()
    sleeps: list[float] = []
    pool, queue, tracker = build_pool(store, enrichment, events, sleep=pacing_sleep(sleeps))
    cancel = asyncio.Event()
    tracker.try_mark_in_flight(30)
    await store.add_to_backlog(30)
    await queue.enqueue(30)
    queue.complete()

    await pool.start(cancel)

    def idle_fired() -> bool:
        return any((wid, "idle") in events.worker_states for wid in range(2))

    await wait_until(idle_fired)
    await pool.stop()

    assert 2.0 in sleeps  # the delayed-idle timer used the injected sleep seam


async def test_default_retry_delays_match_csharp_ladder() -> None:
    assert RETRY_DELAYS_SECONDS == (60, 120, 240, 480, 900)


async def test_worker_run_returns_once_queue_completed_and_drained(
    spy_log: RecordingLogger,
) -> None:
    queue = DiscoveryQueue()
    enrichment = ScriptedScraper()
    enrichment.script(40, make_details(40))
    worker = EnrichmentWorker(
        0,
        queue,
        EnrichmentService(TokenBucketRateLimiter(requests_per_minute=600), enrichment),  # type: ignore[arg-type]
        InFlightTracker(),
        FakeStore(),
        SpyEvents(),
        sleep=pacing_sleep([]),
    )
    tracker_claim = InFlightTracker()
    tracker_claim.try_mark_in_flight(40)
    await queue.enqueue(40)
    queue.complete()

    await asyncio.wait_for(worker.run(asyncio.Event()), timeout=5.0)

    assert enrichment.calls == [40]


async def test_backbone_error_import_surface_available() -> None:
    """Sanity: the typed error family the worker relies on stays importable."""
    assert issubclass(HttpRequestError, BackboneError)
    assert issubclass(StoreOperationError, BackboneError)
    assert poll_listing_fetch_failed(RuntimeError()).code == "POLL-001"

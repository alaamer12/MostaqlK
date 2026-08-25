"""End-to-end pipeline: real SQLite + real scraper over httpx.MockTransport fixtures.

Exercises the full orchestration (poller -> diff -> queue -> workers -> store) with the
real ``MostaqlScraper``/``PageFetcher`` stack fed captured HTML, plus the crash-restart
re-hydration scenario against an always-404 transport.
"""

import asyncio
from collections.abc import Awaitable, Callable
from datetime import UTC, datetime
from pathlib import Path

import httpx

import mostaql.scraping.scraper as scraper_module
from mostaql.http import PageFetcher
from mostaql.models import EnrichmentStatus, ProjectSummary
from mostaql.pipeline.diff import CommittedIdsProvider, DiffEngine, InFlightSetProvider
from mostaql.pipeline.enrich import EnrichmentService
from mostaql.pipeline.inflight import InFlightTracker
from mostaql.pipeline.poller import PollService, PollServiceStatus
from mostaql.pipeline.pool import WorkerPool
from mostaql.pipeline.queue import DiscoveryQueue
from mostaql.pipeline.ratelimit import TokenBucketRateLimiter
from mostaql.storage.sqlite_store import SQLiteStore

FIXTURES = Path(__file__).resolve().parents[1] / "regression" / "fixtures"
LISTING_HTML = (FIXTURES / "listing" / "table_rows.html").read_text(encoding="utf-8")
DETAIL_HTML = (FIXTURES / "detail" / "owner_hash.html").read_text(encoding="utf-8")

EXPECTED_IDS = {1001, 2002, 3003}


class SpyEvents:
    """Records every PipelineEvents callback (plan §8 surface)."""

    def __init__(self) -> None:
        self.discovered: list[tuple[int, str]] = []
        self.enriched: list[ProjectSummary] = []
        self.worker_states: list[tuple[int, str]] = []
        self.scan_failed: list[object] = []

    def on_worker_state(self, worker_id: int, state: str) -> None:
        self.worker_states.append((worker_id, state))

    def on_queue_count_changed(self, count: int) -> None:
        return None

    def on_project_discovered(self, project_id: int, title: str) -> None:
        self.discovered.append((project_id, title))

    def on_enriched(self, details: ProjectSummary) -> None:
        self.enriched.append(details)

    def on_status_changed(self, status: PollServiceStatus) -> None:
        return None

    def on_scan_succeeded(self, seen: int, enqueued: int) -> None:
        return None

    def on_scan_failed(self, error: object) -> None:
        self.scan_failed.append(error)


async def wait_until(
    predicate: Callable[[], bool] | Callable[[], Awaitable[bool]], timeout: float = 20.0
) -> None:
    deadline = asyncio.get_running_loop().time() + timeout
    while True:
        outcome = predicate()
        if isinstance(outcome, Awaitable):
            outcome = await outcome
        if outcome:
            return
        if asyncio.get_running_loop().time() > deadline:
            raise AssertionError("condition not reached before timeout")
        await asyncio.sleep(0.01)


def fast_sleep() -> Callable[[float], Awaitable[None]]:
    async def sleep(delay: float) -> None:
        await asyncio.sleep(min(delay, 0.001))

    return sleep


def tracking_handler(counter: dict[str, int]) -> Callable[[httpx.Request], Awaitable[object]]:
    async def handler(request: httpx.Request) -> httpx.Response:
        counter["active"] += 1
        counter["max"] = max(counter["max"], counter["active"])
        try:
            await asyncio.sleep(0.005)
            path = request.url.path
            if path == "/projects":
                return httpx.Response(200, text=LISTING_HTML)
            if path.startswith("/project/"):
                return httpx.Response(200, text=DETAIL_HTML)
            return httpx.Response(404, text="nope")
        finally:
            counter["active"] -= 1

    return handler


def not_found_handler(request: httpx.Request) -> httpx.Response:
    return httpx.Response(404, text="gone")


def build_stack(
    store: SQLiteStore,
    fetcher: PageFetcher,
    events: SpyEvents,
    worker_count: int = 2,
    retry_delays: tuple[float, ...] = (60.0, 120.0, 240.0, 480.0, 900.0),
) -> tuple[WorkerPool, PollService, DiscoveryQueue, InFlightTracker]:
    queue = DiscoveryQueue()
    tracker = InFlightTracker()
    limiter = TokenBucketRateLimiter(requests_per_minute=600, safe_requests=False)
    scraper = scraper_module.MostaqlScraper(fetcher)
    diff_engine = DiffEngine(CommittedIdsProvider(store), InFlightSetProvider(tracker))
    enrichment = EnrichmentService(limiter, scraper)
    pool = WorkerPool(
        queue,
        enrichment,
        tracker,
        store,
        events,
        worker_count=worker_count,
        sleep=fast_sleep(),
        retry_delays=retry_delays,
    )
    poller = PollService(scraper, diff_engine, queue, tracker, store, limiter, events)
    poller.poll_interval_seconds = 3600
    return pool, poller, queue, tracker


async def test_full_pipeline_discovery_through_fts_and_graceful_stop(
    tmp_path: Path,
) -> None:
    store = SQLiteStore(tmp_path / "pipeline.db")
    counter = {"active": 0, "max": 0}
    client = httpx.AsyncClient(transport=httpx.MockTransport(tracking_handler(counter)))
    events = SpyEvents()
    pool, poller, _queue, tracker = build_stack(store, PageFetcher(client), events)
    cancel = asyncio.Event()

    try:
        await pool.start(cancel)
        await poller.start(cancel)

        async def backlog_empty() -> bool:
            return not await store.get_backlog_ids()

        await wait_until(lambda: len(events.enriched) == 3)
        await wait_until(backlog_empty)

        assert sorted(d.project_id for d in events.enriched) == sorted(EXPECTED_IDS)
        recent = await store.get_recent(10)
        assert {s.project_id for s in recent} == EXPECTED_IDS
        assert all(s.enrichment_status == EnrichmentStatus.ENRICHED for s in recent)

        hits = store.search("Illustrator")
        assert hits and hits[0].skills_text == "Illustrator"

        assert await store.get_backlog_ids() == []
        assert tracker.snapshot() == set()
        assert counter["max"] <= 3  # two workers + at most one listing fetch in flight

        await asyncio.wait_for(poller.stop(), timeout=5.0)
        await asyncio.wait_for(pool.stop(), timeout=10.0)
    finally:
        await client.aclose()
        store.close()


async def test_crash_restart_rehydrates_and_exhausts_to_pending(tmp_path: Path) -> None:
    db_path = tmp_path / "restart.db"
    store = SQLiteStore(db_path)

    # Simulate a crash right after discovery: summary persisted + backlog row written,
    # but no worker ever processed the id.
    residue = ProjectSummary(
        project_id=5005,
        title="مشروع غير مكتمل",
        discovered_at=datetime(2026, 1, 2, tzinfo=UTC),
    )
    assert await store.insert_summary(residue) is True
    await store.add_to_backlog(5005)
    store.close()

    restarted_store = SQLiteStore(db_path)
    client = httpx.AsyncClient(transport=httpx.MockTransport(not_found_handler))
    events = SpyEvents()
    pool, _poller, _queue, tracker = build_stack(
        restarted_store, PageFetcher(client), events, retry_delays=(0.0,) * 5
    )
    cancel = asyncio.Event()

    try:
        await pool.start(cancel)

        async def restart_backlog_empty() -> bool:
            return not await restarted_store.get_backlog_ids()

        await wait_until(restart_backlog_empty)
        await wait_until(lambda: not tracker.snapshot())
        await pool.stop()

        recent = await restarted_store.get_recent(10)
        target = next(s for s in recent if s.project_id == 5005)
        # ENRICH-001 exhausted instantly; the row stays Pending while backlog cleared.
        assert target.enrichment_status == EnrichmentStatus.PENDING
        assert any(state == "error" for _wid, state in events.worker_states)
        assert await restarted_store.get_backlog_ids() == []
    finally:
        await client.aclose()
        restarted_store.close()

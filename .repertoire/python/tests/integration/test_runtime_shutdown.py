"""Runtime lifecycle integration tests: shutdown semantics of ``run_pipeline``.

C# parity targets: ``App.RequestPipelineShutdown`` cancels both loops while
``WorkerPool.StopAsync`` drains what is already queued before workers exit. All
scenarios drive the shared ``stop`` event directly -- no OS signals anywhere
(Windows-first correctness).

Nuance note (C# backlog-removal rule): a backlog entry is removed only after its
processing path RETURNS NORMALLY. Under graceful drain a mid-flight fetch therefore
completes and clears its own backlog row; an entry can genuinely outlive a shutdown
only when its worker never finished successfully -- exactly the crash-residue shape
the second test replays through a real restart.
"""

import asyncio
from collections.abc import Iterator
from datetime import UTC, datetime
from pathlib import Path

import httpx
import pytest

from mostaql.config import Settings
from mostaql.diagnostics import interaction_log
from mostaql.models import EnrichmentStatus, ProjectSummary
from mostaql.runtime import run_pipeline
from mostaql.storage.sqlite_store import SQLiteStore

FIXTURES = Path(__file__).resolve().parents[1] / "regression" / "fixtures"
LISTING_HTML = (FIXTURES / "listing" / "table_rows.html").read_text(encoding="utf-8")
DETAIL_HTML = (FIXTURES / "detail" / "owner_hash.html").read_text(encoding="utf-8")
EXPECTED_IDS = {1001, 2002, 3003}
RESIDUE_ID = 7007

SINGLE_ROW_LISTING_HTML = """
<html><body><table><tbody>
<tr class="project-row"><td>
  <h2><a href="/project/7007-residue">مشروع متروك</a></h2>
</td></tr>
</tbody></table></body></html>
"""


@pytest.fixture(autouse=True)
def fresh_interaction_singleton() -> Iterator[None]:
    interaction_log._instance = None
    yield
    interaction_log._instance = None


def make_settings(tmp_path: Path) -> Settings:
    return Settings(
        db_path=tmp_path / "backbone.db",
        log_file_path=tmp_path / "logs" / "interaction-log.txt",
        poll_interval_seconds=3600,
        max_requests_per_minute=600,
        safe_requests=False,
        start_paused=False,
    )


def listing_handler(
    listing_html: str, detail_delay_seconds: float, started: dict[str, int] | None = None
):
    async def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/projects":
            return httpx.Response(200, text=listing_html)
        if request.url.path.startswith("/project/"):
            if started is not None:
                started["details"] += 1
            await asyncio.sleep(detail_delay_seconds)
            return httpx.Response(200, text=DETAIL_HTML)
        return httpx.Response(404, text="not found")

    return handler


async def cancel_stray_tasks() -> None:
    current = asyncio.current_task()
    strays = [task for task in asyncio.all_tasks() if task is not current]
    for task in strays:
        task.cancel()
    if strays:
        await asyncio.gather(*strays, return_exceptions=True)


async def test_stop_mid_flight_returns_promptly_and_drains(tmp_path: Path) -> None:
    """Shutdown during active enrichment: prompt exit, no stranded work afterwards."""
    settings = make_settings(tmp_path)
    started = {"details": 0}

    def factory() -> httpx.AsyncClient:
        return httpx.AsyncClient(
            transport=httpx.MockTransport(listing_handler(LISTING_HTML, 0.3, started))
        )

    async def stop_once_enrichment_started(stop: asyncio.Event) -> None:
        loop = asyncio.get_running_loop()
        deadline = loop.time() + 5.0
        while not started["details"] and loop.time() < deadline:
            await asyncio.sleep(0.01)
        stop.set()

    stop = asyncio.Event()
    trigger = asyncio.ensure_future(stop_once_enrichment_started(stop))
    began = asyncio.get_running_loop().time()
    code = await asyncio.wait_for(run_pipeline(settings, stop, client_factory=factory), timeout=10)
    elapsed = asyncio.get_running_loop().time() - began
    await trigger

    assert code == 0
    assert elapsed < 5.0

    # Graceful drain: every claimed ID finished processing -> Enriched rows, empty
    # backlog, hence nothing left in-flight (asserted via store state by design).
    store = SQLiteStore(settings.db_path)
    try:
        recent = await store.get_recent(10)
        assert {s.project_id for s in recent} == EXPECTED_IDS
        assert all(s.enrichment_status == EnrichmentStatus.ENRICHED for s in recent)
        assert await store.get_backlog_ids() == []
    finally:
        store.close()

    # Restart over the SAME database is a clean no-op rehydration.
    stop = asyncio.Event()
    stopper = asyncio.ensure_future(_stop_later(stop, 0.8))
    code = await asyncio.wait_for(run_pipeline(settings, stop, client_factory=factory), timeout=10)
    await stopper
    assert code == 0

    store = SQLiteStore(settings.db_path)
    try:
        recent = await store.get_recent(10)
        assert {s.project_id for s in recent} == EXPECTED_IDS
        assert all(s.enrichment_status == EnrichmentStatus.ENRICHED for s in recent)
        assert await store.get_backlog_ids() == []
    finally:
        store.close()

    await cancel_stray_tasks()


async def _stop_later(stop: asyncio.Event, delay: float) -> None:
    await asyncio.sleep(delay)
    stop.set()


async def test_residue_backlog_survives_until_restart_completes_it(tmp_path: Path) -> None:
    """Backlog removal happens ONLY after successful processing (C# nuance).

    A crash-shaped residue (Pending summary + live backlog row, as left behind when
    a worker never finished) must survive process boundaries untouched, then be
    completed by the next run's re-hydration.
    """
    settings = make_settings(tmp_path)

    seeder = SQLiteStore(settings.db_path)
    try:
        residue = ProjectSummary(
            project_id=RESIDUE_ID,
            title="مشروع متروك",
            discovered_at=datetime(2026, 1, 2, tzinfo=UTC),
        )
        assert await seeder.insert_summary(residue) is True
        await seeder.add_to_backlog(RESIDUE_ID)
        recent = await seeder.get_recent(10)
        assert recent[0].enrichment_status == EnrichmentStatus.PENDING
        assert await seeder.get_backlog_ids() == [RESIDUE_ID]
    finally:
        seeder.close()

    def factory() -> httpx.AsyncClient:
        return httpx.AsyncClient(
            transport=httpx.MockTransport(listing_handler(SINGLE_ROW_LISTING_HTML, 0.0))
        )

    stop = asyncio.Event()
    stopper = asyncio.ensure_future(_stop_later(stop, 1.5))
    code = await asyncio.wait_for(run_pipeline(settings, stop, client_factory=factory), timeout=10)
    await stopper
    assert code == 0

    store = SQLiteStore(settings.db_path)
    try:
        recent = await store.get_recent(10)
        assert {s.project_id for s in recent} == {RESIDUE_ID}
        assert all(s.enrichment_status == EnrichmentStatus.ENRICHED for s in recent)
        assert await store.get_backlog_ids() == []
    finally:
        store.close()

    await cancel_stray_tasks()

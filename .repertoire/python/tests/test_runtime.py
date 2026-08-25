"""Wave D runtime tests: events surface, CLI parsing, composition smoke.

Windows-safe by construction: shutdown is driven exclusively through the shared
``stop`` event -- no OS signals are ever raised from tests.
"""

import asyncio
from collections.abc import Iterator
from datetime import UTC, datetime
from pathlib import Path

import httpx
import pytest

from mostaql import __version__
from mostaql.config import Settings
from mostaql.diagnostics import interaction_log
from mostaql.errors import poll_listing_fetch_failed
from mostaql.models import EnrichmentStatus, ProjectDetails
from mostaql.pipeline.poller import PollServiceStatus
from mostaql.runtime import LoggingPipelineEvents, _install_signal_handlers, main, run_pipeline
from mostaql.storage.sqlite_store import SQLiteStore

FIXTURES = Path(__file__).resolve().parents[0] / "regression" / "fixtures"
LISTING_HTML = (FIXTURES / "listing" / "table_rows.html").read_text(encoding="utf-8")
DETAIL_HTML = (FIXTURES / "detail" / "owner_hash.html").read_text(encoding="utf-8")
EXPECTED_IDS = {1001, 2002, 3003}


@pytest.fixture(autouse=True)
def fresh_interaction_singleton() -> Iterator[None]:
    """Re-bind the process-wide interaction logger around every test."""
    interaction_log._instance = None
    yield
    interaction_log._instance = None


class BrokenLogger:
    """Every sink raises: LoggingPipelineEvents must contain the blast radius."""

    def mark(self, *args: object, **kwargs: object) -> None:
        raise RuntimeError("broken mark")

    def fault(self, *args: object, **kwargs: object) -> None:
        raise RuntimeError("broken fault")

    def failure(self, *args: object, **kwargs: object) -> None:
        raise RuntimeError("broken failure")


def sample_details(project_id: int = 42) -> ProjectDetails:
    return ProjectDetails(
        project_id=project_id,
        title="مشروع اختبار",
        discovered_at=datetime(2026, 1, 1, tzinfo=UTC),
    )


def make_settings(tmp_path: Path, **overrides: object) -> Settings:
    values: dict[str, object] = {
        "db_path": tmp_path / "backbone.db",
        "log_file_path": tmp_path / "logs" / "interaction-log.txt",
        "poll_interval_seconds": 1,
        "max_requests_per_minute": 600,
        "safe_requests": False,
        "start_paused": False,
    }
    values.update(overrides)
    return Settings(**values)


def fixture_handler(delay_seconds: float = 0.0, started: dict[str, int] | None = None):
    async def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/projects":
            return httpx.Response(200, text=LISTING_HTML)
        if request.url.path.startswith("/project/"):
            if started is not None:
                started["details"] += 1
            if delay_seconds:
                await asyncio.sleep(delay_seconds)
            return httpx.Response(200, text=DETAIL_HTML)
        return httpx.Response(404, text="not found")

    return handler


async def stop_after(stop: asyncio.Event, delay: float) -> None:
    await asyncio.sleep(delay)
    stop.set()


async def cancel_stray_tasks() -> None:
    """Kill leftover worker idle timers so no loop-close warnings can appear."""
    current = asyncio.current_task()
    strays = [task for task in asyncio.all_tasks() if task is not current]
    for task in strays:
        task.cancel()
    if strays:
        await asyncio.gather(*strays, return_exceptions=True)


# --- LoggingPipelineEvents ----------------------------------------------------------


def test_callbacks_never_raise_even_with_broken_logger() -> None:
    events = LoggingPipelineEvents(BrokenLogger())  # type: ignore[arg-type]
    details = sample_details()

    events.on_status_changed(PollServiceStatus.POLLING)
    events.on_project_discovered(7, "عنوان")
    events.on_queue_count_changed(3)
    events.on_scan_succeeded(10, 2)
    events.on_scan_failed(poll_listing_fetch_failed(RuntimeError("boom")))
    events.on_worker_state(1, "processing")
    events.on_enriched(details)


def test_callbacks_write_checkpoints_to_interaction_log(tmp_path: Path) -> None:
    log_path = tmp_path / "interaction-log.txt"
    interaction_log._instance = None
    interaction_log.get_interaction_logger(log_path)
    events = LoggingPipelineEvents()
    error = poll_listing_fetch_failed(RuntimeError("network down"))
    details = sample_details(project_id=99)

    events.on_status_changed(PollServiceStatus.BACKLOG_DRAINING)
    events.on_project_discovered(7, "عنوان")
    events.on_queue_count_changed(3)
    events.on_scan_succeeded(10, 2)
    events.on_scan_failed(error)
    events.on_worker_state(1, "processing")
    events.on_enriched(details)

    lines = log_path.read_text(encoding="utf-8").splitlines()
    joined = "\n".join(lines)
    for checkpoint in (
        "Pipeline.StatusChanged",
        "Pipeline.ProjectDiscovered",
        "Pipeline.QueueCountChanged",
        "Pipeline.ScanSucceeded",
        "Pipeline.ScanFailed",
        "Pipeline.WorkerStateChanged",
        "Pipeline.ProjectEnriched",
    ):
        assert checkpoint in joined
    scan_failed = next(line for line in lines if "Pipeline.ScanFailed" in line)
    assert "| ERROR |" in scan_failed
    assert "variant=POLL-001" in scan_failed
    enriched = next(line for line in lines if "Pipeline.ProjectEnriched" in line)
    # json.dumps escapes non-ASCII (ensure_ascii default) — assert the stable parts.
    assert '"project_id":99' in enriched and '"title":' in enriched


# --- CLI argument parsing ------------------------------------------------------------


def test_help_exits_zero_without_running_pipeline(capsys: pytest.CaptureFixture[str]) -> None:
    with pytest.raises(SystemExit) as excinfo:
        asyncio.run(main(["--help"]))
    assert excinfo.value.code == 0
    assert "--config" in capsys.readouterr().out


def test_version_exits_zero(capsys: pytest.CaptureFixture[str]) -> None:
    with pytest.raises(SystemExit) as excinfo:
        asyncio.run(main(["--version"]))
    assert excinfo.value.code == 0
    assert f"mostaql {__version__}" in capsys.readouterr().out


def test_unknown_flag_exits_two() -> None:
    with pytest.raises(SystemExit) as excinfo:
        asyncio.run(main(["--bogus"]))
    assert excinfo.value.code == 2


def test_cli_propagates_main_exit_code(monkeypatch: pytest.MonkeyPatch) -> None:
    import mostaql.runtime as runtime_module

    async def fake_main(argv: object = None) -> int:
        return 7

    monkeypatch.setattr(runtime_module, "main", fake_main)
    with pytest.raises(SystemExit) as excinfo:
        runtime_module.cli()
    assert excinfo.value.code == 7


def test_cli_maps_keyboard_interrupt_to_130(monkeypatch: pytest.MonkeyPatch) -> None:
    import mostaql.runtime as runtime_module

    async def interrupting_main(argv: object = None) -> int:
        raise KeyboardInterrupt

    monkeypatch.setattr(runtime_module, "main", interrupting_main)
    with pytest.raises(SystemExit) as excinfo:
        runtime_module.cli()
    assert excinfo.value.code == 130


def test_unreadable_config_file_returns_two(tmp_path: Path) -> None:
    code = asyncio.run(main(["--config", str(tmp_path / "missing.toml")]))
    assert code == 2


# --- Signal handler installation (installation/undo only; no signals fired) ---------


async def test_signal_handlers_install_and_undo_without_raising() -> None:
    loop = asyncio.get_running_loop()
    stop = asyncio.Event()
    undos = _install_signal_handlers(loop, stop)
    try:
        assert len(undos) >= 1
    finally:
        for undo in undos:
            undo()


# --- run_pipeline composition smoke ---------------------------------------------------


async def test_run_pipeline_discovers_enriches_and_rehydrates_cleanly(tmp_path: Path) -> None:
    settings = make_settings(tmp_path)

    def factory() -> httpx.AsyncClient:
        return httpx.AsyncClient(transport=httpx.MockTransport(fixture_handler()))

    stop = asyncio.Event()
    stopper = asyncio.ensure_future(stop_after(stop, 1.5))
    code = await asyncio.wait_for(run_pipeline(settings, stop, client_factory=factory), timeout=15)
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

    # Second run over the SAME database: rehydrates cleanly, nothing new, nothing stuck.
    stop = asyncio.Event()
    stopper = asyncio.ensure_future(stop_after(stop, 0.8))
    code = await asyncio.wait_for(run_pipeline(settings, stop, client_factory=factory), timeout=15)
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

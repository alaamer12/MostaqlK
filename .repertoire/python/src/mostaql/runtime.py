"""Composition root and process lifecycle (Wave D): wiring only, zero business logic.

Python-side replacement for the C# ``MauiProgram`` DI composition plus the
``App.StartPipeline`` / ``App.RequestPipelineShutdown`` pair: build ONE pipeline stack
over a single anonymous HTTP client, apply persisted settings onto the poller, serve
until the shared ``stop`` event fires, then tear down poller-first / pool-second with
queue-drain semantics identical to C# ``WorkerPool.StopAsync``.

Signal handling degrades gracefully in both directions: on POSIX the loop installs
native handlers for SIGINT and SIGTERM; on Windows (``add_signal_handler`` unsupported)
SIGINT falls back to ``signal.signal`` bridged via ``loop.call_soon_threadsafe`` --
external SIGTERM delivery does not exist on Windows, so its fallback registration is
best-effort only. KeyboardInterrupt never reaches the serving coroutine: ``cli()``
maps it to exit code 130.
"""

import argparse
import asyncio
import logging
import os
import signal
from collections.abc import Callable, Sequence
from contextlib import suppress
from pathlib import Path
from types import FrameType
from typing import Any, cast

from mostaql import __version__
from mostaql.config import ConfigError, Settings, load_settings
from mostaql.diagnostics.interaction_log import (
    FailureLike,
    InteractionLogger,
    get_interaction_logger,
)
from mostaql.errors import DomainError
from mostaql.http import PageFetcher, build_default_client
from mostaql.models import ProjectDetails
from mostaql.pipeline.diff import CommittedIdsProvider, DiffEngine, InFlightSetProvider
from mostaql.pipeline.enrich import EnrichmentService
from mostaql.pipeline.inflight import InFlightTracker
from mostaql.pipeline.poller import PollService, PollServiceStatus
from mostaql.pipeline.pool import WorkerPool
from mostaql.pipeline.queue import DiscoveryQueue
from mostaql.pipeline.ratelimit import TokenBucketRateLimiter
from mostaql.scraping.scraper import MostaqlScraper
from mostaql.storage.sqlite_store import SQLiteStore

__all__ = ["LoggingPipelineEvents", "cli", "main", "run_pipeline"]

_LOGGER = logging.getLogger("mostaql")

_WORKER_COUNT = 3
_DEFAULT_CONFIG_FILE_NAME = "mostaql.toml"
_CONFIG_EXIT_CODE = 2
_KEYBOARD_INTERRUPT_EXIT_CODE = 130


class LoggingPipelineEvents:
    """Every pipeline event as an interaction-log line; never raises, zero UI coupling.

    Implements the exact union surface the pipeline layer calls today: the poller's
    five callbacks (:class:`mostaql.pipeline.poller.PollEvents`) plus the pool/workers'
    four (:class:`mostaql.pipeline.pool.WorkerPoolEvents` /
    :class:`mostaql.pipeline.worker.WorkerEvents`, three of them overlapping).
    Each checkpoint replaces the C# ``GlobalAppStatusService`` observation point at
    the same call site (plan §12 ledger 11/12). ``on_enriched`` logs the project
    id+title at the INFO-equivalent MARK level and is the designated future
    notification hook point (ledger 5).
    """

    def __init__(self, logger: InteractionLogger | None = None) -> None:
        self._logger = logger if logger is not None else get_interaction_logger()

    def on_status_changed(self, status: PollServiceStatus) -> None:
        with suppress(Exception):
            self._logger.mark("Pipeline.StatusChanged", status.value)

    def on_project_discovered(self, project_id: int, title: str) -> None:
        with suppress(Exception):
            self._logger.mark(
                "Pipeline.ProjectDiscovered",
                "A",
                {"project_id": project_id, "title": title},
            )

    def on_queue_count_changed(self, count: int) -> None:
        with suppress(Exception):
            self._logger.mark("Pipeline.QueueCountChanged", "A", {"count": count})

    def on_scan_succeeded(self, seen: int, enqueued: int) -> None:
        with suppress(Exception):
            self._logger.mark("Pipeline.ScanSucceeded", "A", {"seen": seen, "enqueued": enqueued})

    def on_scan_failed(self, error: DomainError) -> None:
        with suppress(Exception):
            self._logger.failure("Pipeline.ScanFailed", cast(FailureLike, error))

    def on_worker_state(self, worker_id: int, state: str) -> None:
        with suppress(Exception):
            self._logger.mark(
                "Pipeline.WorkerStateChanged",
                "A",
                {"worker_id": worker_id, "state": state},
            )

    def on_enriched(self, details: ProjectDetails) -> None:
        with suppress(Exception):
            self._logger.mark(
                "Pipeline.ProjectEnriched",
                "A",
                {"project_id": details.project_id, "title": details.title},
            )


def _configure_logging(level_name: str) -> None:
    """Bind stdlib logging once (``basicConfig`` self-guards) and set the level."""
    level = logging.getLevelNamesMapping().get(level_name.upper(), logging.INFO)
    logging.basicConfig(level=level)
    _LOGGER.setLevel(level)


async def run_pipeline(
    settings: Settings,
    stop: asyncio.Event,
    *,
    client_factory: Callable[[], Any] | None = None,
) -> int:
    """Compose the backbone and serve until ``stop`` fires; 0 clean, 1 fault.

    Composition order mirrors ``App.StartPipeline`` over the ``MauiProgram`` graph:
    one HTTP client feeds the scraper; the store, limiter, tracker and queue are
    shared state; diff providers bridge committed + in-flight knowledge; the fixed
    3-worker pool and the poll loop receive the SAME ``stop`` event as their cancel
    signal (the ``_pipelineCts.Token`` analogue). Teardown stops the poller first so
    no late enqueue races ``pool.stop()``'s queue-completion drain, closes the client
    exactly once, and never lets a teardown error mask the exit code.

    ``client_factory`` is a test seam returning an ``httpx.AsyncClient`` (annotated
    loosely because the architecture gate forbids naming ``httpx`` outside
    ``mostaql.http``).
    """
    interaction = get_interaction_logger(settings.log_file_path)
    _configure_logging(settings.log_level)
    interaction.mark("Runtime.Starting", "A")

    client = client_factory() if client_factory is not None else build_default_client()
    poller: PollService | None = None
    pool: WorkerPool | None = None
    store: SQLiteStore | None = None
    exit_code = 0
    try:
        fetcher = PageFetcher(client)
        scraper = MostaqlScraper(fetcher)
        store = SQLiteStore(settings.db_path)
        limiter = TokenBucketRateLimiter(settings.max_requests_per_minute, settings.safe_requests)
        tracker = InFlightTracker()
        queue = DiscoveryQueue()
        diff_engine = DiffEngine(CommittedIdsProvider(store), InFlightSetProvider(tracker))
        enrichment = EnrichmentService(limiter, scraper)
        events = LoggingPipelineEvents()
        pool = WorkerPool(queue, enrichment, tracker, store, events, worker_count=_WORKER_COUNT)
        poller = PollService(scraper, diff_engine, queue, tracker, store, limiter, events)

        # Persisted-state application (C# reads Preferences in StartPipeline).
        poller.poll_interval_seconds = settings.poll_interval_seconds
        poller.query_params = settings.query_params
        poller.set_paused(settings.start_paused)

        await pool.start(stop)
        await poller.start(stop)
        interaction.mark("Runtime.PipelineStarted", "A")
        await stop.wait()
    except Exception as exc:
        exit_code = 1
        interaction.fault("Runtime.PipelineFault", exc)
    finally:
        interaction.mark("Runtime.ShuttingDown", "A")
        if poller is not None:
            with suppress(Exception):
                await poller.stop()
        if pool is not None:
            with suppress(Exception):
                await pool.stop()
        with suppress(Exception):
            await client.aclose()
        if store is not None:
            with suppress(Exception):
                store.close()
    return exit_code


def _threadsafe_stopper(
    loop: asyncio.AbstractEventLoop, stop: asyncio.Event
) -> Callable[[int, FrameType | None], None]:
    """Bridge a foreign signal-handler thread onto the loop (Windows SIGINT path)."""

    def handler(signum: int, frame: FrameType | None) -> None:
        del signum, frame
        loop.call_soon_threadsafe(stop.set)

    return handler


def _install_signal_handlers(
    loop: asyncio.AbstractEventLoop, stop: asyncio.Event
) -> list[Callable[[], None]]:
    """Route SIGINT/SIGTERM to ``stop.set()``; returns undo callables.

    POSIX: native loop handlers for both signals. Windows: ``add_signal_handler``
    raises ``NotImplementedError`` -- SIGINT falls back to ``signal.signal``;
    SIGTERM is registered best-effort but Windows has no external SIGTERM delivery,
    a documented platform limitation. Failures degrade to "no handler installed".
    """
    undos: list[Callable[[], None]] = []
    for sig in (signal.SIGINT, signal.SIGTERM):
        try:
            loop.add_signal_handler(sig, stop.set)
        except (NotImplementedError, AttributeError, RuntimeError):
            previous: Any = None
            try:
                previous = signal.signal(sig, _threadsafe_stopper(loop, stop))
            except (ValueError, OSError):
                continue

            def _restore_signal(s: signal.Signals = sig, p: Any = previous) -> None:
                signal.signal(s, p)

            undos.append(_restore_signal)
        else:

            def _remove_signal(s: signal.Signals = sig) -> None:
                loop.remove_signal_handler(s)

            undos.append(_remove_signal)
    return undos


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="mostaql", description="MostaqlK backbone service.")
    parser.add_argument(
        "--config",
        type=Path,
        default=None,
        help="Path to a TOML configuration file (default: ./mostaql.toml if present)",
    )
    parser.add_argument("--version", action="version", version=f"mostaql {__version__}")
    return parser


def _resolve_config_file(explicit: Path | None) -> Path | None:
    if explicit is not None:
        return explicit
    candidate = Path(_DEFAULT_CONFIG_FILE_NAME)
    return candidate if candidate.is_file() else None


async def main(argv: Sequence[str] | None = None) -> int:
    """Parse arguments, wire signals, serve the pipeline; see :func:`run_pipeline`."""
    args = _build_parser().parse_args(argv)
    try:
        settings = load_settings(os.environ, _resolve_config_file(args.config))
    except ConfigError as exc:
        get_interaction_logger().fault("Runtime.ConfigInvalid", exc)
        return _CONFIG_EXIT_CODE

    loop = asyncio.get_running_loop()
    stop = asyncio.Event()
    undo_handlers = _install_signal_handlers(loop, stop)
    try:
        return await run_pipeline(settings, stop)
    finally:
        for undo in reversed(undo_handlers):
            with suppress(Exception):
                undo()


def cli() -> None:
    """Console-script wrapper: run :func:`main` to completion and propagate its code."""
    try:
        code = asyncio.run(main())
    except KeyboardInterrupt:
        code = _KEYBOARD_INTERRUPT_EXIT_CODE
    raise SystemExit(code)

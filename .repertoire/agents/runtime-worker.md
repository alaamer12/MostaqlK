# runtime-worker — Wave D Report

## Goal

Replace the `mostaql.runtime` stub with the real composition root + lifecycle per frozen
plan §6/§8/§12, mirroring C# `MauiProgram` composition + `App.StartPipeline` /
`App.RequestPipelineShutdown`, with tests, gates, and README run-commands.

## What was done

### Files created / modified

| File | Change |
|---|---|
| `.repertoire/python/src/mostaql/runtime.py` | Rewritten: `LoggingPipelineEvents` (7 callbacks), `_configure_logging`, `run_pipeline`, `_install_signal_handlers`, argparse (`--config`/`--version`), `main`, `cli`. |
| `.repertoire/python/tests/test_runtime.py` | New: 10 tests (events surface w/ broken + healthy logger, argparse paths, cli exit-code mapping incl. KeyboardInterrupt→130, unreadable config→2, signal install/undo mechanics, full run_pipeline smoke + same-DB second run). |
| `.repertoire/python/tests/integration/test_runtime_shutdown.py` | New: 2 tests (stop-mid-flight prompt drain + restart; crash-residue backlog survives until restart completes it). |
| `.repertoire/python/src/mostaql/__main__.py` | **Unchanged** — already exactly the required pattern (`from mostaql.runtime import cli`; guard calls `cli()` which raises `SystemExit`). |
| `.repertoire/python/README.md` | Run-commands section only: real service description, command table, exit-code table. Gates table untouched (already accurate). |

Untouched as mandated: `pyproject.toml`, all pipeline/storage/scraping/http modules,
config, diagnostics. No scratch files remain (debug scripts deleted).

### Exact event-callback surface found in the pipeline layer

Union of `PollEvents` (poller.py), `WorkerPoolEvents` (pool.py), `WorkerEvents`
(worker.py) — **7 unique callbacks** (diff/enrich raise typed errors consumed by
poller/worker; no direct event calls):

1. `on_status_changed(status: PollServiceStatus)` — MARK `Pipeline.StatusChanged` `<status.value>`
2. `on_project_discovered(project_id: int, title: str)` — MARK `Pipeline.ProjectDiscovered` A
3. `on_queue_count_changed(count: int)` — MARK `Pipeline.QueueCountChanged` A
4. `on_scan_succeeded(seen: int, enqueued: int)` — MARK `Pipeline.ScanSucceeded` A
5. `on_scan_failed(error: DomainError)` — ERROR `Pipeline.ScanFailed` (variant=code, C# Failure payload)
6. `on_worker_state(worker_id: int, state: str)` — MARK `Pipeline.WorkerStateChanged` A
7. `on_enriched(details: ProjectDetails)` — MARK `Pipeline.ProjectEnriched` A with id+title
   (INFO-equivalent; designated future notification hook point)

All callbacks wrap bodies in `contextlib.suppress(Exception)` → never raise even against a
broken logger; zero UI coupling; logger injectable for tests, else diagnostics singleton.

### Composition order in `run_pipeline` (mirrors MauiProgram/App.StartPipeline)

`get_interaction_logger(settings.log_file_path)` → `_configure_logging(log_level)`
(basicConfig once-guarded, module logger `"mostaql"`) → client (injected factory or
`build_default_client()`) → `PageFetcher` → `MostaqlScraper` → `SQLiteStore` →
`TokenBucketRateLimiter(rpm, safe_requests)` → `InFlightTracker` → `DiscoveryQueue` →
`CommittedIdsProvider`+`InFlightSetProvider`+`DiffEngine` → `EnrichmentService(limiter,
scraper)` → `LoggingPipelineEvents` → `WorkerPool(worker_count=3)` → `PollService` →
apply persisted state (`poll_interval_seconds`, `query_params`,
`set_paused(start_paused)`) → `pool.start(stop)` then `poller.start(stop)` sharing ONE
asyncio.Event (verified both take cancel `asyncio.Event`s) → `await stop.wait()`.

Teardown in C# order: `poller.stop()` → `pool.stop()` (queue.complete + cancel → workers
drain buffered IDs first, like `WorkerPool.StopAsync`) → `client.aclose()` once →
`store.close()`. Every step suppressed so teardown never masks the exit code.
Unexpected exceptions during serve/compose: FAULT `Runtime.PipelineFault` + return 1;
composition-phase failures also release the client via the same finally.
Checkpoints logged: `Runtime.Starting` / `Runtime.PipelineStarted` /
`Runtime.ShuttingDown` / `Runtime.PipelineFault` / `Runtime.ConfigInvalid`.

Signals: POSIX uses `loop.add_signal_handler(SIGINT/SIGTERM)`; Windows fallback bridges
SIGINT through `signal.signal` + `loop.call_soon_threadsafe`; SIGTERM on Windows is
best-effort only (documented limitation: no external SIGTERM delivery). Returns undo
list; `main` restores handlers in reverse. `cli()` maps KeyboardInterrupt → exit 130.

## Verification (from `.repertoire/python`)

| Gate | Result |
|---|---|
| `uv sync` | OK (mostaql editable reinstalled) |
| `uv run ruff format .` | 73 files unchanged |
| `uv run ruff check .` | All checks passed |
| `uv run mypy src` | Success: no issues in 43 files (strict, zero ignores) |
| `uv run xenon src -b B` | Pass (no blocks > B reported) |
| `uv run lint-imports` | **2 kept, 1 BROKEN** — see Concern #1 |
| `uv run pytest -q --cov --cov-report=term` | **456 passed**, total coverage **94.18%** (≥85); runtime.py itself 86% |
| `uv build` | sdist + wheel built |
| `uv run mostaql --help` | usage printed, EXIT=0 |
| `uv run mostaql --version` | `mostaql 0.1.0`, EXIT=0 |
| Dry start (`python -m mostaql --config <paused toml>`, 3s, terminated) | Served whole window; log shows `Runtime.Starting` → queue pulse → `Runtime.PipelineStarted`; no stderr. Missing `Runtime.ShuttingDown` is expected: Windows `terminate()` is a hard kill (no graceful signal path — the documented platform limitation). Graceful shutdown is proven by the stop-driven tests instead. |

Tests are Windows-safe: no OS signals fired anywhere; shutdown driven exclusively via the
shared `stop` event; stray worker idle timers cancelled explicitly to keep loop-close clean.

## Interpretation notes / deviations

1. **`client_factory` annotated `Callable[[], Any]`, not `Callable[[], httpx.AsyncClient]`.**
   The import-linter contract forbids naming `httpx` outside `mostaql.http` and does not
   exempt runtime; behavior identical (factory must return an object with `aclose()`).
2. **Shutdown-mid-flight test semantics.** Under the actual worker contract, a backlog row
   is removed only after processing returns normally — so a *graceful* mid-flight stop with
   a succeeding fetch necessarily DRAINS it (row → Enriched, backlog emptied), which test 1
   asserts. A row that outlives a shutdown requires an unfinished/unsuccessful process —
   exactly the crash-residue shape test 2 replays (Pending row + live backlog row seeded,
   restart completes it → Enriched + backlog empty). Both sides of the C# nuance are
   covered; a literal "stop abandons the in-flight fetch leaving backlog intact" scenario
   cannot occur without violating the <5s bound (failed attempts incur 60s ladder sleeps)
   or the frozen `WorkerPool.stop()` await-workers contract.
3. **`__main__.py` unchanged**: existing content already matched the required pattern.
4. **README Status section** still says "Wave A" stub wording (outside my sanctioned
   "run-commands section only" boundary — left for final wave).

## Concerns for final wave

1. **BLOCKING-ish: `httpx-only-in-http-layer` contract is BROKEN by design conflict.**
   Frozen plan §6 grants `mostaql.runtime` "may import every layer", and composition
   requires `PageFetcher` + `build_default_client` from `mostaql.http`'s public API — but
   pyproject's contract lists runtime among forbidden sources and its single
   `ignore_imports` sanction covers only `mostaql.scraping.scraper -> mostaql.http`.
   pyproject.toml was outside my write boundary, so the gate stays red. One-line remedy
   (mirror of the existing sanctioned edge):
   add `"mostaql.runtime -> mostaql.http"` under `[tool.importlinter.contracts]`
   `ignore_imports` for `httpx-only-in-http-layer`.
   Alternatives rejected: duplicating the load-bearing UA/Accept/Accept-Language headers
   into runtime (drift risk on bot-filter-critical config) or dynamic `importlib` access
   (gate evasion).
2. Worker idle-state timers (fire-and-forget 2s transitions inside `EnrichmentWorker`) are
   not tracked/cancelled by `pool.stop()`; they are cancel-flag-guarded and harmless, but a
   hung detail fetch would delay `run_pipeline` past any bound since nothing cancels an
   in-flight HTTP request (matches C# StopAsync-await semantics; noting for awareness).
3. `MOSTAQL_*` env vars do not include `start_paused` (config-file key only) — dry-start
   smoke had to use TOML; consider adding it if headless operators need env-only pause.
4. README Status section stale (see deviation 4).

## Skills used

None available in this environment (`.cursor/skills/` absent) — per task instruction
"SKILLS — none".

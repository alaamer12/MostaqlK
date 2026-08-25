# MostaqlK Python Backbone

Headless asyncio service that watches the mostaql.com open-projects feed: poll, discover,
enrich, persist (SQLite), all rate-limited against a shared token-bucket budget.

This is the C# → Python migration of the MostaqlK backbone. The frozen migration plan is the
single source of truth: [`docs/refactor-python-plan.md`](docs/refactor-python-plan.md)
(governance: [`docs/agents-workflow.md`](docs/agents-workflow.md)).

## Status

Wave A / Phase 4 "Foundation": packaging, tool configuration, directory skeleton,
configuration loader, interaction-log sink. Pipeline modules are intentional empty stubs
until their waves land.

## Running the service

Requires [uv](https://docs.astral.sh/uv/). Python 3.12 is pinned via `.python-version`
(uv downloads it automatically).

```cmd
uv sync
uv run mostaql
```

`mostaql` is a headless long-running service: it polls mostaql.com, enriches new
projects through a fixed 3-worker pool, and persists to SQLite until stopped.
Stop with `Ctrl+C` (POSIX additionally honours `SIGTERM`); shutdown is graceful —
polling stops first, then queued projects drain before workers exit.

| Command | Effect |
|---|---|
| `uv run mostaql` | Run the service (defaults; uses `./mostaql.toml` if present) |
| `uv run mostaql --config PATH` | Run with an explicit TOML configuration file |
| `uv run mostaql --version` | Print the package version and exit 0 |
| `uv run mostaql --help` | Show usage and exit 0 |

Exit codes: `0` clean shutdown · `1` unexpected pipeline fault (also logged as
FAULT) · `2` invalid configuration · `130` interrupted (`Ctrl+C`).

## Quality gates

Run from this directory (`​.repertoire/python/`):

| Gate | Command |
|---|---|
| Format | `uv run ruff format .` |
| Lint (+ cyclomatic ≤ 10, bandit `S`, security rules) | `uv run ruff check .` |
| Type check | `uv run mypy src` |
| Cognitive complexity (block grade ≤ B) | `uv run xenon src -b B` |
| Architecture boundaries | `uv run lint-imports` |
| Tests + coverage | `uv run pytest -q` |
| Dependency security | `uv run pip-audit` |
| Build | `uv build` |

Coverage threshold: `fail_under = 85` (enforced; current suite sits at ~95%).

## Configuration

Defaults live in code; an optional TOML file may override them; environment variables win
over the file. See `src/mostaql/config.py` for the exact contract (plan §8).

| Env var | Effect |
|---|---|
| `MOSTAQL_DB_PATH` | SQLite database path (default `<project root>/data/mostaqlk.db`) |
| `MOSTAQL_POLL_INTERVAL_SECONDS` | Poll interval, clamped to 10..3600 (default 30) |
| `MOSTAQL_MAX_REQUESTS_PER_MINUTE` | Shared request budget, must be ≥ 1 (default 2) |
| `MOSTAQL_SAFE_REQUESTS` | `0`/`false`/`no` disables safe mode spacing (default true) |
| `MOSTAQL_QUERY_PARAMS` | Extra query string appended to listing requests |
| `MOSTAQL_LOG_FILE` | Interaction-log path (default `<project root>/data/logs/interaction-log.txt`) |
| `MOSTAQL_LOG_LEVEL` | Standard logging level name (default INFO) |

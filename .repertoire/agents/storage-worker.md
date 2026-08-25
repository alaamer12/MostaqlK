# storage-worker — Wave B3: Persistence Layer Report

## Goal

Complete persistence layer per frozen plan §3.E / §4.2 / §8: `storage/timestamps.py`,
`schema.py`, `protocol.py`, `sqlite_store.py`, `search.py`, `__init__.py`, plus the
contract suite (`tests/contract/**`), `test_timestamps.py`, `test_fts_search.py` —
byte-parity with C# `SqliteConnectionFactory`, `ProjectRepository`, `OwnerRepository`,
and `FtsQueryService`.

## What was done

- **timestamps.py** — `dotnet_o_format` (7-digit fraction = `microsecond*10` padded,
  explicit `+HH:MM`, UTC → `+00:00` like `DateTimeOffset.UtcNow.ToString("O")`),
  `parse_dotnet_o` (6/7-digit fractions, trailing `Z`, signed offsets; offset preserved
  on parse; fraction normalized to 6 digits before `fromisoformat` for version-robustness),
  `current_utc`. Naive input raises `ValueError`.
- **schema.py** — `INITIAL_SCHEMA_SQL` verbatim minus `assets`/`app_secrets`;
  `SCHEMA_VERSION`/`CURRENT_SCHEMA_VERSION = 1`; `connect()` runs
  `PRAGMA journal_mode=WAL` + `busy_timeout=5000` on EVERY open, `row_factory=Row`,
  autocommit isolation; `ensure_schema(conn, *, schema_verified_flag)` mirrors C#
  `_schemaVerified` once-per-process gate under a module lock — fresh DB runs DDL in a
  tx then sets `user_version=1`; mismatched version raises
  `SchemaMismatchError(schema_mismatch(...))` (DB-003); FTS backfill SQL copied from C#
  `BackfillMissingFtsRows` (secrets bits dropped).
- **protocol.py** — `ProjectStore(Protocol)` exactly per plan §8 (14 methods);
  docstrings cite C# method names; `Ok(false)` duplicate-not-error documented as plain
  `False`.
- **search.py** — pure `build_fts_query`: split on `" "`, trim, embedded quotes doubled,
  each term wrapped `"term"*`, joined by space; whitespace-only → `""`.
- **sqlite_store.py** — all C# SQL verbatim (qmark params); single shared connection +
  one `threading.Lock` serializing every op (single-writer discipline mirroring WAL);
  async surface via `asyncio.to_thread` (search stays sync per contract);
  `insert_summary` tx {INSERT OR IGNORE + conditional FTS insert} → bool;
  `upsert_details` ONE tx {ON CONFLICT DO UPDATE with the four CASE sentinel guards,
  discovered_at absent from DO UPDATE SET, owner_id 0→NULL, NULL-when-None budget/
  delivery_days/project_status, skills delete+executemany, FTS delete+reinsert with
  `' '`-joined skill names}; `upsert_owner` exact identity-insert-only/conflict-stats-
  refresh semantics with `last_seen_at=dotnet_o_format(current_utc())`; backlog ops
  incl. exact `datetime('now', '-' || ? || ' days')` clean shape returning rowcount;
  `get_recent` exact ORDER BY with `group_concat(name, ', ')` subquery mapped BY NAME;
  guarded mark-as-read; counts verbatim; `search` = `FtsQueryService.SearchAsync`
  column list MINUS enriched_at (summaries carry `enriched_at=None`), ORDER BY rank.
  All `sqlite3.Error` → `StoreOperationError(store_query_failed("<C# op name>", exc))`;
  no logging/prints inside the store.
- **tests** — contract suite parameterized over `["sqlite","memory"]` with faithful
  `InMemoryStore` fake (sentinel/duplicate/ordering semantics replicated via same string
  keys; rank order explicitly NOT replicated); sentinel matrix incl. raw-sqlite byte
  checks (owner_id IS NULL, discovered_at TEXT unchanged), skills replace, owner
  identity-vs-stats, backlog lifecycle + aged-entry ordering + clean_old(30),
  get_recent ordering `[3,1,4,2]` (pending-last, enriched DESC, discovered tiebreak),
  mark guards, counts, Arabic prefix search both arms; sqlite-only FTS-row introspection;
  timestamps unit tests (7-digit padding matrix, Z handling, offsets, naive rejection,
  round-trips); build_fts_query unit tests.

## Files touched

- `src/mostaql/storage/{__init__,timestamps,schema,protocol,search,sqlite_store}.py` (implemented)
- `tests/contract/{fakes.py,test_project_store_contract.py}` (new)
- `tests/test_timestamps.py`, `tests/test_fts_search.py` (new)

## Verification (trimmed)

| Gate | Result |
|---|---|
| `uv sync` | OK |
| `uv run ruff format .` | OK (my files clean) |
| `uv run ruff check` (scoped to my files) | All checks passed |
| `uv run mypy src` (storage modules) | clean |
| `uv run xenon src/mostaql/storage -b B` | clean |
| `uv run lint-imports` | pipeline-free-of-storage-and-http KEPT; pure-leaves KEPT |
| `uv run pytest -q` (full suite) | **295 passed, 0 failed** |
| FTS5 probe | `FTS5 OK` |

## Deviations & concerns

1. **Pre-existing gate failures outside my boundary (NOT introduced by this task):**
   `ruff check .` fails in `scraping/parsers/inference.py` + `tests/test_inference_engine.py`;
   `mypy src` has 2 errors in `scraping/scraper.py` (DetailParser attr);
   `xenon src -b B` flags 3 C-blocks in `scraping/parsers/structural.py`;
   `lint-imports` httpx-only-in-http-layer BROKEN via `scraping.scraper → mostaql.http`;
   global coverage 59.21% < 60 because `detail.py` (+372 stmts) landed mid-run from a
   concurrent wave.
2. **SQL alias additions:** appended `AS skills_text` to the two COALESCE group_concat
   subqueries so name-based mapping (trap 14) is deterministic. No semantic change;
   C# maps by position instead.
3. **DDL splitting:** `run_initial_migration` splits `INITIAL_SCHEMA_SQL` on `";"` — safe
   invariant (no semicolons inside DDL literals), chosen because sqlite3 cannot execute
   multi-statement scripts inside an explicit transaction otherwise.
4. **clean_old testability:** kept byte-parity SQL form; tests age backlog rows via raw
   sqlite UPDATE (sqlite arm) / internal dict (fake arm), as sanctioned by the task spec.
5. **Fake search rank:** InMemoryStore returns hits in insertion order; FTS5 bm25 rank
   ordering is not approximated (documented in module docstring; no rank assertions in
   shared contract cases).
6. **Python 3.12 note:** verified `datetime.fromisoformat` natively handles 7-digit
   fractions and `Z`; parser still normalizes defensively before parsing.
7. **Concurrency note:** C# opens per-operation connections; Python uses one long-lived
   connection (`check_same_thread=False`) behind a lock — same WAL/busy_timeout pragmas,
   serialized access, no lost updates.

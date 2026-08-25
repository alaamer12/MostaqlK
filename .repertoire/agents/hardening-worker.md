# hardening-worker — Wave E Resilience Hardening Report

**Date:** 2026-08-25 · **Version scope:** V1 only · **C# spec:** frozen (`Infrastructure/Database/ProjectRepository.cs`, `.repertoire/.steering/v1/tech/error-handling-and-resilience.md`)

## Goal

Close the two known resilience gaps WITHOUT touching behavior:
A) error-branch coverage for `sqlite_store.py` (SqliteException→StoreOperationError wrappers) to ≥95%;
B) malformed-input matrix pinning plan §19 semantics (one bad record never aborts the batch unless C# aborts);
C) fix any real divergence from the C# spec found along the way.

Skills note: `.cursor/skills/` is absent in this environment; no skill was loadable (per task header "SKILLS — none").

## Files created / modified

| File | Status |
|---|---|
| `.repertoire/python/tests/test_sqlite_store_errors.py` | NEW — 20 hardening tests |
| `.repertoire/python/tests/regression/fixtures/malformed/*.html` (7 files) | NEW fixtures |
| `.repertoire/python/tests/test_malformed_matrix.py` | NEW — 11 matrix cases + type-safety meta-test |
| Production sources (`sqlite_store.py`, `schema.py`) | **UNTOUCHED** — see "Divergences" below |

### A) `tests/test_sqlite_store_errors.py`

Fault injection via `monkeypatch.setattr(store, "_conn", …)`:

- **Connection-level failures** (`LockedConnection`): every `execute()` raises `sqlite3.OperationalError("database is locked")`.
- **Mid-transaction failures** (`MidTransactionFailure`): passthrough until `cursor()`, so the REAL `BEGIN IMMEDIATE`/rollback machinery runs; atomicity probed with a second raw sqlite3 connection.
- **Defensive empty-result path** (`NullRowConnection`): pins C# ProjectRepository.cs:563-566 (`reader.ReadAsync()==false ⇒ Ok((0,0))`).

Coverage achieved per goal list:
- insert_summary: connection-level → DB-002 + `'InsertSummaryAsync'` + `__cause__`; mid-tx rollback leaves zero partial rows.
- upsert_details: both modes; mid-tx asserts `(projects, skills, fts) == (0, 0, 0)`; **non-sqlite3 exception rethrown unwrapped** (pins cs:246-250 Fault+RETHROW analog — only `sqlite3.Error` becomes StoreOperationError).
- owner upsert failure → DB-002 `'UpsertAsync'`.
- Backlog ops (add/remove/get/clean), get_recent, count_tracked, count_added_today, search, mark_as_read, mark_all_as_read: parametrized DB-002 assertions each carrying the exact C# operation name.
- Schema bootstrap on corrupted file: pre-set `PRAGMA user_version = 99` ⇒ `SchemaMismatchError`, `.error.code == "DB-003"`, message names both versions.
- Concurrent-writer smoke: two `SQLiteStore` instances over ONE db file, `asyncio.gather` of two writers × 25 inserts + backlog ops each (bounded N); WAL + `busy_timeout=5000` absorb contention; zero "database is locked" escapes; final row/backlog sets verified.

### B) Malformed matrix

Fixtures (`tests/regression/fixtures/malformed/`) and pinned outcomes:

| Fixture | Pinned behavior |
|---|---|
| `listing_no_cards_but_links.html` | Tier-3 anchor sweep rescues batch; junk anchors (non-numeric id, blank title) skipped individually; duplicate id keeps FIRST occurrence (trap §4.1-14); all listing defaults asserted |
| `listing_broken_encoding.html` | Literal U+FFFD mojibake survives all normalization; intact Arabic time meta still classifies (`منذ ساعتين` → 2) |
| `detail_no_meta.html` | Every field resolves to documented defaults (budget/status/dates None-or-0, skills [], owner_id 0, proposal provenance `none`, discovered==enriched instant) |
| `detail_empty_description.html` | Description chain exhausts → empty STRING, not None (trap §4.1-22) |
| `detail_weird_skills.html` | Empty/whitespace `<li>` skipped silently; nested tags flatten via text_content; entity decoded pre-normalize |
| `detail_absolute_date_publish.html` | Digit-run precedence quirk: `"2024/05/01"` → `publish_time_number == 2024`, raw text preserved (trap §4.1-5) |
| `detail_duplicate_labels.html` | See verification below |

Runner is parametrized over 10 rows (7 new fixtures + 3 negative controls reusing existing `listing/blank.html`→PARSE-001, `listing/empty_body.html`→PARSE-003, `detail/missing_title.html`→PARSE-002) asserting either success-with-defaults or the exact PARSE-* code. No new exception types introduced (enforced by a dedicated meta-test).

## Divergences found + fixed

**None in production code — zero changes to `sqlite_store.py`/`schema.py` were needed.**

One task-hypothesis CORRECTED during verification (test-side pinning only):
- Task asked whether duplicate labels resolve "first structural wins per dict-overwrite semantics". **Verified against C# `StructuralExtractor.ExtractMetaFields` (StructuralExtractor.cs:261)**: `results[NormalizeLabel(GetText(label))] = GetText(value)` is a plain Dictionary indexer assignment over document-order rows ⇒ **LAST occurrence wins** (the first value is overwritten). Gap-filler heuristics (cs:320 `!results.ContainsKey(norm)`) only fill MISSING keys and cannot resurrect it. The Python port (`structural.py extract_meta_fields`) assigns identically. Fixture docstring + `_verify_duplicate_labels_last_wins` cite the C# line and pin last-wins (`budget == "250 $"`).

## Verification (trimmed gate outputs, from `.repertoire/python`)

```
uv run ruff format .   → "2 files reformatted, 73 files left unchanged" (the 2 new test files)
uv run ruff check .    → All checks passed!
uv run mypy src        → Success: no issues found in 43 source files
uv run xenon src -b B  → (silent exit 0)
uv run lint-imports    → Contracts: 3 kept, 0 broken.
uv run pytest -q --cov → 487 passed in 37.70s; Total coverage: 95.42%
```

Coverage before/after for the target module:

| Module | Before | After |
|---|---|---|
| `storage/sqlite_store.py` | 84% (32 missed lines — exactly the SqliteException wrappers) | **100%** (205/205 stmts, 6/6 branches) |
| `storage/schema.py` | 88% | 91% (DB-003 mismatch branch now exercised; residual misses are the once-per-process flag short-circuit + migration-rollback guard) |
| TOTAL | 94.18% | 95.42% |

## Concerns

1. `count_tracked()`'s defensive `(0, 0)` row-missing branch (line 411) is unreachable through real SQL (`COUNT(*)` always yields a row); it is pinned via a scripted connection mirroring the C# reader-guard, so any future refactor that deletes it now fails a test instead of drifting silently.
2. The concurrent-writer smoke relies on WAL + `busy_timeout=5000` absorbing contention; with N=25×2 microsecond-scale transactions this is deterministic on this machine but is a smoke test by design — it does not prove absence of lock timeouts under heavy load, only that the configured PRAGMAs are actually applied per connection.
3. White-box access to `store._conn` (private attribute swap) mirrors the existing contract suite's white-box raw-sqlite precedent; if the store ever renames the attribute these tests fail loudly, which is acceptable for fault-injection seams.
4. No scratch files were created; nothing to clean up.

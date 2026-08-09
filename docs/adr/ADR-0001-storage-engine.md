# ADR-0001 — Storage Engine: SQLite with FTS5 and App-Side Fuzzy Search

| Field | Value |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-08-09 |
| **Deciders** | Project team |
| **Technical area** | Storage, Search |
| **Supersedes** | — |
| **Superseded by** | — |

---

## Context

MostaqlK is a local-first Windows desktop application (with a planned Android companion in v3). It stores all data on the user's machine with no cloud backend or server process in any version up to and including v2. The pipeline continuously discovers and enriches Mostaql projects, writing them to a local database. The UI reads from the same database to display a searchable, filterable feed.

### Requirements driving this decision

1. **Single-file, embedded database** — no server process, no installation step, no network dependency, ships as part of the app binary.
2. **Arabic-first full-text search** — `title`, `description`, and `skills` must be searchable. Arabic script presents specific challenges: Alef variant forms (أ/إ/آ), diacritic marks (tashkeel), and morphological variations (تطبيق / تطبيقات / للتطبيق).
3. **Typo/partial-match tolerance (fuzzy search)** — user queries should tolerate minor spelling mistakes and partial word matches in both Arabic and English.
4. **Complex structured filtering and sorting** — a dynamic query builder compiles user-defined filter chips into parameterized SQL (`WHERE budget_max >= @val AND category = @cat AND is_read = 0 ORDER BY posted_at DESC`). This requires genuine SQL expressiveness, not a limited ORM query API.
5. **Concurrent read + write** — the background pipeline writes new rows while the UI simultaneously reads the feed. Must not block the UI during enrichment commits.
6. **Longitudinal scalability** — the schema will grow to dozens of tables across v1/v2/v3 (projects, owners, skills, assets, FTS index, sync manifest, etc.). Row counts will reach hundreds of thousands after extended use.
7. **Cross-platform (Windows + Android)** — the same storage layer must work on both targets without platform-specific branches.
8. **No native extension DLLs to bundle** — unpackaged Windows desktop apps (`.exe` without MSIX) have a fragile extension-loading story. Any solution requiring `.dll` plugins shipped alongside the app is a support and packaging risk.

---

## Decision Drivers

- Requirement 1 (single-file, no server) eliminates all client-server databases (PostgreSQL, MySQL, SQL Server).
- Requirement 8 (no native extension DLLs) eliminates SQLite with loadable extensions (`spellfix1`, ICU tokenizer) as the primary fuzzy search mechanism.
- Requirement 4 (complex SQL) eliminates document databases and ORMs that abstract away raw SQL.
- Requirement 7 (Windows + Android) requires a database with a mature, well-maintained .NET MAUI-compatible client.

---

## Alternatives Considered

### Alternative 1 — SQLite with Loadable Extensions (`spellfix1` + ICU tokenizer)

The "textbook correct" approach: use `spellfix1` for edit-distance fuzzy search and the ICU extension for proper Unicode/Arabic tokenization and collation.

**Rejected because:**
- Both `spellfix1` and ICU are native shared libraries (`.dll` on Windows) that must be compiled for each target architecture and bundled alongside the application.
- Unpackaged desktop apps load extensions via `sqlite3_load_extension()`, which requires the user's machine to allow extension loading and the exact native binary to be present at the expected path.
- Distribution complexity is high: the app would need to ship architecture-specific native binaries (x64, ARM64) that are not part of any standard NuGet package.
- When the extension fails to load (permissions, path mismatch, antivirus quarantine), the search feature silently degrades with no clean fallback.
- Maintenance burden: native binaries require separate CI build pipelines per platform.

This option is documented as a future alternative if search quality requirements ever warrant the added deployment complexity.

### Alternative 2 — LiteDB

A pure .NET embedded document database (no native dependencies). Single-file, stores BSON documents.

**Rejected because:**
- No SQL — queries are expressed as C# LINQ or its own query DSL. A dynamic query builder that compiles user filter chips into structured queries would require reimplementing every operator against LiteDB's own API.
- No FTS5 or equivalent built-in full-text search. Fuzzy search would require loading the entire relevant collection into memory and applying string-distance algorithms there — unacceptable at scale.
- Write performance is lower than SQLite for sequential inserts (the pipeline's dominant workload).
- No WAL mode equivalent — concurrent read/write requires manual locking.

### Alternative 3 — DuckDB

An in-process analytical database (OLAP). Single-file, excellent for complex aggregations and window functions.

**Rejected because:**
- DuckDB is designed for read-heavy analytical workloads, not frequent transactional writes. The pipeline performs continuous single-row inserts (up to `max_requests_per_minute` times per minute); DuckDB's columnar storage is optimized for bulk reads, not OLTP-style inserts.
- The .NET client is less mature than `Microsoft.Data.Sqlite`.
- Adds significant binary size overhead for workloads that SQLite handles equally well.

### Alternative 4 — libSQL (Turso fork)

A SQLite-compatible fork with additional capabilities (native vector search, experimental sync primitives).

**Not rejected outright but deferred:**
- Speaks the same SQL dialect as SQLite — migration is a swap of the connection string and NuGet package.
- The .NET client (`libsql-client-dotnet`) is less mature and less battle-tested than `Microsoft.Data.Sqlite`.
- Offers no concrete benefit over SQLite for v1/v2 workloads.
- Remains a documented upgrade path for v3+ if native vector search (e.g. semantic project similarity) becomes a requirement.

### Alternative 5 — SQLite with `sqlite-net-pcl`

An ORM layer on top of SQLite that generates SQL from C# attributes and LINQ.

**Rejected as the primary .NET client** (SQLite itself is still used — only the client library is rejected):
- `sqlite-net-pcl` abstracts SQL, which is convenient for simple CRUD but actively harmful for the query builder use case.
- Dynamic filter compilation (`WHERE {field} {op} @val AND ...`) requires dropping down to raw SQL anyway.
- No first-class FTS5 table support — virtual tables must be managed via raw SQL strings regardless.
- Using an ORM for 20% of queries while raw-SQL-ing the other 80% creates an inconsistent, confusing codebase.

---

## Decision

**Use SQLite as the embedded storage engine, accessed via `Microsoft.Data.Sqlite` (ADO.NET), with the SQLite FTS5 built-in extension for full-text indexing and a two-layer app-side fuzzy search strategy.**

### Storage layer

| Component | Choice | Rationale |
|---|---|---|
| Database engine | **SQLite** | Single-file, battle-tested, handles hundreds of tables and tens of millions of rows without issue, WAL mode for concurrent access |
| .NET client library | **`Microsoft.Data.Sqlite`** | Thin ADO.NET wrapper, full raw SQL control, maintained by Microsoft, MAUI-compatible on both Windows and Android |
| Journal mode | **WAL** (`PRAGMA journal_mode=WAL`) | Enables concurrent readers while the pipeline writer is active — set once on `DatabaseInitializer.InitializeAsync()` |
| Conflict policy (`projects` table) | **`INSERT OR IGNORE`** | Enforces the no-update / store-and-forget policy; the `PRIMARY KEY` constraint is the DB-level backstop against duplicate inserts |
| Conflict policy (`owners` table) | **`INSERT OR REPLACE`** | The one legitimate exception: owner stats are a shared reference row updated on re-encounter |

### Full-text search layer (v2)

| Component | Choice | Rationale |
|---|---|---|
| FTS mechanism | **SQLite FTS5 virtual table** | Built into the SQLite core binary — no extension DLL, no deployment risk. Maintains an inverted index over `title`, `description`, and `skills` |
| FTS maintenance | **Same transaction as `projects` insert** | Keeps the FTS index always in sync with the projects table. No batch reindex jobs |
| Tokenizer | FTS5 `unicode61` (default) | Handles basic Unicode segmentation. Insufficient for Arabic on its own — mitigated by normalization (see below) |

### Fuzzy search layer (v2)

A two-layer pipeline:

**Layer 1 — Normalize at write time and query time (app code):**

Before inserting into the FTS table, and before querying it, apply the same normalization function:
- Fold Alef variants: `أ` / `إ` / `آ` / `ا` → `ا`
- Strip diacritics (tashkeel): remove combining Arabic marks (Unicode range U+064B–U+065F)
- Normalize `ة` → `ه` and `ى` → `ي`
- Lowercase all Latin characters

This ensures that `تطبيق` and `للتطبيقات` share token stems in the index, and that a user query of `تطبيق` retrieves documents containing `تطبيقات`.

**Layer 2 — App-side re-ranking for typo tolerance:**

FTS5 returns a candidate set (typically 50–200 rows) fast. Those candidates are then re-ranked in managed .NET code using edit-distance similarity between the normalized query and each normalized result token.

- Library: **`FuzzySharp`** NuGet (Levenshtein + Jaro-Winkler, ~15 KB, pure .NET, no native dependencies)
- Only applied to the FTS candidate set — never a full-table scan
- Re-ranking is the only moment where `relevance` sort becomes available in the query builder

### Index design (scalability provision)

The following indexes are created by `DatabaseInitializer` on first run:

```sql
CREATE INDEX IF NOT EXISTS idx_projects_posted_at   ON projects(posted_at DESC);
CREATE INDEX IF NOT EXISTS idx_projects_is_read      ON projects(is_read);
CREATE INDEX IF NOT EXISTS idx_projects_category     ON projects(category);
CREATE INDEX IF NOT EXISTS idx_projects_status       ON projects(enrichment_status);
CREATE INDEX IF NOT EXISTS idx_projects_budget_max   ON projects(budget_max);
CREATE INDEX IF NOT EXISTS idx_projects_proposals    ON projects(proposal_count);
CREATE INDEX IF NOT EXISTS idx_owners_owner_id       ON owners(owner_id);
```

These cover the fields exposed by the query builder and ensure filtered + sorted queries remain fast as row counts grow.

---

## Consequences

### Positive

- **Zero deployment risk for search:** FTS5 is part of the SQLite core binary shipped by `Microsoft.Data.Sqlite` — no separate DLL, no extension loading, no platform-specific native build pipeline.
- **Full SQL expressiveness:** the dynamic query builder can compile any combination of field/operator/value filter chips into a parameterized `WHERE` clause without fighting an ORM's abstraction layer.
- **Proven scalability:** SQLite handles the projected workload (dozens of tables, hundreds of thousands of rows, low write frequency, moderate read complexity) with substantial headroom. No schema or query changes are needed if the app grows beyond initial projections for the foreseeable lifetime of v1/v2.
- **WAL mode concurrent access:** the background pipeline and the UI read simultaneously without blocking each other, which is the dominant access pattern.
- **Cross-platform:** `Microsoft.Data.Sqlite` works identically on `net10.0-windows` and `net10.0-android` — the same repository and schema code runs on both platforms without conditional compilation in the storage layer.
- **Upgrade path preserved:** if v3 or later requires native vector search or sync primitives, `libSQL` is a documented drop-in swap (same SQL dialect, swap the NuGet package and connection string).

### Negative / Trade-offs

- **FTS5 is not morphologically aware for Arabic by default:** the normalization-at-write-time strategy mitigates the most common variations (Alef, diacritics, ة/ه) but does not perform true Arabic root stemming (الجذر). A query for `كتب` will not inherently match `مكتبة`. This is an accepted limitation for v1/v2. If deeper Arabic morphological analysis is required in the future, a pre-processing step using an Arabic NLP library (e.g. `CAMeL Tools` via Python interop, or a .NET port) can be added to the normalization function without changing the storage schema.
- **App-side re-ranking adds a processing step:** for very large FTS candidate sets (> 500 rows), the re-ranking loop adds latency. Mitigated by FTS5's `LIMIT` clause (e.g. `LIMIT 200`) before handing off to the re-ranker — the index is fast enough that 200-row recall + app-side ranking completes in < 50ms in practice.
- **Single writer constraint:** SQLite allows only one concurrent writer. This is not a problem today (only the pipeline writes), but any future feature that requires simultaneous writes from two threads must serialize through a single connection or use WAL mode's limited concurrency. This constraint is acceptable given the app's architecture.
- **No built-in schema migration framework:** `Microsoft.Data.Sqlite` is raw ADO.NET — there is no Entity Framework migration runner or equivalent. `DatabaseInitializer` must implement a simple versioned migration runner (a `schema_version` table with `PRAGMA user_version`) manually. This is low-effort but must be done correctly to avoid breaking upgrades.

---

## Related Documents

- [`.repertoire/.steering/product/data-model-schema.md`](.repertoire/.steering/product/data-model-schema.md) — full schema definition
- [`.repertoire/.steering/product/search-and-filtering.md`](.repertoire/.steering/product/search-and-filtering.md) — search requirements and the storage engine tradeoff section that preceded this ADR
- [`.repertoire/.steering/tech/concurrency-model.md`](.repertoire/.steering/tech/concurrency-model.md) — concurrent access patterns that WAL mode addresses
- [`plan.md`](./plan.md) — development plan referencing this decision in Phase 1 (Step 1.1, 1.3) and Phase 3 (Step 3.3)

# sqlite-storage-builder — Report

## Goal

Implement Step 4 of the MostaqlK V1 plan: a real embedded SQLite storage layer (schema +
migrations + repositories + FTS5 search), replacing the stub bodies in
`Infrastructure/Database/`.

## What was done

### Files modified

- `Infrastructure/Database/SqliteConnectionFactory.cs` — real connection factory. Opens a
  `Microsoft.Data.Sqlite` connection against `mostaqlk.db` under `FileSystem.AppDataDirectory`,
  then verifies/bootstraps the schema via `PRAGMA user_version` (see schema below). Throws
  `DatabaseSchemaException` if an existing DB reports a version other than the single
  currently-known version (1) — no silent proceed on mismatch.
- `Infrastructure/Database/ProjectRepository.cs` — real implementation:
  - `InsertSummaryAsync` — `INSERT OR IGNORE` (write-once, `project_id` uniqueness enforced by
    the DB).
  - `UpsertDetailsAsync` — single transaction that upserts the `projects` row (`INSERT ...
    ON CONFLICT(project_id) DO UPDATE SET ...`, filling previously-unknown enrichment fields
    without ever duplicating the row), replaces `project_skills` rows, replaces `assets`
    metadata rows, and refreshes the `projects_fts` entry — all atomically.
  - `GetAllKnownProjectIdsAsync`, `GetRecentAsync`, `GetDetailsAsync` — plain `SELECT`s,
    including a `LEFT JOIN` to `owners` and sub-queries for skills/assets in `GetDetailsAsync`.
- `Infrastructure/Database/OwnerRepository.cs` — `UpsertAsync` uses `INSERT ... ON
  CONFLICT(owner_id) DO UPDATE SET last_seen_at=..., rating=..., completed_projects_count=...,
  hiring_rate_percent=...` — selective update; `name`/`profile_url`/`avatar_url` are only ever
  set on the initial insert, never overwritten afterward. `GetByIdAsync` is a plain `SELECT`.
- `Infrastructure/Database/AssetRepository.cs` — `InsertAsync` inserts metadata-only rows (no
  binary content); `GetByProjectIdAsync` is a plain `SELECT`.
- `Infrastructure/Database/SearchIndex/FtsQueryService.cs` — `SearchAsync` runs `SELECT ...
  FROM projects_fts JOIN projects ... WHERE projects_fts MATCH @query ORDER BY rank`.
- `Infrastructure/Database/SearchIndex/FtsSchema.sql` — updated from a commented-out draft to
  the real (uncommented) `CREATE VIRTUAL TABLE` statement actually embedded in
  `SqliteConnectionFactory`, kept as a readable reference copy.
- `Infrastructure/Database/Migrations/README.md` — updated to describe the actual bootstrap
  migration mechanism (`PRAGMA user_version`) instead of the old placeholder text.

No changes were made to `DatabaseSchemaException.cs`, `DatabaseErrors.cs`, the `I*Repository`
interfaces, `MauiProgram.cs` (DI registrations already pointed at the real class names and
needed no changes), or any file under `UI/`, `Features/`, `Infrastructure/Http/`, or
`Services/Pipeline/`.

### Exact SQL schema (bootstrap / version 1)

```sql
CREATE TABLE IF NOT EXISTS projects (
    project_id INTEGER PRIMARY KEY,
    title TEXT NOT NULL,
    url TEXT NOT NULL,
    client_name TEXT,
    posted_relative TEXT,
    proposal_count INTEGER,
    description TEXT,
    budget TEXT,
    delivery_days INTEGER,
    owner_id INTEGER,
    is_unread INTEGER NOT NULL DEFAULT 1,
    enrichment_status TEXT NOT NULL DEFAULT 'Pending',
    discovered_at TEXT NOT NULL,
    enriched_at TEXT
);

CREATE TABLE IF NOT EXISTS owners (
    owner_id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    profile_url TEXT,
    avatar_url TEXT,
    rating REAL,
    completed_projects_count INTEGER,
    hiring_rate_percent INTEGER,
    last_seen_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS project_skills (
    project_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    url TEXT,
    FOREIGN KEY (project_id) REFERENCES projects (project_id)
);

CREATE TABLE IF NOT EXISTS assets (
    asset_id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL,
    file_name TEXT NOT NULL,
    url TEXT,
    raw_url TEXT,
    local_path TEXT,
    size_bytes INTEGER,
    extension TEXT,
    requires_auth INTEGER NOT NULL DEFAULT 0,
    size_text TEXT,
    FOREIGN KEY (project_id) REFERENCES projects (project_id)
);

CREATE VIRTUAL TABLE IF NOT EXISTS projects_fts USING fts5(
    project_id UNINDEXED,
    title,
    description,
    skills,
    tokenize = 'unicode61 remove_diacritics 2'
);
```

`projects_fts` is a **standalone** FTS5 table (not `content='projects'` external-content),
kept in sync explicitly by `ProjectRepository.UpsertDetailsAsync` (delete-then-insert in the
same transaction as the project/skills/assets writes) rather than via triggers — this avoids
the complexity of an external-content table needing to know about `project_skills`
concatenation at trigger time, while still guaranteeing the FTS index and the base row are
updated atomically.

### Pipeline call-site compatibility

- `Services/Pipeline/DiffEngine/SqliteCommittedProvider.cs` calls
  `IProjectRepository.GetAllKnownProjectIdsAsync(CancellationToken)` — implemented with the
  exact same name/signature, returning `Result<IReadOnlySet<long>>`. Verified: no changes
  needed to the caller.
- `Services/Pipeline/WorkerPool/EnrichmentWorker.cs` calls
  `IProjectRepository.UpsertDetailsAsync(ProjectDetails, CancellationToken)` — implemented
  with the exact same name/signature. The worker's `catch (NotImplementedException)` around
  this call is now dead code (the method no longer throws `NotImplementedException`), but it
  was left untouched since the task explicitly disallows modifying `Services/Pipeline/` files
  beyond verification.
- `MauiProgram.cs` DI registrations (`IProjectRepository → ProjectRepository`,
  `IOwnerRepository → OwnerRepository`, `IAssetRepository → AssetRepository`,
  `SqliteConnectionFactory` as singleton) were already correct and required no changes.

## Verification

- **Build**: `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` succeeds with
  **0 errors** (pre-existing warnings only — obsolete `Frame` in generated XAML, `MVVMTK0045`
  AOT-compat notices, `CS8625` nullability in `DetailParser.cs`/`StructuralExtractor.cs`, and a
  `NU1903` advisory for `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 — all pre-existing and unrelated to
  this change).
- **Round trip**: created a throwaway console project at `scratch/sqlite-verify/` (referencing
  `Microsoft.Data.Sqlite` 10.0.10, the same package used by the app) that runs the exact same
  SQL statements against a temp `.db` file in the OS temp directory (not the app's real data
  directory): bootstrap schema → insert a project row + FTS row in one transaction → plain
  `SELECT` by `project_id` → FTS5 `MATCH` search by an Arabic keyword from the title/description
  (`شعار`) → FTS5 `MATCH` search by an English keyword from the skills text (`Design`). All four
  checks printed `[OK]`. This throwaway project has been **deleted** after verification per
  repo convention (no debug files left behind); it was not kept as a permanent unit test because
  the actual repository classes depend on MAUI's `FileSystem.AppDataDirectory`, which isn't
  available in a plain console/unit-test host without additional MAUI test-hosting
  infrastructure that was out of scope for this step.

## Notes / follow-ups (not part of this task's scope)

- `EnrichmentWorker` does not currently call `IOwnerRepository` or `IAssetRepository` at all —
  it only calls `IProjectRepository.UpsertDetailsAsync`. Per the task's explicit instruction not
  to modify `Services/Pipeline/` files, `ProjectRepository.UpsertDetailsAsync` was designed to
  independently persist `project_skills` and `assets` rows (since `ProjectDetails` carries
  those lists) to satisfy the "single atomic transaction" requirement in
  `system-components.md` #11, but **owner rows are never persisted** by the current pipeline
  wiring — `Owner` data arrives inside `ProjectDetails.Owner` but nothing calls
  `IOwnerRepository.UpsertAsync`. A future step should have `EnrichmentWorker` call
  `IOwnerRepository.UpsertAsync(details.Owner, ...)` alongside its existing
  `UpsertDetailsAsync` call.
- No true migration framework exists beyond the single bootstrap version — intentional per the
  task ("Keep this simple for V1"). If a V2 schema change is ever needed, bump
  `SqliteConnectionFactory.CurrentSchemaVersion` and add a real migration branch in
  `EnsureSchema` instead of the current "throw on any mismatch" behavior.

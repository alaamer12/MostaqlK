# Migrations

Numbered schema steps tracked via `PRAGMA user_version`, executed by the stepwise runner in
`SqliteConnectionFactory.EnsureSchema`. Each `.sql` file here is the readable reference copy;
the executable copy is the matching `MigrationNNNN_*Sql` constant in that class (keep the two
byte-identical). A step runs only when the database sits exactly at its predecessor version,
inside its own transaction, and `user_version` is written after the last step — so re-runs are
no-ops, a failed step leaves the version untouched, and a file newer than this build still
throws `DatabaseSchemaException` (no silent best-effort drift).

| Version | File | What it does |
|---------|------|--------------|
| 1 (bootstrap) | `0001_initial_schema.sql` (historical reference) | V1 tables: `projects` (write-once rows), `owners` (selective-update stats), `project_skills`, `assets` (metadata only), `discovery_backlog`, `projects_fts` (standalone FTS5 kept in sync explicitly by `ProjectRepository`). |
| 2 | `0002_add_source_identity.sql` | Source identity: `source_id` + `site_project_id` on `projects`, `source_id` on `owners`/`discovery_backlog`, unique `(source_id, site_project_id)`. Mostaql's internal-id band is 0, so existing rowids (and every child/FTS reference to them) stay valid untouched. |
| 3 | `0003_add_source_unique_keys.sql` | Enforces composite identity in storage: unique `(source_id, owner_id)` on owners; backlog rebuilt keyed `(source_id, project_id)` — band disjointness alone must not be the only guard against cross-source overwrite. |
| 4 | `0004_add_executor.sql` | Adds the nafezly-only `executor_name` column on `projects` (منفذ المشروع). Additive nullable `TEXT`; no rebuild/drop, FTS untouched (the UI never searches executors). Existing rows keep NULL; enrichment fills it going forward. |

Fresh installs never replay steps: `InitialSchemaSql` (version 0) already lands at the current
`CurrentSchemaVersion`, and the numbered steps only run for databases that predate it.

Adding a step: create `NNNN_name.sql` here, add the matching constant + `else if
(currentVersion == N-1)` branch in `EnsureSchema`, bump `CurrentSchemaVersion`, extend
`tools/DbMigrationTests` with an upgrade assertion, and update the table above.

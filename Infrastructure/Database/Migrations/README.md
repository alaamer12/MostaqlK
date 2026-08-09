# Migrations

V1 ships with a single bootstrap ("version 1") migration, embedded directly as a SQL string
inside `SqliteConnectionFactory.InitialSchemaSql` and applied on first connection to a fresh
database file. The applied version is tracked via `PRAGMA user_version` (no separate
`schema_migrations` table is needed for a single migration).

The bootstrap migration creates:
- `projects` — write-once project rows (`project_id` primary key; `INSERT OR IGNORE` /
  `INSERT ... ON CONFLICT DO UPDATE` only fills in previously-unset enrichment fields)
- `owners` — client/employer profiles with selective-update semantics (`last_seen_at` + stats
  only)
- `project_skills` — many-to-many project/skill rows
- `assets` — attachment metadata only (no binary content)
- `projects_fts` — standalone FTS5 virtual table for bilingual (Arabic/English) search,
  kept in sync explicitly by `ProjectRepository` (delete + re-insert) rather than via triggers

If a future version needs a real schema change, add a new numbered `.sql` file here
(e.g. `0002_add_x.sql`) as a readable reference, bump `SqliteConnectionFactory
.CurrentSchemaVersion`, and extend `EnsureSchema` with an actual migration step instead of
throwing `DatabaseSchemaException` for that version transition. Until then, any unexpected
`PRAGMA user_version` value causes `DatabaseSchemaException` at startup (no silent
best-effort schema drift).

# Migrations

Placeholder directory for SQLite schema migration scripts.

Each migration will be a numbered `.sql` file (e.g. `0001_initial_schema.sql`,
`0002_add_assets_table.sql`) applied in order by `SqliteConnectionFactory` on startup,
tracked via a `schema_migrations` table.

No migrations exist yet — this is scaffolding for the V1 storage engine described in
`.repertoire/.steering/base/system-components.md`.

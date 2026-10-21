-- 0003_add_source_unique_keys.sql — reference copy of the v2 -> v3 step executed by
-- SqliteConnectionFactory.EnsureSchema (PRAGMA user_version 2 -> 3).
--
-- Makes the composite source identity enforceable in storage instead of relying on
-- internal-id band disjointness alone: owners get a unique (source_id, owner_id) index,
-- and the discovery backlog is rebuilt keyed (source_id, project_id).

CREATE UNIQUE INDEX IF NOT EXISTS ux_owners_source_owner ON owners(source_id, owner_id);
CREATE TABLE discovery_backlog_new (
    source_id TEXT NOT NULL,
    project_id INTEGER NOT NULL,
    discovered_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    retry_count INTEGER DEFAULT 0,
    PRIMARY KEY (source_id, project_id)
);
INSERT INTO discovery_backlog_new (source_id, project_id, discovered_at, retry_count)
    SELECT source_id, project_id, discovered_at, retry_count FROM discovery_backlog;
DROP TABLE discovery_backlog;
ALTER TABLE discovery_backlog_new RENAME TO discovery_backlog;

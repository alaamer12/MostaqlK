-- 0001_initial_schema.sql — historical reference for the original V1 bootstrap schema
-- (PRAGMA user_version = 1). No longer executed on fresh installs: the current bootstrap in
-- SqliteConnectionFactory.InitialSchemaSql lands directly at the latest version. Kept so the
-- 0002 upgrade step can be understood against what shipped.

CREATE TABLE IF NOT EXISTS projects (
    project_id INTEGER PRIMARY KEY,
    title TEXT NOT NULL,
    url TEXT NOT NULL,
    client_name TEXT,
    publish_time_number INTEGER,
    publish_time_text TEXT,
    proposal_count INTEGER,
    proposal_count_text TEXT,
    description TEXT,
    budget TEXT,
    delivery_days INTEGER,
    project_status TEXT,
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
    hiring_rate_percent REAL,
    registered_at TEXT,
    open_projects_count INTEGER,
    in_progress_projects_count INTEGER,
    ongoing_communications_count INTEGER,
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

CREATE TABLE IF NOT EXISTS discovery_backlog (
    project_id INTEGER PRIMARY KEY,
    discovered_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    retry_count INTEGER DEFAULT 0
);

CREATE VIRTUAL TABLE IF NOT EXISTS projects_fts USING fts5(
    project_id UNINDEXED,
    title,
    description,
    skills,
    tokenize = 'unicode61 remove_diacritics 2'
);

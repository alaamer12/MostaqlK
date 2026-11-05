-- v5 -> v6 step (v4 competitor analysis): the personalization + competitor tables.
-- Isolation rules honoured here (v4 plan §1): EVERY table carries an explicit source_id and a
-- composite natural key, so a Mostaql row can never alias or overwrite a Nafezly row even if the
-- internal-id banding ever overlapped (0003's own note: band disjointness must not be the only guard).
-- Additive-only (0004/0005 style): no rebuild, no drop, no FTS/index change to existing tables.
-- NO score/percentage column anywhere: the winnability index is derived at read time (R4.12 / D42).
-- fields_json holds public scraped facts ONLY; no cookie/token ever lands here (secrets live in app_secrets).

-- R2.8: every cookie crawl is one append-only row; a failed attempt is evidence, never a profile write.
CREATE TABLE IF NOT EXISTS profile_snapshots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_id TEXT NOT NULL,
    crawled_at TEXT NOT NULL DEFAULT (datetime('now')),
    ok INTEGER NOT NULL,                              -- 1 usable, 0 failed attempt
    fields_json TEXT,                                 -- NULL when ok = 0
    error_kind TEXT,                                  -- session_expired | waf_challenge | not_found | network | parse_empty
    http_status INTEGER,                              -- recorded because it is NOT the success signal (R1.4)
    body_bytes INTEGER,                               -- the discriminator that works (~2 KB shell vs ~200 KB data)
    pages_read INTEGER                                -- how many of the read pages completed; honest progress
);
CREATE INDEX IF NOT EXISTS ix_snapshots_source_ok_time ON profile_snapshots(source_id, ok, crawled_at DESC);

-- R2.1 / R2.2: structured, per-source profile facts; one row per source (never merged). Skills and
-- interests live as real rows below (R2.1 "structured records", QueryParams demoted to a derived output).
CREATE TABLE IF NOT EXISTS user_profile (
    source_id TEXT NOT NULL,
    username TEXT,
    target_band TEXT,
    availability TEXT,
    usual_speed_days INTEGER,
    completeness_missing_json TEXT,                   -- R2.6: maturity = the exact list of missing canon fields
    updated_at TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (source_id)
);
CREATE TABLE IF NOT EXISTS user_profile_tags (
    source_id TEXT NOT NULL,
    kind TEXT NOT NULL,                               -- 'skill' | 'interest'
    slug TEXT NOT NULL,
    name TEXT NOT NULL,
    PRIMARY KEY (source_id, kind, slug)
);

-- R3.2 / R3.3: competitor/rival profile files. performer_key is the numeric avatar-prefix id on Nafezly
-- and the username on anonymous Mostaql (D37); identity resolution is app-side, storage keys per source.
CREATE TABLE IF NOT EXISTS performers (
    source_id TEXT NOT NULL,
    performer_key TEXT NOT NULL,
    display_name TEXT,
    profile_url TEXT,
    avatar_url TEXT,
    rating REAL,
    completed_projects_count INTEGER,
    completion_rate REAL,
    live_presence TEXT,
    last_seen_at TEXT,
    fields_json TEXT,
    PRIMARY KEY (source_id, performer_key)
);

-- R3.1 / R3.4: per-project bidder offer rows, stored as-is including the preview text.
CREATE TABLE IF NOT EXISTS project_bidders (
    source_id TEXT NOT NULL,
    site_project_id INTEGER NOT NULL,
    performer_key TEXT NOT NULL,
    display_name TEXT,
    bid_amount REAL,
    bid_text TEXT,
    delivered_days INTEGER,
    state TEXT,
    seen_at TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (source_id, site_project_id, performer_key)
);

-- R2.3 / D35 / C13 / D36: my own bid history. Agreed price is null unless I won; Mostaql carries the
-- site's five real outcome states, Nafezly infers won/lost (§3). This is the win-rate factor's substrate.
CREATE TABLE IF NOT EXISTS user_bids (
    source_id TEXT NOT NULL,
    site_project_id INTEGER NOT NULL,
    my_bid_amount REAL,
    agreed_deal_price REAL,
    outcome TEXT,
    project_title TEXT,
    category TEXT,
    delivered_days INTEGER,
    bid_created_at TEXT,
    awarded_at TEXT,
    PRIMARY KEY (source_id, site_project_id)
);

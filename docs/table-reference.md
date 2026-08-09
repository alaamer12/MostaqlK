# Table Reference: Normalization and SQL Conventions

> This document applies the normalization principles (3NF+) and SQL conventions (`docs/sql-conventions.md`) specifically to every table in MostaqlK's schema.
> For each table: purpose → functional dependencies → normalization proof → DDL → column reference → canonical queries.

---

## Table of Contents

| # | Table | Phase |
|---|---|---|
| 1 | [`schema_migrations`](#1-schema_migrations) | v1 |
| 2 | [`settings`](#2-settings) | v1 |
| 3 | [`categories`](#3-categories) | v1 |
| 4 | [`skills`](#4-skills) | v1 |
| 5 | [`owners`](#5-owners) | v1 |
| 6 | [`poll_runs`](#6-poll_runs) | v1 |
| 7 | [`projects`](#7-projects) | v1 |
| 8 | [`project_details`](#8-project_details) | v1 |
| 9 | [`project_skills`](#9-project_skills) | v1 |
| 10 | [`assets`](#10-assets) | v2 |
| 11 | [`notifications`](#11-notifications) | v1 |
| 12 | [`projects_fts`](#12-projects_fts) | v2 |

---

## 1. `schema_migrations`

### Purpose

Tracks which migration scripts have been applied to this installation. `DatabaseInitializer` reads this table at every startup to determine the current schema version and which migrations to run.

### Functional Dependencies

```
version → description
version → applied_at
```

Single-column primary key. Every non-key attribute depends directly and only on `version`.

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | All values are atomic. `description` is a single string, not a list. |
| **2NF** | ✅ | Single-column PK — partial dependency is impossible by definition. |
| **3NF** | ✅ | No transitive dependencies. `description` does not determine `applied_at`; `applied_at` does not determine `description`. Both depend solely on `version`. |
| **BCNF** | ✅ | The only determinant is `version`, which is the primary key. |

### DDL

```sql
CREATE TABLE IF NOT EXISTS schema_migrations (
    version     INTEGER PRIMARY KEY,
    description TEXT    NOT NULL,
    applied_at  TEXT    NOT NULL DEFAULT (datetime('now'))
);
```

### Column Reference

| Column | Type | Nullable | Constraint | Description |
|---|---|---|---|---|
| `version` | INTEGER | No | PRIMARY KEY | Sequential migration number (1, 2, 3, …). Never reused, never skipped. |
| `description` | TEXT | No | NOT NULL | Human-readable label for this migration, e.g. `"Initial schema"`. |
| `applied_at` | TEXT | No | NOT NULL, DEFAULT datetime('now') | ISO8601 UTC timestamp when this migration was applied. |

### SQL Conventions Applied

- `applied_at` follows the `_at` suffix naming rule (§7 of conventions).
- `DEFAULT (datetime('now'))` — the one acceptable use of a SQLite date function (§9).
- No indexes needed — this table has at most a handful of rows and is only read at startup.

### Canonical Queries

```sql
-- Check current schema version
SELECT MAX(version) FROM schema_migrations;

-- Log a completed migration (called inside the migration transaction)
INSERT INTO schema_migrations (version, description)
VALUES (@version, @description);

-- Read full migration history (diagnostic/admin use)
SELECT version, description, applied_at
FROM   schema_migrations
ORDER  BY version ASC;
```

---

## 2. `settings`

### Purpose

Persists application configuration as key-value pairs. Survives app restarts. Seeded with defaults on first launch. Read by `SettingsService` at startup and cached in memory — not queried on every operation.

### Functional Dependencies

```
key → value
key → updated_at
```

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | Each row holds one setting. Values are atomic strings. |
| **2NF** | ✅ | Single-column PK. |
| **3NF** | ✅ | `value` does not determine `updated_at` and vice versa. Both depend solely on `key`. |
| **BCNF** | ✅ | Only determinant is `key`. |

### DDL

```sql
CREATE TABLE IF NOT EXISTS settings (
    key        TEXT PRIMARY KEY,
    value      TEXT NOT NULL,
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Seed defaults on first migration
INSERT OR IGNORE INTO settings (key, value) VALUES
    ('poll_interval_seconds',         '30'),
    ('max_requests_per_minute',       '2'),
    ('max_concurrent_detail_fetches', '2'),
    ('query_params',                  ''),
    ('include_assets',                '0'),
    ('notification_grouping_enabled', '0'),
    ('notification_grouping_mode',    ''),
    ('notification_grouping_param',   ''),
    ('theme',                         'system');
```

### Column Reference

| Column | Type | Nullable | Constraint | Description |
|---|---|---|---|---|
| `key` | TEXT | No | PRIMARY KEY | Setting identifier, e.g. `'poll_interval_seconds'`. Always a known, fixed string — never user-supplied. |
| `value` | TEXT | No | NOT NULL | Setting value stored as TEXT regardless of logical type. C# layer converts to the correct type on read. |
| `updated_at` | TEXT | No | NOT NULL, DEFAULT | ISO8601 UTC. Updated on every write. |

### Notes on the Key-Value Pattern

This table uses a key-value pattern — an intentional and documented exception to strict 3NF for **configuration data only** (see §3.1 of `sql-conventions.md`). It is not a license to use key-value patterns for domain data. Domain data (projects, skills, etc.) always gets a proper normalized table.

### SQL Conventions Applied

- `INSERT OR IGNORE` for seeding defaults — ensures idempotency across migrations.
- All setting keys are hardcoded constants in C# — never user-supplied strings reaching this query.

### Canonical Queries

```sql
-- Load all settings at startup
SELECT key, value
FROM   settings
ORDER  BY key ASC;

-- Update a single setting
UPDATE settings
SET    value      = @value,
       updated_at = @updatedAt
WHERE  key        = @key;
```

---

## 3. `categories`

### Purpose

Normalizes the project category field out of the `projects` table. Without this table, `category` would be a raw TEXT column on `projects`, duplicated across every project in that category and unindexable without a full string scan.

### Functional Dependencies

```
category_id → name
name        → category_id   (because name is UNIQUE)
```

Two candidate keys: `category_id` (surrogate) and `name` (natural unique key).

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | `name` is atomic — one category name per row. |
| **2NF** | ✅ | Single-column PK. |
| **3NF** | ✅ | Only one non-key attribute (`name`). No transitive dependency is possible. |
| **BCNF** | ✅ | Both `category_id` and `name` are candidate keys. Every determinant is a candidate key. |

### DDL

```sql
CREATE TABLE IF NOT EXISTS categories (
    category_id INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL UNIQUE COLLATE NOCASE
);
```

### Column Reference

| Column | Type | Nullable | Constraint | Description |
|---|---|---|---|---|
| `category_id` | INTEGER | No | PRIMARY KEY AUTOINCREMENT | Surrogate key. Used as FK on `projects.category_id`. |
| `name` | TEXT | No | NOT NULL, UNIQUE COLLATE NOCASE | Category name as scraped from Mostaql, e.g. `"تطوير الويب"`. `COLLATE NOCASE` prevents ASCII-cased duplicates. |

### SQL Conventions Applied

- `COLLATE NOCASE` — accepted exception (§5, exception 4 in conventions).
- `AUTOINCREMENT` — used here because category IDs have no natural numeric meaning from the source. Surrogate key is appropriate.
- Category names from scraping are normalized (trim whitespace) in C# before `INSERT OR IGNORE`.

### Canonical Queries

```sql
-- Get or create a category by name (called during enrichment)
INSERT OR IGNORE INTO categories (name) VALUES (@name);
SELECT category_id FROM categories WHERE name = @name COLLATE NOCASE;

-- List all categories (for filter picker in UI)
SELECT category_id, name
FROM   categories
ORDER  BY name ASC;
```

---

## 4. `skills`

### Purpose

Normalizes skill names into a lookup table. Without this, `project_skills` would store raw TEXT skill names — duplicating strings, making "how many projects require React" a LIKE scan, and allowing "PHP" and "php" to coexist as distinct skills.

### Functional Dependencies

```
skill_id → name
name     → skill_id   (UNIQUE constraint)
```

Two candidate keys: `skill_id` and `name`.

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | One skill name per row. Atomic. |
| **2NF** | ✅ | Single-column PK. |
| **3NF** | ✅ | Only one non-key attribute. No transitive dependency possible. |
| **BCNF** | ✅ | Both candidate keys (`skill_id`, `name`) are determinants. |

### DDL

```sql
CREATE TABLE IF NOT EXISTS skills (
    skill_id INTEGER PRIMARY KEY AUTOINCREMENT,
    name     TEXT    NOT NULL UNIQUE COLLATE NOCASE
);
```

### Column Reference

| Column | Type | Nullable | Constraint | Description |
|---|---|---|---|---|
| `skill_id` | INTEGER | No | PRIMARY KEY AUTOINCREMENT | Surrogate key. Used as FK in `project_skills`. |
| `name` | TEXT | No | NOT NULL, UNIQUE COLLATE NOCASE | Skill name as scraped, e.g. `"PHP"`, `"تطوير الويب"`. `COLLATE NOCASE` merges ASCII case variants. |

### SQL Conventions Applied

- `INSERT OR IGNORE` used during enrichment — if the skill already exists, return its existing ID.
- `COLLATE NOCASE` — accepted exception (§5, exception 4).
- A skill name is normalized (trimmed, deduplicated) in C# before insertion.

### Canonical Queries

```sql
-- Get or create multiple skills in one enrichment pass
INSERT OR IGNORE INTO skills (name) VALUES (@name);
SELECT skill_id FROM skills WHERE name = @name COLLATE NOCASE;

-- Skills for a specific project (used in detail page)
SELECT s.skill_id, s.name
FROM   skills s
JOIN   project_skills ps ON s.skill_id = ps.skill_id
WHERE  ps.project_id = @projectId
ORDER  BY s.name ASC;

-- Projects requiring a given skill (query builder filter)
SELECT p.project_id
FROM   projects p
JOIN   project_skills ps ON p.project_id = ps.project_id
WHERE  ps.skill_id = @skillId;
```

---

## 5. `owners`

### Purpose

Project publisher/client profiles, deduplicated. The same client posts multiple projects — their stats live in one row rather than being copied per project. `owners` is the **one table with an update policy**: owner stats are refreshed each time the same owner appears in an enrichment pass.

### Functional Dependencies

```
owner_id → display_name
owner_id → title
owner_id → country
owner_id → joined_at
owner_id → hire_rate
owner_id → open_projects_count
owner_id → in_progress_projects_count
owner_id → ongoing_communications_count
owner_id → last_seen_at
```

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | All columns atomic. Stats are individual integers, not a JSON blob or comma list. |
| **2NF** | ✅ | Single-column PK. |
| **3NF** | ✅ | No non-key column determines another non-key column. `display_name` does not determine `hire_rate`. `country` does not determine `joined_at`. Each attribute is an independent fact about the owner identified by `owner_id`. |
| **BCNF** | ✅ | Only determinant is `owner_id`. |

### DDL

```sql
CREATE TABLE IF NOT EXISTS owners (
    owner_id                     INTEGER PRIMARY KEY,
    display_name                 TEXT    NOT NULL,
    title                        TEXT,
    country                      TEXT,
    joined_at                    TEXT,
    hire_rate                    REAL,
    open_projects_count          INTEGER NOT NULL DEFAULT 0,
    in_progress_projects_count   INTEGER NOT NULL DEFAULT 0,
    ongoing_communications_count INTEGER NOT NULL DEFAULT 0,
    last_seen_at                 TEXT    NOT NULL DEFAULT (datetime('now'))
);
```

### Column Reference

| Column | Type | Nullable | Constraint | NULL means |
|---|---|---|---|---|
| `owner_id` | INTEGER | No | PRIMARY KEY | Numeric ID from Mostaql owner profile URL. |
| `display_name` | TEXT | No | NOT NULL | e.g. `"أحمد العتيبي"`. Always present on the listing card. |
| `title` | TEXT | Yes | — | Professional headline e.g. `"مهندس أمن معلومات"`. NULL if not provided. |
| `country` | TEXT | Yes | — | e.g. `"السعودية"`. NULL if not shown. |
| `joined_at` | TEXT | Yes | — | ISO8601 date `"2015-09-12"`. NULL if not parseable. |
| `hire_rate` | REAL | Yes | — | **NULL means `"لم يحسب بعد"` (not yet calculated).** Never `0.0` for this state. |
| `open_projects_count` | INTEGER | No | NOT NULL DEFAULT 0 | Snapshot at last scrape time. |
| `in_progress_projects_count` | INTEGER | No | NOT NULL DEFAULT 0 | Snapshot at last scrape time. |
| `ongoing_communications_count` | INTEGER | No | NOT NULL DEFAULT 0 | Snapshot at last scrape time. |
| `last_seen_at` | TEXT | No | NOT NULL, DEFAULT | ISO8601 UTC. Updated each time this owner appears in an enrichment pass. |

### SQL Conventions Applied

- **`INSERT OR REPLACE`** — the accepted exception for this table (§5, exception 2 in conventions). The no-update policy applies to `projects`, not `owners`.
- `hire_rate` NULL follows the rule: NULL means genuinely unknown, not a sentinel value for `0%` (§10).
- `last_seen_at` follows the `_at` suffix rule (§7).

### Canonical Queries

```sql
-- Upsert owner on enrichment
INSERT OR REPLACE INTO owners (
    owner_id, display_name, title, country, joined_at,
    hire_rate, open_projects_count, in_progress_projects_count,
    ongoing_communications_count, last_seen_at
)
VALUES (
    @ownerId, @displayName, @title, @country, @joinedAt,
    @hireRate, @openProjects, @inProgress, @ongoingComms,
    @lastSeenAt
);

-- Owner profile for detail page
SELECT owner_id, display_name, title, country, joined_at,
       hire_rate, open_projects_count, in_progress_projects_count,
       ongoing_communications_count, last_seen_at
FROM   owners
WHERE  owner_id = @ownerId;
```

---

## 6. `poll_runs`

### Purpose

An audit log of every execution of the listing poll loop. Each time the orchestrator fires a poll cycle, it opens a `poll_runs` row at start (`status = 'running'`) and closes it at completion with the outcome. Provides diagnostics, rate analytics, and a FK anchor for `projects.poll_run_id`.

### Functional Dependencies

```
poll_run_id → started_at
poll_run_id → completed_at
poll_run_id → status
poll_run_id → query_params
poll_run_id → projects_found
poll_run_id → projects_new
poll_run_id → error_message
```

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | All columns atomic. `error_message` is a single string (not structured). |
| **2NF** | ✅ | Single-column PK. |
| **3NF** | ✅ | No transitive dependencies. `projects_new` does not determine `error_message`. `status` does not determine `completed_at`. Each column is an independent fact about this poll execution. |
| **BCNF** | ✅ | Only determinant is `poll_run_id`. |

### DDL

```sql
CREATE TABLE IF NOT EXISTS poll_runs (
    poll_run_id    INTEGER PRIMARY KEY AUTOINCREMENT,
    started_at     TEXT    NOT NULL DEFAULT (datetime('now')),
    completed_at   TEXT,
    status         TEXT    NOT NULL
                       CHECK (status IN ('running', 'success', 'failed', 'partial')),
    query_params   TEXT    NOT NULL DEFAULT '',
    projects_found INTEGER NOT NULL DEFAULT 0,
    projects_new   INTEGER NOT NULL DEFAULT 0,
    error_message  TEXT
);

CREATE INDEX IF NOT EXISTS idx_poll_runs_started ON poll_runs (started_at DESC);
```

### Column Reference

| Column | Type | Nullable | Constraint | NULL means |
|---|---|---|---|---|
| `poll_run_id` | INTEGER | No | PRIMARY KEY AUTOINCREMENT | Surrogate key. |
| `started_at` | TEXT | No | NOT NULL, DEFAULT | ISO8601 UTC when the poll loop began. |
| `completed_at` | TEXT | Yes | — | NULL while the poll is still `'running'` or if the process crashed before it could update. |
| `status` | TEXT | No | NOT NULL, CHECK | One of: `'running'`, `'success'`, `'failed'`, `'partial'`. |
| `query_params` | TEXT | No | NOT NULL, DEFAULT '' | The `query_params` setting active during this poll. Empty string = no filter. |
| `projects_found` | INTEGER | No | NOT NULL DEFAULT 0 | Total project cards parsed from the listing page. |
| `projects_new` | INTEGER | No | NOT NULL DEFAULT 0 | Net-new projects enqueued (unseen by DiffEngine). |
| `error_message` | TEXT | Yes | — | NULL on success. Set to the exception message on failure. |

### `status` Values

| Value | When set |
|---|---|
| `'running'` | Written on INSERT at poll start. |
| `'success'` | All candidates diffed and enqueued; listing parse succeeded. |
| `'failed'` | Listing fetch failed (network error, HTTP 4xx/5xx) or parse returned zero valid cards. |
| `'partial'` | Listing parsed successfully but some enqueued projects failed enrichment. |

### Canonical Queries

```sql
-- Open a new poll run at start
INSERT INTO poll_runs (status, query_params)
VALUES ('running', @queryParams);
-- → retrieve last_insert_rowid() as poll_run_id

-- Close a poll run on completion
UPDATE poll_runs
SET    completed_at   = @completedAt,
       status         = @status,
       projects_found = @projectsFound,
       projects_new   = @projectsNew,
       error_message  = @errorMessage
WHERE  poll_run_id    = @pollRunId;

-- Recent poll history (diagnostics page)
SELECT poll_run_id, started_at, completed_at, status,
       projects_found, projects_new, error_message
FROM   poll_runs
ORDER  BY started_at DESC
LIMIT  50;
```

---

## 7. `projects`

### Purpose

The central table. One row per Mostaql project ID, written once from the **listing page** (Tier 1 scrape). Contains only what is immediately available from the listing card. Enrichment data (description, budget, skills) goes to `project_details` and related tables after the Tier 2 scrape completes.

This is the table with the most strict insertion policy: **`INSERT OR IGNORE` only. No updates. No deletes.**

### Functional Dependencies

```
project_id → owner_id
project_id → category_id
project_id → poll_run_id
project_id → title
project_id → url
project_id → proposal_count
project_id → posted_at
project_id → scraped_at
project_id → source_query_params
project_id → is_read
project_id → enrichment_status

url → project_id   (UNIQUE constraint — second candidate key)
```

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | All values atomic. No multi-valued columns. `source_query_params` is a single nullable string, not a list. |
| **2NF** | ✅ | Single-column PK. |
| **3NF** | ✅ | The key question is: does any non-key column determine another non-key column? **No.** `owner_id` (a FK reference) does not determine `title` — the title is a fact about the project, not the owner. `category_id` does not determine `posted_at`. `enrichment_status` does not determine `is_read`. Each column is an independent attribute of `project_id`. |
| **BCNF** | ✅ | Both candidate keys (`project_id`, `url`) are determinants. No other determinants exist. |

> **Why is `owner_id` here and not a transitive dependency?**
> A transitive dependency would be: `project_id → owner_id → some_attribute_also_on_this_table`. The attributes of the owner (name, country, hire_rate) are on the `owners` table, not here. `owner_id` on `projects` is just a FK reference — a fact that "this project belongs to this owner." That is a direct fact about the project, not a transitive path.

### DDL

```sql
CREATE TABLE IF NOT EXISTS projects (
    project_id          INTEGER PRIMARY KEY,
    owner_id            INTEGER NOT NULL REFERENCES owners(owner_id),
    category_id         INTEGER REFERENCES categories(category_id),
    poll_run_id         INTEGER REFERENCES poll_runs(poll_run_id),
    title               TEXT    NOT NULL,
    url                 TEXT    NOT NULL UNIQUE,
    proposal_count      INTEGER NOT NULL DEFAULT 0,
    posted_at           TEXT    NOT NULL,
    scraped_at          TEXT    NOT NULL DEFAULT (datetime('now')),
    source_query_params TEXT,
    is_read             INTEGER NOT NULL DEFAULT 0
                            CHECK (is_read IN (0, 1)),
    enrichment_status   TEXT    NOT NULL DEFAULT 'pending'
                            CHECK (enrichment_status IN ('pending', 'enriched', 'failed'))
);

CREATE INDEX IF NOT EXISTS idx_projects_owner_id    ON projects (owner_id);
CREATE INDEX IF NOT EXISTS idx_projects_category_id ON projects (category_id);
CREATE INDEX IF NOT EXISTS idx_projects_poll_run_id ON projects (poll_run_id);
CREATE INDEX IF NOT EXISTS idx_projects_posted_at   ON projects (posted_at DESC);
CREATE INDEX IF NOT EXISTS idx_projects_scraped_at  ON projects (scraped_at DESC);
CREATE INDEX IF NOT EXISTS idx_projects_is_read     ON projects (is_read);
CREATE INDEX IF NOT EXISTS idx_projects_status      ON projects (enrichment_status);
```

### Column Reference

| Column | Type | Nullable | Constraint | NULL means / Notes |
|---|---|---|---|---|
| `project_id` | INTEGER | No | PRIMARY KEY | Mostaql's numeric project ID, parsed from the project URL. |
| `owner_id` | INTEGER | No | NOT NULL, FK → owners | Always present — listing page always shows the client. |
| `category_id` | INTEGER | Yes | FK → categories | NULL if the category was not parseable from the listing card. |
| `poll_run_id` | INTEGER | Yes | FK → poll_runs | Which poll discovered this project. Nullable for robustness. |
| `title` | TEXT | No | NOT NULL | Arabic project title, as scraped. Stored as-is (not normalized). |
| `url` | TEXT | No | NOT NULL, UNIQUE | Full canonical URL e.g. `https://mostaql.com/projects/12345-title`. |
| `proposal_count` | INTEGER | No | NOT NULL DEFAULT 0 | Snapshot at scrape time. Never updated (no-update policy). |
| `posted_at` | TEXT | No | NOT NULL | ISO8601 UTC. Resolved from relative Arabic time (`"منذ 4 ساعات"`) at scrape time. Never the relative string. |
| `scraped_at` | TEXT | No | NOT NULL, DEFAULT | ISO8601 UTC when the listing page was fetched. |
| `source_query_params` | TEXT | Yes | — | NULL = the default base URL (no filter). Set to the `query_params` setting value that was active when this project was discovered. |
| `is_read` | INTEGER | No | NOT NULL DEFAULT 0, CHECK | `0` = unread, `1` = read. Follows boolean convention (§8). |
| `enrichment_status` | TEXT | No | NOT NULL DEFAULT 'pending', CHECK | `'pending'` on insert. Updated to `'enriched'` or `'failed'` by the pipeline. |

### SQL Conventions Applied

- **`INSERT OR IGNORE`** — the defining insert mode for this table. The `PRIMARY KEY` constraint is the DB-level backstop (§5, exception 1).
- `is_read` follows boolean convention: `INTEGER`, `0`/`1`, `is_` prefix (§8).
- `enrichment_status` is a TEXT enum with a `CHECK` constraint — not an integer code (§2).
- `posted_at` is ISO8601 TEXT in UTC — relative strings are never stored (§9).
- `enrichment_status` update is the **only** UPDATE ever run on this table (to mark `'enriched'` or `'failed'`). No other column is ever updated.

### Canonical Queries

```sql
-- Insert a newly discovered project (inside enrichment transaction)
INSERT OR IGNORE INTO projects (
    project_id, owner_id, category_id, poll_run_id,
    title, url, proposal_count, posted_at, scraped_at, source_query_params
)
VALUES (
    @projectId, @ownerId, @categoryId, @pollRunId,
    @title, @url, @proposalCount, @postedAt, @scrapedAt, @queryParams
);

-- Get committed IDs for DiffEngine (SqliteCommittedProvider)
SELECT project_id
FROM   projects
WHERE  project_id IN (@id1, @id2, @id3 /*, ... */);

-- Main feed query
SELECT p.project_id, p.title, p.posted_at, p.proposal_count,
       p.is_read, p.enrichment_status,
       o.display_name  AS owner_name,
       c.name          AS category_name,
       pd.budget_min, pd.budget_max, pd.delivery_days
FROM   projects p
JOIN   owners         o  ON p.owner_id    = o.owner_id
LEFT JOIN categories  c  ON p.category_id = c.category_id
LEFT JOIN project_details pd ON p.project_id = pd.project_id
ORDER  BY p.is_read ASC, p.posted_at DESC
LIMIT  @pageSize OFFSET @offset;

-- Mark enrichment complete
UPDATE projects
SET    enrichment_status = @status   -- 'enriched' or 'failed'
WHERE  project_id        = @projectId;

-- Mark a single project as read
UPDATE projects
SET    is_read = 1
WHERE  project_id = @projectId;

-- Mark all as read
UPDATE projects
SET    is_read = 1
WHERE  is_read = 0;

-- Unread count (footer status bar)
SELECT COUNT(*) FROM projects WHERE is_read = 0;

-- Total count (footer status bar)
SELECT COUNT(*) FROM projects;
```

---

## 8. `project_details`

### Purpose

Enrichment data fetched from the **detail page** (Tier 2 scrape). A strict 1:1 extension of `projects`. This row does not exist while `enrichment_status = 'pending'` or `'failed'`. It is created **in the same transaction** as the `enrichment_status` update to `'enriched'`. The separation from `projects` enforces the pipeline lifecycle in the schema.

### Functional Dependencies

```
project_id → description
project_id → budget_min
project_id → budget_max
project_id → delivery_days
project_id → enriched_at
```

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | `description` is one text block. `budget_min`/`budget_max` are two separate REAL columns (not `"250 - 500"` as a string). |
| **2NF** | ✅ | Single-column PK. |
| **3NF** | ✅ | No transitive dependencies. `budget_min` does not determine `budget_max` — they are independent facets of the budget range. `delivery_days` does not determine anything else. |
| **BCNF** | ✅ | Only `project_id` is a determinant. |

> **Why is `budget_min`/`budget_max` not a 3NF violation?**
> One might argue: `budget_min → budget_max` (if you know the minimum, you know the maximum). This is not a functional dependency in the relational sense — knowing `budget_min = 250` does not tell you `budget_max` is `500`; different projects with `budget_min = 250` have different `budget_max` values. They are two independent facts scraped from the source page.

### DDL

```sql
CREATE TABLE IF NOT EXISTS project_details (
    project_id    INTEGER PRIMARY KEY
                      REFERENCES projects(project_id) ON DELETE CASCADE,
    description   TEXT    NOT NULL,
    budget_min    REAL,
    budget_max    REAL,
    delivery_days INTEGER,
    enriched_at   TEXT    NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_details_budget   ON project_details (budget_min, budget_max);
CREATE INDEX IF NOT EXISTS idx_details_delivery ON project_details (delivery_days);
```

### Column Reference

| Column | Type | Nullable | Constraint | NULL means |
|---|---|---|---|---|
| `project_id` | INTEGER | No | PRIMARY KEY, FK → projects | Shared PK with `projects`. Row does not exist until enrichment succeeds. |
| `description` | TEXT | No | NOT NULL | Full project description, as scraped. May be multi-paragraph Arabic text. |
| `budget_min` | REAL | Yes | — | NULL if the client specified no budget, or if the budget string was not parseable. |
| `budget_max` | REAL | Yes | — | NULL under the same conditions as `budget_min`. |
| `delivery_days` | INTEGER | Yes | — | NULL if no delivery timeframe was specified. |
| `enriched_at` | TEXT | No | NOT NULL, DEFAULT | ISO8601 UTC when enrichment completed and this row was committed. |

### SQL Conventions Applied

- `ON DELETE CASCADE` — if a `projects` row were ever deleted (hypothetically), its `project_details` row would auto-delete. In practice, projects are never deleted under the no-update policy. The cascade is a schema integrity safeguard.
- Budget stored as two REAL columns — not as a formatted string (§2, 1NF).
- `delivery_days` as INTEGER — not stored as `"20 يوما"` (parsing happens in C#, §3.1).

### Canonical Queries

```sql
-- Insert detail row (inside enrichment transaction, after projects INSERT OR IGNORE)
INSERT INTO project_details (project_id, description, budget_min, budget_max, delivery_days, enriched_at)
VALUES (@projectId, @description, @budgetMin, @budgetMax, @deliveryDays, @enrichedAt);

-- Load detail for project detail page
SELECT pd.description, pd.budget_min, pd.budget_max, pd.delivery_days, pd.enriched_at
FROM   project_details pd
WHERE  pd.project_id = @projectId;

-- Budget range filter (query builder)
SELECT p.project_id
FROM   projects p
JOIN   project_details pd ON p.project_id = pd.project_id
WHERE  pd.budget_max >= @minBudget
  AND  pd.budget_min <= @maxBudget;
```

---

## 9. `project_skills`

### Purpose

Many-to-many join table between `projects` and `skills`. A project may require multiple skills; a skill may appear in multiple projects. No non-key attributes — this table is a pure association.

### Functional Dependencies

The only functional dependency is the trivial one: the full composite key determines itself.

```
{project_id, skill_id} → {} (no non-key attributes)
```

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | Composite PK with two atomic columns. No repeating groups. |
| **2NF** | ✅ | No non-key attributes exist — the table has only PK columns. Partial dependency is impossible with no non-key attributes. |
| **3NF** | ✅ | Same reasoning — nothing to transitively depend on anything. |
| **BCNF** | ✅ | The only determinant is the full composite key, which is the PK. |
| **4NF** | ✅ | A 4NF concern would arise if project_id and skill_id were two independent multivalued facts (e.g. project → skills AND project → something_else). Here the pair is one fact: "this project requires this skill." No 4NF violation. |

### DDL

```sql
CREATE TABLE IF NOT EXISTS project_skills (
    project_id INTEGER NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    skill_id   INTEGER NOT NULL REFERENCES skills(skill_id),
    PRIMARY KEY (project_id, skill_id)
);

-- Reverse lookup: "which projects require skill X?"
CREATE INDEX IF NOT EXISTS idx_project_skills_skill ON project_skills (skill_id);
```

### Column Reference

| Column | Type | Nullable | Constraint |
|---|---|---|---|
| `project_id` | INTEGER | No | NOT NULL, FK → projects, part of PK |
| `skill_id` | INTEGER | No | NOT NULL, FK → skills, part of PK |

### Canonical Queries

```sql
-- Insert skills for a project (inside enrichment transaction)
INSERT OR IGNORE INTO project_skills (project_id, skill_id) VALUES (@projectId, @skillId);
-- (repeated for each skill in the project)

-- All skills for a project
SELECT s.skill_id, s.name
FROM   skills s
JOIN   project_skills ps ON s.skill_id = ps.skill_id
WHERE  ps.project_id = @projectId
ORDER  BY s.name ASC;

-- Projects that require a given skill
SELECT ps.project_id
FROM   project_skills ps
WHERE  ps.skill_id = @skillId;
```

---

## 10. `assets`

### Purpose

Project attachments and images, populated only when `settings.include_assets = '1'`. A project may have zero or many assets. `local_path` is NULL until the file is downloaded — a row exists as soon as the source URL is known, even before the download completes. This allows the UI to show a "downloading…" state without a separate status column.

### Functional Dependencies

```
asset_id   → project_id
asset_id   → source_url
asset_id   → local_path
asset_id   → file_name
asset_id   → mime_type
asset_id   → file_size_bytes
asset_id   → downloaded_at
```

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | One URL, one path, one file name per row. No list columns. |
| **2NF** | ✅ | Single-column PK. |
| **3NF** | ✅ | Could `local_path → file_name` be a transitive dependency? Only if `local_path` uniquely determined `file_name` as a relational fact. But `local_path` is NULL until download, so it cannot be a determinant for un-downloaded assets. Once downloaded, the local path does contain the filename — but we store `file_name` separately as a convenience, not as a derived value. This is a pragmatic denormalization of a physical path, not a logical transitive dependency. |
| **BCNF** | ✅ | Only determinant is `asset_id`. |

### DDL

```sql
CREATE TABLE IF NOT EXISTS assets (
    asset_id        INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id      INTEGER NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    source_url      TEXT    NOT NULL,
    local_path      TEXT,
    file_name       TEXT,
    mime_type       TEXT,
    file_size_bytes INTEGER,
    downloaded_at   TEXT
);

CREATE INDEX IF NOT EXISTS idx_assets_project ON assets (project_id);
```

### Column Reference

| Column | Type | Nullable | Constraint | NULL means |
|---|---|---|---|---|
| `asset_id` | INTEGER | No | PRIMARY KEY AUTOINCREMENT | Surrogate key. |
| `project_id` | INTEGER | No | NOT NULL, FK → projects | Which project owns this asset. |
| `source_url` | TEXT | No | NOT NULL | Original URL on Mostaql's CDN. |
| `local_path` | TEXT | Yes | — | NULL until the file is downloaded to disk. |
| `file_name` | TEXT | Yes | — | Original filename from URL. NULL if not determinable from URL. |
| `mime_type` | TEXT | Yes | — | e.g. `"image/png"`, `"application/pdf"`. NULL until determined (usually at download). |
| `file_size_bytes` | INTEGER | Yes | — | NULL until downloaded. |
| `downloaded_at` | TEXT | Yes | — | NULL until download completes. ISO8601 UTC. |

### Canonical Queries

```sql
-- Insert asset source URL (before download)
INSERT INTO assets (project_id, source_url, file_name)
VALUES (@projectId, @sourceUrl, @fileName);

-- Update after successful download
UPDATE assets
SET    local_path      = @localPath,
       mime_type       = @mimeType,
       file_size_bytes = @fileSizeBytes,
       downloaded_at   = @downloadedAt
WHERE  asset_id        = @assetId;

-- All assets for a project (detail page)
SELECT asset_id, source_url, local_path, file_name, mime_type, downloaded_at
FROM   assets
WHERE  project_id = @projectId
ORDER  BY asset_id ASC;
```

---

## 11. `notifications`

### Purpose

Persistent log of every toast notification fired by the app. Serves the tray "recent notifications" submenu (last 10 entries) and the v2 notification center page. `project_id` is NULL for grouped toasts that reference multiple projects.

### Functional Dependencies

```
notification_id → project_id
notification_id → type
notification_id → title
notification_id → body
notification_id → project_count
notification_id → fired_at
notification_id → dismissed_at
notification_id → clicked_at
```

### Normalization Proof

| Form | Satisfied? | Reasoning |
|---|---|---|
| **1NF** | ✅ | All values atomic. A grouped toast does not store multiple `project_id`s — it stores `NULL` and a `project_count` integer. |
| **2NF** | ✅ | Single-column PK. |
| **3NF** | ✅ | `type` does not determine `project_count`. `title` does not determine `body`. `dismissed_at` does not determine `clicked_at`. Each column is an independent fact about this notification event. |
| **BCNF** | ✅ | Only determinant is `notification_id`. |

### DDL

```sql
CREATE TABLE IF NOT EXISTS notifications (
    notification_id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id      INTEGER REFERENCES projects(project_id),
    type            TEXT    NOT NULL CHECK (type IN ('individual', 'grouped')),
    title           TEXT    NOT NULL,
    body            TEXT    NOT NULL,
    project_count   INTEGER NOT NULL DEFAULT 1,
    fired_at        TEXT    NOT NULL DEFAULT (datetime('now')),
    dismissed_at    TEXT,
    clicked_at      TEXT
);

CREATE INDEX IF NOT EXISTS idx_notifications_fired   ON notifications (fired_at DESC);
CREATE INDEX IF NOT EXISTS idx_notifications_project ON notifications (project_id);
```

### Column Reference

| Column | Type | Nullable | Constraint | NULL means |
|---|---|---|---|---|
| `notification_id` | INTEGER | No | PRIMARY KEY AUTOINCREMENT | Surrogate key. |
| `project_id` | INTEGER | Yes | FK → projects | NULL for grouped toasts. Set for individual toasts. |
| `type` | TEXT | No | NOT NULL, CHECK | `'individual'` or `'grouped'`. |
| `title` | TEXT | No | NOT NULL | Toast headline shown to the user. |
| `body` | TEXT | No | NOT NULL | Toast body text. |
| `project_count` | INTEGER | No | NOT NULL DEFAULT 1 | Always 1 for individual toasts. > 1 for grouped toasts. |
| `fired_at` | TEXT | No | NOT NULL, DEFAULT | ISO8601 UTC when the toast was shown. |
| `dismissed_at` | TEXT | Yes | — | NULL until the user dismisses the toast. |
| `clicked_at` | TEXT | Yes | — | NULL until the user clicks the toast. |

### Canonical Queries

```sql
-- Log an individual notification
INSERT INTO notifications (project_id, type, title, body, project_count, fired_at)
VALUES (@projectId, 'individual', @title, @body, 1, @firedAt);

-- Log a grouped notification
INSERT INTO notifications (project_id, type, title, body, project_count, fired_at)
VALUES (NULL, 'grouped', @title, @body, @count, @firedAt);

-- Recent notifications for tray submenu
SELECT notification_id, project_id, type, title, body, fired_at
FROM   notifications
ORDER  BY fired_at DESC
LIMIT  10;

-- Mark clicked
UPDATE notifications
SET    clicked_at = @clickedAt
WHERE  notification_id = @notificationId;
```

---

## 12. `projects_fts`

### Purpose

FTS5 virtual table providing full-text search over `title`, `description`, and concatenated skill names. Maintained in the same transaction as each `project_details` insert — always consistent with the main tables.

### Normalization Note

FTS5 virtual tables are **search indexes**, not relational tables. Standard normalization theory does not apply. The FTS table intentionally denormalizes data (duplicates `title` from `projects`, `description` from `project_details`, and concatenated skills) to build the inverted index. This is the correct design — search indexes are always a controlled, intentional denormalization.

### DDL

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS projects_fts USING fts5 (
    project_id  UNINDEXED,
    title,
    description,
    skills_text,
    tokenize = 'unicode61 remove_diacritics 2'
);
```

### Column Reference

| Column | FTS-indexed? | Source | Notes |
|---|---|---|---|
| `project_id` | No (`UNINDEXED`) | `projects.project_id` | Used for JOIN back to `projects` after search. Not indexed because you'd never search "find documents where project_id matches the word '12345'". |
| `title` | Yes | `projects.title` | App-normalized before insert (Alef folding, diacritics stripped). |
| `description` | Yes | `project_details.description` | App-normalized before insert. |
| `skills_text` | Yes | Concatenated `skills.name` for this project | Space-joined list, e.g. `"PHP MySQL تطوير الويب"`. App-normalized before insert. |

### SQL Conventions Applied

FTS5 is an accepted SQLite-specific exception (§5, exception 3 in conventions). The following special rules apply **only** to this virtual table:

- `MATCH` operator is used instead of standard `WHERE` for FTS queries.
- The `rank` implicit column is used for relevance ordering.
- All other SQL conventions (parameterized queries, naming, etc.) apply normally.

### Canonical Queries

```sql
-- Insert into FTS (inside enrichment transaction)
INSERT INTO projects_fts (project_id, title, description, skills_text)
VALUES (@projectId, @normalizedTitle, @normalizedDescription, @normalizedSkillsText);

-- FTS search with structured filter combined
SELECT pf.project_id,
       pf.rank
FROM   projects_fts pf
JOIN   projects p ON pf.project_id = p.project_id
WHERE  projects_fts MATCH @normalizedQuery
  AND  p.is_read = @isRead
ORDER  BY pf.rank
LIMIT  200;
-- → top 200 results handed to Layer 2 (app-side fuzzy re-ranker)
```

---

## Summary: Normal Form per Table

| Table | 1NF | 2NF | 3NF | BCNF | Notes |
|---|---|---|---|---|---|
| `schema_migrations` | ✅ | ✅ | ✅ | ✅ | Trivial — single PK, two attributes |
| `settings` | ✅ | ✅ | ✅ | ✅ | Key-value pattern — documented exception for config only |
| `categories` | ✅ | ✅ | ✅ | ✅ | Two candidate keys (`category_id`, `name`) |
| `skills` | ✅ | ✅ | ✅ | ✅ | Two candidate keys (`skill_id`, `name`) |
| `owners` | ✅ | ✅ | ✅ | ✅ | All stats are independent facts about `owner_id` |
| `poll_runs` | ✅ | ✅ | ✅ | ✅ | Audit log; all columns are pipeline execution facts |
| `projects` | ✅ | ✅ | ✅ | ✅ | Two candidate keys (`project_id`, `url`); FK columns are not transitive |
| `project_details` | ✅ | ✅ | ✅ | ✅ | `budget_min`/`budget_max` are independent facts, not mutually determining |
| `project_skills` | ✅ | ✅ | ✅ | ✅ | Pure junction table; no non-key attributes; 4NF satisfied |
| `assets` | ✅ | ✅ | ✅ | ✅ | `local_path`/`file_name` co-location is pragmatic, not a logical transitive dependency |
| `notifications` | ✅ | ✅ | ✅ | ✅ | `project_id` nullable for grouped toasts by design |
| `projects_fts` | N/A | N/A | N/A | N/A | Search index — normalization theory does not apply |

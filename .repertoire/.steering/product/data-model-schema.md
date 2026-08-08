# Data model & schema

[← Back to wiki home](./README.md)

## Table of contents
- [Storage engine choice](#storage-engine-choice)
- [`projects`](#projects)
- [`owners`](#owners)
- [`project_skills`](#project_skills)
- [`assets`](#assets)
- [Search index](#search-index)
- [Field parsing notes](#field-parsing-notes)

## Storage engine choice

An **embedded, single-file database** — SQLite is the default choice, but the requirement is "embedded single-file," not strictly SQLite. SQLite-compatible alternatives (e.g. libSQL) are viable drop-in swaps if a specific feature (native vector search, sync primitives) becomes worth the trade later. See [search-and-filtering.md § storage engine tradeoff](./search-and-filtering.md#storage-engine-tradeoff) for the specific decision around fuzzy search support.

## `projects`

The core table. One row per `project_id`, write-once (see [no-update policy](./architecture-pipeline.md#no-update-policy)).

| Column | Type | Notes |
|---|---|---|
| `project_id` | INTEGER PRIMARY KEY | Mostaql's numeric project ID, parsed from the URL |
| `title` | TEXT | |
| `url` | TEXT | Full canonical project URL |
| `description` | TEXT | Full detail-page description |
| `owner_id` | INTEGER | FK → `owners.owner_id` |
| `category` | TEXT | |
| `budget_min` | REAL | Nullable — parsed from e.g. "$250.00 - $500.00" |
| `budget_max` | REAL | Nullable |
| `delivery_days` | INTEGER | Nullable — parsed from e.g. "20 يوما" |
| `proposal_count` | INTEGER | Snapshot at scrape time only |
| `posted_at` | DATETIME | Resolved from relative time ("منذ 4 ساعات") to absolute at scrape time |
| `scraped_at` | DATETIME | When this app fetched it |
| `is_read` | BOOLEAN | Default `false`. See [ui-ux-design.md § unread highlighting](./ui-ux-design.md#unreadread-highlighting) |
| `enrichment_status` | TEXT | `pending` \| `enriched` \| `failed` |
| `source_query_params` | TEXT | The `query_params` value active on the poll that discovered this row (nullable) |

`project_id` uniqueness is the concurrency backstop described in [architecture-pipeline.md § in-flight tracking](./architecture-pipeline.md#in-flight-tracking).

## `owners`

Client/project-owner profiles, deduplicated — the same client posts multiple projects, so their stats live in one place rather than being copied per-project.

| Column | Type | Notes |
|---|---|---|
| `owner_id` | INTEGER PRIMARY KEY | Derived from owner profile URL |
| `display_name` | TEXT | e.g. "صالح ا." |
| `title` | TEXT | e.g. "مهندس أمن معلومات" |
| `joined_at` | DATE | "تاريخ التسجيل" |
| `hire_rate` | TEXT/REAL | "معدل التوظيف" — nullable, may be "لم يحسب بعد" (not yet calculated) |
| `open_projects_count` | INTEGER | "المشاريع المفتوحة" |
| `in_progress_projects_count` | INTEGER | "مشاريع قيد التنفيذ" |
| `ongoing_communications_count` | INTEGER | "التواصلات الجارية" |
| `last_seen_at` | DATETIME | Updated (this table only) when the same owner is encountered again — owner stats are the one exception to strict no-update, since they're a shared reference row, not a project snapshot |

## `project_skills`

Many-to-many join, since a project can list multiple skills.

| Column | Type |
|---|---|
| `project_id` | INTEGER, FK → `projects.project_id` |
| `skill` | TEXT |

## `assets`

Populated only when [`include_assets`](./configuration-reference.md#include_assets) is enabled.

| Column | Type | Notes |
|---|---|---|
| `asset_id` | INTEGER PRIMARY KEY | |
| `project_id` | INTEGER, FK | |
| `source_url` | TEXT | Original attachment/image URL |
| `local_path` | TEXT | Saved to `assets/{project_id}/…` on disk — path stored, not a BLOB, to keep the DB itself small and fast |
| `downloaded_at` | DATETIME | |

## Search index

An FTS5 (or engine-equivalent) virtual table shadowing `title`, `description`, and concatenated `project_skills.skill`, maintained incrementally in the same transaction as each `projects` insert. Full rationale and tokenizer/normalization strategy: [search-and-filtering.md](./search-and-filtering.md).

## Field parsing notes

Source strings from the listing/detail pages require normalization before storage:

| Source text | Parsed to |
|---|---|
| "منذ 4 ساعات" | Absolute `posted_at` datetime (resolve relative time at scrape time — never store the relative string as the sortable value) |
| "61 عرض" / "عرض واحد" / "عرضان" / "أضف أول عرض" | `proposal_count` integer (61 / 1 / 2 / 0) |
| "$250.00 - $500.00" | `budget_min` = 250.00, `budget_max` = 500.00 |
| "20 يوما" | `delivery_days` = 20 |
| "لم يحسب بعد" (owner hire rate) | stored as `NULL`, not a string sentinel |

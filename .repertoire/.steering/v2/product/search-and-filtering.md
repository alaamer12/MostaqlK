# Search & filtering

[← Back to wiki home](../../base/product/README.md)

## Table of contents
- [Query builder UX](#query-builder-ux)
- [Filterable / sortable fields](#filterable--sortable-fields)
- [Safety: no raw SQL from the UI](#safety-no-raw-sql-from-the-ui)
- [Fuzzy search requirements](#fuzzy-search-requirements)
- [Storage engine tradeoff](#storage-engine-tradeoff)
- [Incremental FTS maintenance](#incremental-fts-maintenance)

## Query builder UX

A chip + dropdown builder over the local archive — **not** a filter on what gets fetched from Mostaql (that's [`query_params`](../../v1/product/configuration-reference.md#query_params), a separate and unrelated concept). Each chip is `{field, operator, value}`; chips AND together. Example:

```
[category = development]  [budget_max ≥ 500]  [unread only]
```

Sort is a separate control: any structured field, ascending/descending, plus a `relevance` option that appears only when a search query is active.

## Filterable / sortable fields

All sourced from the [schema](../../base/product/data-model-schema.md):

- `title`, `category`, `posted_at`, `proposal_count`
- `budget_min`, `budget_max`, `delivery_days`
- `skills` (via `project_skills`, `contains`/`in [...]` operator)
- Owner fields: `owner.hire_rate`, `owner.open_projects_count`, `owner.in_progress_projects_count`, `owner.joined_at`
- `is_read` (boolean)
- `enrichment_status`

## Safety: no raw SQL from the UI

User input never reaches the database as a raw string. Each chip is validated against a **field/operator whitelist** and compiled into a parameterized query, e.g.:

```
[{field: "budget_max", op: "gte", value: 500},
 {field: "category",   op: "eq",  value: "development"}]
```

compiles to a parameterized `WHERE` clause server-side (app-side) — never string concatenation.

## Fuzzy search requirements

Full-text search across `title` + `description` + `skills`, incrementally maintained (no full rebuild per insert), supporting both Arabic and English with typo/partial-match tolerance, combinable with the structured filters above.

**Why plain `LIKE` isn't enough:** full table scan, no ranking, no typo tolerance, and no help with Arabic script variation (تطبيق / تطبيقات / للتطبيق should reasonably be related; `LIKE '%query%'` treats them as unrelated strings).

**Arabic-specific concerns:** default tokenizers don't fold Alef variants (ا/أ/إ/آ), don't normalize ة/ه or ي/ى interchange, and don't strip diacritics — all of which real user input varies inconsistently.

## Storage engine tradeoff

Two respectable approaches were weighed:

| Approach | Pros | Cons |
|---|---|---|
| **Loadable extensions** (`spellfix1` for fuzzy edit-distance, ICU for proper Unicode/Arabic tokenization/collation) | "Textbook correct" — native, well-tested language handling | Requires compiling/bundling native extensions and correct runtime loading across every user's Windows machine — real packaging/support risk for a distributed single-exe app |
| **FTS5 (built into SQLite core) + app-side fuzzy re-ranking** (e.g. Rust `strsim`/`fuzzy-matcher` crates) | No extension-loading risk, ships cleanly inside the existing Tauri/Rust binary, mature Unicode-safe string-distance libraries | Slightly more logic lives in application code rather than the DB |

**Decision:** FTS5 + app-side fuzzy re-ranking, with a **normalize-at-write-time** step (fold Alef variants, strip diacritics, normalize ة/ي variants) applied to both indexed text and incoming queries. This is the standard, low-risk approach for a distributed desktop app; extension-based approaches remain a documented option if search quality needs later warrant the added deployment complexity.

The storage engine itself does not need to be strictly SQLite — any embedded, single-file, SQLite-API-compatible engine (e.g. libSQL) is an acceptable substitute if a specific future need (native vector search, sync primitives) makes the swap worthwhile. See [data-model-schema.md § storage engine choice](../../base/product/data-model-schema.md#storage-engine-choice).

## Incremental FTS maintenance

Every successful `projects` insert (from the [enrichment pipeline](../../base/product/architecture-pipeline.md#two-tier-request-flow)) writes to the FTS table in the **same transaction** — no batch reindex job, the search index stays current automatically as the archive grows.

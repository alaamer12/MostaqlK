# MVP

[← Back to wiki home](./README.md)

This doc is the actual build target for v1. Everything else in this wiki — [`DESIGN.md`](./DESIGN.md), [search-and-filtering.md](./search-and-filtering.md), grouping config, assets, mobile/push — is **reference for future versions**, not part of what gets built now. The architecture is built so those slot in later without rework, but none of them are implemented in MVP.

## Table of contents
- [What MVP is](#what-mvp-is)
- [In scope](#in-scope)
- [Explicitly out of scope](#explicitly-out-of-scope)
- [Plug-and-play seams](#plug-and-play-seams)
- [MVP data model (minimal)](#mvp-data-model-minimal)
- [Definition of done](#definition-of-done)

## What MVP is

Three things, and only three things:

1. **The parser/pipeline** — poll the listing, diff, enqueue, enrich, commit. See [architecture-pipeline.md](./architecture-pipeline.md) for the full mechanics; MVP builds exactly that, with no special-cased backlog handling (the earlier "cold start" idea was folded into the general queue/worker design — see [architecture-pipeline.md § backlog handling](./architecture-pipeline.md#backlog-handling-no-special-cold-start) — MVP implements that unified version from day one, not a separate mode later).
2. **Storage** — the embedded DB, write-once, no-update. See [data-model-schema.md](./data-model-schema.md), trimmed to the [MVP subset](#mvp-data-model-minimal) below.
3. **Notifications** — one native toast per newly committed project. No grouping, no batching logic, no push, no mobile — just: new row committed → toast fires with its details.

Everything else (visual polish, RTL, theming, query builder, search, asset downloads, `query_params`, mobile/sync) is deliberately deferred so the pipeline itself gets built and proven correct first.

## In scope

- Listing poll on a fixed interval (hardcoded or minimally configurable — no UI required for this in MVP, a config value is enough)
- Diff against DB **and** in-flight set (the concurrency-safety mechanism is not optional or a "later" item — it's required from the first line of pipeline code, since without it the pipeline is wrong, not just incomplete). See [architecture-pipeline.md § in-flight tracking](./architecture-pipeline.md#in-flight-tracking)
- Bounded worker pool for detail enrichment, shared rate budget across both tiers
- No-update / store-and-forget persistence
- Tray icon with minimal state (running / error) — no polished icon states beyond that
- One toast per new project: title, client, time, proposal count, link
- A bare-functional window or even just a list view sufficient to prove data is being stored correctly — **not** the designed UI from [ui-ux-design.md](./ui-ux-design.md) or [DESIGN.md](./DESIGN.md)

## Explicitly out of scope

- Any visual design system work — colors, theming, RTL, typography, icon tiers, onboarding illustrations ([DESIGN.md](./DESIGN.md) in full)
- `query_params` override — MVP polls the fixed base URL only
- `include_assets` — no attachment/image downloading
- Notification grouping/batching config — MVP is always one toast per project, no modes
- Query builder, filters, sort — no UI for browsing beyond a plain list
- Fuzzy/FTS search ([search-and-filtering.md](./search-and-filtering.md))
- Read/unread highlighting — `is_read` column can exist in schema but no UI treats it specially yet
- Owner profile enrichment beyond whatever's trivially on the detail page — no dedicated `owners` table logic required yet if it adds friction
- Everything in [roadmap-future.md](./roadmap-future.md) — mobile, LAN sync, push

## Plug-and-play seams

The point of building the pipeline first is that later features attach without restructuring it. Concretely, that means MVP code should:

- Keep the listing-poll target as a single configurable URL string internally, even though no UI exposes it yet — so `query_params` later is a UI addition, not a pipeline change.
- Keep enrichment as a discrete step that produces one full project record — so `include_assets` later just adds a sub-step inside that function, not a new pipeline stage.
- Fire notifications through a single internal function (`notify(project)`), even though MVP always calls it once per project — so grouping/batching later wraps *calls* to that function, not the commit logic itself.
- Keep the DB schema close to the [full schema](./data-model-schema.md#projects) even if some columns are unused in MVP (e.g. `is_read`, `source_query_params`) — cheaper to have unused columns now than to migrate later.
- Keep the window as a genuinely separate layer reading from the same DB — so the [designed UI](./ui-ux-design.md) can replace it later without touching the pipeline at all.

## MVP data model (minimal)

A trimmed version of [data-model-schema.md § projects](./data-model-schema.md#projects) — only what the pipeline itself needs to function correctly:

| Column | Required for MVP? |
|---|---|
| `project_id` | yes — PK, concurrency backstop |
| `title`, `url`, `description` | yes |
| `budget_min`, `budget_max`, `delivery_days`, `category`, `proposal_count` | yes — trivial to parse alongside description, no reason to drop |
| `posted_at`, `scraped_at` | yes |
| `enrichment_status` | yes — needed for the worker pool's own bookkeeping |
| `owner_id` / `owners` table | optional — inline `owner_name` string is enough for MVP if a full owners table adds friction |
| `is_read` | schema-only, no UI logic yet |
| `source_query_params` | schema-only, unused until `query_params` ships |
| `project_skills`, `assets` tables | not created in MVP — added when search/assets ship |

## Definition of done

MVP is done when: the app runs unattended in the background, correctly discovers every new project within one poll interval, never duplicates or drops one under concurrent load, stores full details for each, and fires exactly one accurate notification per discovery — with nothing else attached yet.

# Architecture & pipeline

[← Back to wiki home](./README.md)

## Table of contents
- [Process model](#process-model)
- [Two-tier request flow](#two-tier-request-flow)
- [Backlog handling — no special cold start](#backlog-handling-no-special-cold-start)
- [In-flight tracking](#in-flight-tracking)
- [Rate limiting](#rate-limiting)
- [No-update policy](#no-update-policy)

## Process model

Single Tauri process:
- **Rust backend** — scheduler, HTTP client, HTML parsing, DB access, rate limiter, notification dispatch. Runs independent of window visibility.
- **React frontend** — main window (feed, search, settings). Closing the window hides it; it does not terminate the backend. Only "Quit" from the tray menu exits the process.
- **Tray icon** — always present. Icon state reflects: idle, polling, backlog draining, or error. Right-click menu: Pause polling, Open window, Quit.

## Two-tier request flow

```
[Listing poll] --(new IDs)--> [Discovery queue] --> [Worker pool] --(enriched row)--> [DB commit] --> [Notification]
```

1. **Listing poll** (Tier 1) — fetch the projects listing (base URL, optionally modified by [`query_params`](../../v1/product/configuration-reference.md#query_params)). Parse each card: `project_id`, `title`, `url`, `client_name`, `posted_relative`, `proposal_count`.
2. **Diff** — compare parsed IDs against DB + [in-flight set](#in-flight-tracking) (not DB alone — see below). Anything in neither is genuinely new.
3. **Enqueue** — new IDs are pushed to a FIFO discovery queue.
4. **Worker pool** (Tier 2) — a fixed number of async workers (configurable concurrency cap) pull from the queue, fetch the detail page, parse the full record, and commit it to the DB in one transaction (including the [search index](../../v2/product/search-and-filtering.md#incremental-fts-maintenance) and, if [`include_assets`](../../v1/product/configuration-reference.md#include_assets) is on, downloaded attachments).
5. **Notification** — on commit, either an individual toast or a [grouped summary toast](../../v1/product/ui-ux-design.md#notification-grouping) fires, depending on config.

Both tiers draw from the same [shared rate budget](#rate-limiting) — a burst of detail fetches never causes the total outbound rate to exceed the configured limit.

## Backlog handling — no special cold start

There is no distinct "cold start" or "backfill mode." A fresh install with an empty DB, and an existing install relaunched after five days offline, both simply produce a *large diff* on the first poll — potentially 20+ new IDs at once. This is handled by the exact same queue + worker pool as a normal 1–2 item poll; the pipeline doesn't know or care why the batch is large.

This also holds for genuine bursts (e.g. 20 real projects posted within an actual 2-minute window) — same code path, same fairness guarantees.

**Fairness rule:** the queue is FIFO. A later poll's new arrivals are appended after an existing backlog, never jumping ahead of it. New listing polls continue to fire on schedule regardless of queue depth — they just keep diffing and appending; they never block waiting for the queue to drain.

Large backlogs naturally interact well with [notification grouping](../../v1/product/ui-ux-design.md#notification-grouping) — a 20-item backlog draining over several minutes produces grouped summary toasts rather than a special-cased suppression rule.

## In-flight tracking

**The problem:** diffing only against the DB is unsafe under concurrency. If poll cycle N enqueues 20 IDs and most are still mid-enrichment (not yet committed) when poll cycle N+1 fires, N+1 will see those same IDs as "not in DB" and re-enqueue them — causing duplicate detail fetches (wasted rate budget) and duplicate insert attempts (constraint violations or, worse, silent duplicate rows).

**The fix:** track a third state beyond "in DB" / "not in DB" — an in-memory `in_flight_ids` set.

| State | Meaning | Diff outcome |
|---|---|---|
| `unseen` | not in DB, not in `in_flight_ids` | enqueue |
| `in_flight` | queued or actively being enriched, not yet committed | skip |
| `committed` | already in DB | skip |

- `project_id` is added to `in_flight_ids` the moment it's enqueued.
- It's removed the moment enrichment either commits successfully or fails permanently (always via a `finally`-equivalent — a failure must never leave an ID stuck as permanently invisible to future polls).
- Diff logic: `new_to_enqueue = listing_ids − db_ids − in_flight_ids`.
- On process restart, `in_flight_ids` is naturally empty — anything that was mid-enrichment but uncommitted before a crash/close is simply picked up fresh on the next poll (correct behavior: it resumes rather than silently drops).

**Defensive backstop:** `project_id` is a `PRIMARY KEY`/`UNIQUE` constraint in the DB regardless. Inserts use `INSERT OR IGNORE` / `ON CONFLICT DO NOTHING` — even if in-flight tracking has a bug, a duplicate insert fails cleanly instead of corrupting the archive.

## Rate limiting

- A single shared token-bucket budget covers **both** listing polls and detail/asset fetches — configurable requests-per-minute (default ~2/min, adjustable).
- Detail fetches run with a bounded concurrency cap (e.g. 2–5 concurrent workers) and a minimum inter-request spacing, so a burst of new discoveries doesn't spike simultaneous connections even within the per-minute budget.
- On HTTP errors (429/5xx), the affected request backs off exponentially rather than retrying immediately — this does not stall the rest of the pipeline.

## No-update policy

Once a `project_id` is committed, it is **never re-fetched or mutated**. This is a deliberate scope boundary ("store and forget"), not an oversight:

- Status transitions (open → closed, etc.) are not tracked and not detectable by this app.
- `proposal_count` and every other field reflect a snapshot at scrape time, permanently.
- No polling cycle ever revisits an already-committed ID's detail page.
- Consequence for schema: no `updated_at`, no status-history table, no revisit queue needed — `scraped_at` is sufficient. See [data-model-schema.md](data-model-schema.md).

This keeps the entire rate budget spent on *discovery of new projects* — the actual value of the tool — rather than splitting it with staleness-chasing.

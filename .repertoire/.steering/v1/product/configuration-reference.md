# Configuration reference

[← Back to wiki home](../../base/product/README.md)

## Table of contents
- [Polling & rate](#polling--rate)
- [`query_params`](#query_params)
- [`include_assets`](#include_assets)
- [Notification grouping](#notification-grouping)
- [Push notifications (v3)](#push-notifications-v3)
- [Full settings table](#full-settings-table)

## Polling & rate

- `poll_interval_seconds` — how often the listing page is re-fetched. Default: 30.
- `max_requests_per_minute` — shared budget across listing polls + detail/asset fetches. Default: 2/min, user-adjustable. See [architecture-pipeline.md § rate limiting](../../base/product/architecture-pipeline.md#rate-limiting).
- `max_concurrent_detail_fetches` — worker pool size for Tier 2 enrichment. Default: 2–3.
- `safe_requests` — how strictly `max_requests_per_minute` is enforced. Default: **true**.
  - **On (default):** the shared token bucket follows [worker-pool-and-rate-limiter.md](../tech/worker-pool-and-rate-limiter.md#shared-rate-limiter-token-bucket) exactly — capacity equals `max_requests_per_minute`, tokens refill at `rpm / 60` per second, and consecutive requests are spaced by at least 1s (the "minimum inter-request spacing" from [architecture-pipeline.md § rate limiting](../../base/product/architecture-pipeline.md#rate-limiting)).
  - **Off:** the limiter keeps the same `max_requests_per_minute` burst capacity but refills it 10× faster with no minimum spacing (FIX: this used to be a fixed 10-request/1-per-second floor that ignored a lower configured `max_requests_per_minute`, making the setting look hard-coded/inert whenever it was below 10; it now always scales off whatever budget is configured). A large backlog drains far faster, at a materially higher risk of being blocked by the site. Offered as an explicit, opt-in escape hatch only.

## `query_params`

A single optional string, e.g. `category=development&sort=latest`, with or without a leading `?` (the app normalizes it).

- **Empty** → poll `https://mostaql.com/projects`.
- **Set** → poll `https://mostaql.com/projects{normalized_params}` instead, from the next poll cycle onward.
- **Not retroactive.** Changing this value never re-classifies, re-fetches, or removes anything already stored. It only changes what the *next* poll requests. There is exactly one active polling target at any time — this is not a multi-watchlist system.
- Each row stores which `query_params` value was active when it was discovered (`projects.source_query_params`), purely for reference/filtering later.

## `include_assets`

Boolean. Default: **false**.

- When true, detail enrichment also identifies and downloads attachment/image URLs on the project page, saving them under `assets/{project_id}/` and recording paths in the [`assets` table](../../base/product/data-model-schema.md#assets).
- Asset downloads queue *after* the detail page fetch, under the same shared rate budget — not a parallel unthrottled burst.
- A per-project cap on asset count is recommended to bound worst-case request volume.

## Notification grouping

Desktop-only. Batches near-simultaneous new-project discoveries into one summary toast ("There are `<N>` new projects, check them here") instead of N separate popups.

- `notification_grouping_enabled` — default: **false**. When off, every new project gets its own detailed toast.
- `notification_grouping_mode` — one of:
  - `end_of_minute` — accumulate everything discovered for the remainder of the current clock minute, flush at the minute boundary.
  - `after_minutes` — accumulate for a fixed rolling window of `N` minutes from the *first* discovery in the batch, then flush.
  - `after_count` — accumulate until `N` pending items are reached, flush immediately.
- `notification_grouping_param` — the numeric parameter for `after_minutes` or `after_count` (unused for `end_of_minute`).
- **Single-item rule:** if exactly 1 project is pending when a batch would flush, it falls back to a normal individual detailed toast rather than a "1 new project" grouped message.
- Clicking a grouped toast opens the window filtered to `is_read = false` — no separate "batch ID" concept is needed. See [ui-ux-design.md](ui-ux-design.md#unreadread-highlighting).

## Push notifications (v3)

Deferred — full detail in [roadmap-future.md](../../v2/product/roadmap-future.md#push-notifications).

- `push_notifications_enabled` — default: **true**, but inert until a mobile device has been paired at least once.

## Full settings table

| Setting | Default | Scope |
|---|---|---|
| `poll_interval_seconds` | 30 | v1 |
| `max_requests_per_minute` | 2 | v1 |
| `max_concurrent_detail_fetches` | 2–3 | v1 |
| `safe_requests` | true | v1 |
| `query_params` | *(empty)* | v2 |
| `include_assets` | false | v2 |
| `notification_grouping_enabled` | false | v2 |
| `notification_grouping_mode` | — | v2 |
| `notification_grouping_param` | — | v2 |
| `push_notifications_enabled` | true (inert until paired) | v3 |

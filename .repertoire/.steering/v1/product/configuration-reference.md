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

**Storage** is where the value persists: `system` = the flat container (one value shared by every
platform), `per-platform` = inside that platform's own container. **Since** is the version that
introduced the key — the old single "Scope" column meant that, and reading it as storage is what
allowed a row to advertise "على كل المنصات" while its value lived in one platform's container.

The executable authority is `Features/Settings/SettingsCatalog.Builtin.cs` (one
`SettingsDescriptor` per row: shape, scope, bounds, Arabic copy, ids, visibility). This table is the
readable view of it, and `TestConfigurationReferenceMatchesCatalog` fails when the two disagree or
when a catalog row is missing from this table — a stale paragraph is a red test here, not a rumour.

| Key | Arabic row | Default | Storage | Since |
|---|---|---|---|---|
| `settings_disabled_platforms` | المنصات (per-platform switches) | *(empty — every platform on)* | system | dev57 |
| `settings_poll_interval_seconds` | فاصل الفحص | 30 (floored per site by `MinSafePollIntervalSeconds`) | system | v1 |
| `settings_safe_requests` | الطلبات الآمنة | true | system | v1 |
| `settings_max_concurrent_detail_fetches` | تحميل التفاصيل المتوازي | 2 | system | v1 |
| `settings_notification_grouping_enabled` | تجميع التنبيهات | true | system | v2 |
| `settings_grouping_mode` | نمط التجميع | `EndOfMinute` | system | v2 |
| `settings_grouping_threshold` | حد التجميع | 5 | system | v2 |
| `settings_startup_enabled` | تشغيل مع النظام | false (the registry is the truth the row reads) | system | v1 |
| `settings_remember_last_source` | تذكر آخر منصة مفتوحة | true | system | v3 |
| `settings_is_dark_mode` | الوضع الداكن | false | system | v3 |
| `close_behavior_action` | عند الضغط على X | `Ask` | system | v3 |
| `close_behavior_remembered` | تذكر خياري | false | system | v3 |
| `settings_is_polling_active` | *(Start/Pause state, no row)* | false | system | v1 |
| `mostaqlk-source` | *(projected source, no row)* | default source | system | v3 |
| `settings_max_requests_per_minute` | حد الطلبات | mostaql 2 · nafezly 8 | per-platform | v1 |
| `settings_query_params` | معاملات البحث | *(empty)* | per-platform | v2 |
| `settings_include_assets` | تحميل مرفقات المشاريع | false | per-platform | v2 |
| `settings_enable_source_notifications` | تنبيهات هذه المنصة | true | per-platform | v3 |
| `nafezly_enrichment_retry_enabled` | إعادة محاولة الإثراء التلقائي | true | per-platform | v3 |

## Who owns a setting's scope

The rules below exist so adding a platform never re-opens these questions.

1. **System (flat container)** — anything whose meaning is "how the app behaves", or whose consumer is
   a single app-wide object: scan interval, safe-request spacing, parallel detail fetches, notification
   grouping (enable / mode / threshold — `NotificationGrouper` is one singleton for the whole app),
   start-with-system, remember-last-platform, theme, close behavior, and the set of enabled platforms.
2. **Per-platform (namespaced container)** — anything that is a property of one site: `max_requests_per_minute`
   (each pipeline owns a private `TokenBucketRateLimiter`, so a budget spent on mostaql.com says nothing
   about nafezly.com — there is no shared budget and no aggregate to protect), extra query params,
   attachment download, that platform's notifications, its session cookie, and every read-only
   capability statement (currency, supervised types, enrichment session).
3. **A system value may still be floored by a platform's own safety minimum.** That is a capability
   fact, so it lives in the module (`ISourceDescriptor.MinSafePollIntervalSeconds`: default 10, nafezly
   60 because Cloudflare answers a burst with 429/1015) and not in the settings row. The clamp is
   `SourceCadence.ClampPollInterval`, used by pipeline start-up, the live settings apply, and the feed
   header caption — the header must never promise a cadence the pipeline refuses to run at.
4. **Disabling a platform is network silence, not deletion.** Its projects, routes, deep links,
   filters, archive, unread badge and notification history all keep working; polling (including a
   forced "check now"), enrichment dequeue and toasts stop; already-queued items wait and resume on
   re-enable. At least one platform must stay enabled — the switch that would break that renders
   locked **with its reason**, never as a rejected click. A new source needs no code for any of this:
   it is enabled by default and appears in المنصات on its own, because the row enumerates
   `SourceRegistry`.
5. **A source may override its own configuration, fully or partially, without shared code being
   touched** — `ISourceSettingsContribution.Adds` (new rows; module-local keys stay module-local and
   never enter `SettingsKeys`) and `.Overrides` (any single `SettingsDescriptorPatch` field: copy,
   bounds, default, options, icon, visibility, section, order, ids; a whole descriptor for an existing
   key is a full replace). What a module may **not** override is the user's stored value: a module sets
   the shape and the default, the user owns the value.
6. **Checklist for adding a platform** — register the descriptor + capabilities; contribute only rows
   that are genuinely yours, with a `SectionHeader` whose `Order` is your section's rank (system 0,
   platform 10, your section 12…, actions 90 — a header's `Order` ranks its band and always renders
   first inside it); leave every system row alone; add your module-local keys to no shared table; and
   put nothing about your id in `SettingsCatalog.Builtin.cs`. If a row you want already exists, patch
   it.

## Moving a setting between scopes

`ISettingsRepository` resolves a per-platform read as *namespaced container → legacy flat key →
default*, so a flat→container move needs no migration. **The opposite direction has no
fall-through**: `GetGlobal` only ever looks at the flat container, so a row that becomes `system`
would answer with its default while the user's value sat unread in a platform container.

`SettingsScopeHarvest` closes that gap once: it never overwrites a value that is already global,
otherwise takes the active platform's stored value, then any other platform that has one, and stamps
`settings_scope_harvest_v1` so the decision is never re-made. Losing containers stay on disk
untouched — nothing prunes them, and deleting a user's stored values is nobody's business here. The
move this mechanism served (dev53–dev54): scan interval, safe requests, parallel fetches, and the
three grouping settings went per-platform → system.

## Behavior notes worth not re-discovering

- `settings_enable_source_notifications` **is** read now (`NotificationDispatcher`, via
  `NotificationMute`). It persisted for many builds with nothing consuming it, so switching it off
  changed a number on disk and every toast still arrived. A platform's toasts are silenced while its
  badge and history keep counting.
- The startup row reads the OS (`IStartupService.IsStartupEnabled` → `HKCU\…\CurrentVersion\Run`),
  not the stored bool, because the registry can be edited outside the app.
- Grouping settings are applied when the grouper singleton is constructed, not when the settings page
  is first opened; a launch that never visits Settings still obeys what is stored.

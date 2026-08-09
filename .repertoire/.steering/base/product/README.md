# Mostaqlk

A Windows desktop app that watches [mostaql.com/projects](https://mostaql.com/projects) — the open-projects feed of Mostaql — and alerts the user within roughly a minute of a new project being posted, storing full project details locally for offline, searchable, permanent reference.

This is a wiki-style documentation set. Each concern has one home; other docs link to it rather than repeating it.

## Documents

| Doc | What it answers |
|---|---|
| [`MVP.md`](../../v1/product/README.md) | **Start here for build.** What actually ships first — parser/pipeline + storage + notifications only |
| [`diff-engine.md`](../../v1/tech/diff-engine.md) | The reusable compare abstraction — local scrape-vs-DB today, mobile peer-sync later |
| [`concurrency-model.md`](../../v1/tech/concurrency-model.md) | Thread-safe in-flight tracking, transaction boundaries, crash/restart behavior |
| [`worker-pool-and-rate-limiter.md`](../../v1/tech/worker-pool-and-rate-limiter.md) | Queue, worker pool, shared token-bucket rate limiter — with C# implementation |
| [`error-handling-and-resilience.md`](../../v1/tech/error-handling-and-resilience.md) | Failure handling per pipeline stage, retry/backoff policy |
| [`overview.md`](overview.md) | What is this, why does it exist, what ships in each version (MVP / v2 / v3) |
| [`architecture-pipeline.md`](architecture-pipeline.md) | How polling, the discovery queue, worker pool, and rate limiting actually work — including the concurrency race condition and its fix |
| [`data-model-schema.md`](data-model-schema.md) | The full embedded-DB schema: projects, owners, skills, assets, search index |
| [`configuration-reference.md`](../../v1/product/configuration-reference.md) | Every user-facing setting, its default, and its effect |
| [`ui-ux-design.md`](../../v1/product/ui-ux-design.md) | Window layout, tray behavior, unread highlighting, toast design |
| [`DESIGN.md`](DESIGN.md) | Visual design system — colors, light/dark theme, RTL, typography, icons, component base |
| [`search-and-filtering.md`](../../v2/product/search-and-filtering.md) | The dynamic query builder, sort options, fuzzy Arabic/English search, and the storage-engine decision behind it |
| [`roadmap-future.md`](../../v2/product/roadmap-future.md) | v3 stretch goals: mobile companion, LAN peer sync, FCM/APNs push |

## Quick facts

- **Platform:** Windows desktop app, tray-resident. Stack is likely C#/.NET MAUI for cross-platform reach (supersedes earlier Tauri/Rust references in [architecture-pipeline.md](architecture-pipeline.md) — read that doc's code-adjacent details as conceptual; [worker-pool-and-rate-limiter.md](../../v1/tech/worker-pool-and-rate-limiter.md) and [concurrency-model.md](../../v1/tech/concurrency-model.md) reflect the current C# direction)
- **Storage:** embedded single-file DB (SQLite or SQLite-compatible), no server, no cloud dependency in MVP/v2
- **Request budget:** configurable, default ~2 requests/minute against mostaql.com
- **Data policy:** [store-and-forget](architecture-pipeline.md#no-update-policy) — a project is scraped once and never re-fetched or updated
- **Language support:** Arabic-first (source site is Arabic), with English handled equally in [search](../../v2/product/search-and-filtering.md)

## Version scope at a glance

- **v1 (MVP):** poll → discover → enrich → store → notify → tray + window. See [overview.md § MVP](overview.md#v1-mvp-scope)
- **v2:** `query_params` override, `include_assets`, notification grouping, unread highlighting, full query builder + search. See [overview.md § v2](overview.md#v2-scope)
- **v3 (stretch):** mobile companion app, LAN pairing, two-way sync, push notifications. See [roadmap-future.md](../../v2/product/roadmap-future.md)

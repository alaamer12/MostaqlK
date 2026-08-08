# Overview

[← Back to wiki home](./README.md)

## Table of contents
- [Problem](#problem)
- [Product statement](#product-statement)
- [Core idea](#core-idea)
- [v1 (MVP) scope](#v1-mvp-scope)
- [v2 scope](#v2-scope)
- [v3 scope](#v3-scope)
- [Out of scope / explicit non-goals](#out-of-scope--explicit-non-goals)

## Problem

Freelancers on Mostaql compete partly on speed — being among the first to submit a proposal on a newly posted project meaningfully improves response rate. Mostaql has no public API and no push notifications for new listings. Manually refreshing the page is unreliable and wastes attention.

## Product statement

**Mostaqlk** is a lightweight Windows tray/desktop application that polls the Mostaql open-projects feed on a configurable interval, detects newly posted projects via delta comparison against a local database, fetches and stores their full details, and notifies the user — while respecting a strict, configurable outbound request budget so it never behaves like a scraper hammering the site.

It requires no backend, no account, and no hosting. Everything — scheduler, HTTP client, parser, database, notifier, tray icon, window — runs inside a single local process.

## Core idea

Two request tiers, sharing one rate budget:

1. **Listing poll** — periodically fetch the projects listing page, extract each project's ID and summary fields, diff against what's already known.
2. **Detail enrichment** — for every genuinely new project ID, fetch its detail page and store the full record (description, budget, delivery time, skills, owner profile, and optionally attachments).

Full mechanics: [architecture-pipeline.md](./architecture-pipeline.md).

## v1 (MVP) scope

- Listing poll with configurable interval
- Delta detection against local DB (see [in-flight tracking](./architecture-pipeline.md#in-flight-tracking))
- Detail enrichment via a bounded worker pool, shared rate budget
- Local embedded-DB persistence — see [data-model-schema.md](./data-model-schema.md)
- Tray icon with status state (idle / polling / error)
- Native toast notification per new project
- Minimal window: project feed, basic settings

There is no separate "cold start" or "backfill" mode — a five-day-old app relaunching and finding 20 new projects is handled by the exact same queue/worker mechanism as a normal 1-item poll. See [architecture-pipeline.md § backlog handling](./architecture-pipeline.md#backlog-handling-no-special-cold-start).

## v2 scope

- `query_params` override — point the listing poll at a specific Mostaql filter URL (category, sort, budget, etc.) going forward only, non-retroactive. See [configuration-reference.md](./configuration-reference.md#query_params)
- `include_assets` — optionally download and store project attachments/images during enrichment
- Notification grouping (desktop-side batching of near-simultaneous discoveries into one summary toast)
- Read/unread highlighting on the projects feed
- Full dynamic query builder: filter and sort by any structured field
- Fuzzy Arabic + English full-text search

Details: [configuration-reference.md](./configuration-reference.md), [search-and-filtering.md](./search-and-filtering.md), [ui-ux-design.md](./ui-ux-design.md).

## v3 scope

A mobile companion app, LAN-based pairing and two-way sync, and push notifications for when the mobile app is fully closed. This is explicitly deferred — it requires a second full application and, for the push sub-feature, a small dependency on FCM/APNs. See [roadmap-future.md](./roadmap-future.md).

## Out of scope / explicit non-goals

- **No project status tracking.** Once stored, a project is never re-fetched or updated (open→closed transitions are not recorded). See [no-update policy](./architecture-pipeline.md#no-update-policy).
- **No account system, no cloud backend** for v1/v2. Everything is local-first.
- **No bidding/proposal automation** — this is a discovery and archival tool, not an auto-apply bot.
- **No retroactive re-classification** when `query_params` changes — only future polls are affected.

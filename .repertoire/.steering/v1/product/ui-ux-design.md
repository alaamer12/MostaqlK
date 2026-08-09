# UI / UX design

[← Back to wiki home](./README.md)

## Table of contents
- [Design philosophy](#design-philosophy)
- [Tray icon](#tray-icon)
- [Main window layout](#main-window-layout)
- [Unread/read highlighting](#unreadread-highlighting)
- [Toast notifications](#toast-notifications)
- [Notification grouping (UX side)](#notification-grouping-ux-side)

## Design philosophy

Flat, card-based, minimal chrome — status and signal (new vs. seen, enriched vs. pending) communicated primarily through subtle color and weight, not decoration. The window is a working tool checked frequently, not a marketing surface.

Colors, theming, RTL, typography, and component base are specified separately in [`DESIGN.md`](../../base/product/DESIGN.md) — this doc covers layout and interaction, that one covers the visual tokens.

## Tray icon

- Always present. Icon state reflects: idle, polling, backlog draining, error.
- Right-click menu: Pause polling, Open window, Quit.
- Closing the main window hides it; it does **not** stop the backend or quit the app. Only "Quit" from the tray menu exits the process.

## Main window layout

Top to bottom:

1. **Status bar** — current mode (e.g. "Live — polling every 30s"), live rate-budget indicator (e.g. "12 req/min budget"), settings shortcut.
2. **Active query indicator** — shows the current [`query_params`](configuration-reference.md#query_params) target (or "All projects" if unset), with a quick way to edit it.
3. **Project feed** — reverse-chronological cards. Each card: title, owner name, time posted, proposal count, category, enrichment status badge (`enriched` / `pending` / `failed`).
4. **Footer** — running totals ("147 projects tracked · last poll 12s ago"), notification toggle indicator.

Supports the full [dynamic query builder](../../v2/product/search-and-filtering.md#query-builder-ux) for filtering/sorting the feed, and the [search box](../../v2/product/search-and-filtering.md) for fuzzy title/description lookup.

## Unread/read highlighting

- `projects.is_read` (default `false`) drives visual distinction on the feed, independent from `enrichment_status` — a fully enriched project can still be unread; they're different signals.
- **Unread:** bolder title weight, filled accent-colored left edge on the card, stands out clearly when scanning.
- **Read** (flips to `true` when the user opens the project in-app or in-browser): muted/gray left edge, normal weight.
- A persistent "N unread" counter lives in the footer alongside the tracked-project total.
- "Mark all as read" action available.
- `unread only` is available as a standard filter chip in the [query builder](../../v2/product/search-and-filtering.md#query-builder-ux), since `is_read` is just another boolean column in the same filter system.

## Toast notifications

Native Windows toast per new project (when grouping is off, or for single-item batches — see [configuration-reference.md](configuration-reference.md#notification-grouping)):

- Title, owner name, time posted, proposal count, category, budget (if enrichment completed in time).
- Click action: opens the window scrolled/filtered to that project.

## Notification grouping (UX side)

When grouping is enabled and a batch flushes with 2+ items:

- Toast reads: **"There are `<N>` new projects — check them here."**
- Click action opens the window pre-filtered to `is_read = false`, reusing the same unread mechanism described above rather than a separate batch concept.
- Mechanics (trigger modes, thresholds) are configuration, not UI — see [configuration-reference.md § notification grouping](configuration-reference.md#notification-grouping).

## Tray icon (system tray) (Windows Only)
For an MVP that is essentially a background monitor + notifier, the tray should stay minimal and non-noisy. Proposed default set:
Tray icon states

Idle / monitoring active (normal brand icon)
New projects available (badge or subtle accent color)
Error / paused (distinct state so the user notices without a notification storm)

Right-click / long-press menu (desktop)

Open main window
Pause / Resume monitoring
Check now (force a poll)
Recent notifications (last 5–10, clickable → open project)
Preferences / Settings
Quit

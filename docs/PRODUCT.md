# Product

## What this is

MostaqlK is a Windows desktop, tray-resident monitoring tool for Arabic freelance platforms. It polls project feeds, enriches each project's details into a local SQLite archive, and notifies the user the moment something new matches their query. Since v3 it watches **multiple sources**: مستقل (Mostaql) and نفذلي (Nafezly), with more sources planned as modules, each potentially carrying its own logic beyond the raw feed (per-source filters, scoring/ML matching, future tooling).

## Who uses it

Arabic-speaking freelancers and agencies (Egypt + Gulf) who compete on proposal speed. The app is checked in short glances many times a day, not sat in. It is a working tool: dense, calm, scannable. RTL Arabic UI, light/dark parity.

## Job to be done

"Tell me, faster than anyone, that a project I could win just appeared — on any platform I watch — and let me open it and bid."

## Surfaces

- **Projects** — per-source tabbed feed of tracked projects (unread/read states, enrichment status, per-source metadata shapes).
- **Project details** — one enriched project; the field grid adapts to the source's schema.
- **Advanced search** — query builder over the local archive.
- **Settings** — global preferences + per-source management (enable/disable, session state, poll cadence, rate budget, notifications), folded in rather than a separate page (user decision, v3).
- **Tray** — always-present; states idle / polling / backlog / error.

## Constraints

- .NET MAUI app; mockups are Tailwind HTML references, RTL, Tajawal, FontAwesome glyph icons.
- Design system: `.repertoire/.steering/base/product/DESIGN.md` — slate neutrals, Mostaql blue `#2386C8` (light) / `#5CA8DE` (dark), positive green; unread cards get a 4px inline-start accent edge; cards are flat with 12–16px radius.
- Source brand tokens (v3): Mostaql `#2386C8`; Nafezly `#FFA200` (their real secondary brand color — their primary `#0194fe` is too close to Mostaql blue to distinguish in a feed).
- No auth screens exist; per-source "session" means an optional imported cookie for enrichment only.
- All counts, budgets, titles in mockups are illustrative design data, not claims.

# Mostaqlk

> A lightweight, local-first Windows desktop app that monitors Mostaql's open projects feed, provides sub-minute alerts for new postings, and maintains a fully searchable offline archive.
> 

---

## 📌 Overview

Freelancers on Mostaql rely heavily on response speed to secure projects. **Mostaqlk** eliminates manual page refreshes by polling the open projects feed on a configurable schedule, parsing project details, and triggering native desktop notifications—all while adhering to a strict, configurable outbound request budget.

The application operates with zero cloud dependencies or external backends. All components—from HTTP scheduling and HTML parsing to local storage and UI—run locally inside a single process.

---

## 🚀 Key Features

* **Real-time Monitoring & Alerts:** Discovers new project listings and delivers native Windows desktop toasts.


* **Offline Local Archive:** Stores project descriptions, owner details, budgets, delivery timelines, and skills locally in an embedded database.


* **Store-and-Forget Policy:** Projects are scraped once upon discovery and stored permanently, avoiding unnecessary network traffic.


* **Polite Request Budgeting:** Features a shared token-bucket rate limiter (~2 requests/minute by default) to keep requests lightweight and respectful.


* **Arabic-First Design:** Full RTL layout support, Unicode bidi isolation for mixed text, and Arabic/English fuzzy search capabilities.


* **Tray-Resident:** Lives unobtrusively in your system tray with clear visual status indicators (idle, polling, error).



---

## 🏗 System Architecture

Mostaqlk operates through a two-tier request model driven by a shared rate budget:

```
[ Mostaql.com ] 
      │
      ▼
1. Listing Poll (Fetches summary & project IDs)
      │
      ▼
2. Delta Engine (Diffs discovered IDs against local DB)
      │
      ▼
3. Worker Pool (Enriches NEW projects with full details)
      │
      ├──► [ Embedded Database ] (SQLite/Local FTS Index)
      └──► [ Native System Toast ] (Windows Notification)
```
[cite: 1, 2, 4]

### Tech Stack

* **Platform:** Windows Desktop (C# / .NET MAUI)[cite: 1]
* **Database:** Single-file embedded database (SQLite or libSQL with FTS5 search)[cite: 1, 2]
* **UI Framework:** shadcn/ui primitives with Tailwind CSS, custom RTL layouting, and Tabler outline icons[cite: 3]
* **Typography:** Lyra El-Mesry (Arabic content) paired with a clean grotesque font for Latin/numerals[cite: 3]

---

## 🛠 Scope & Version Roadmap

| Feature / Capability | v1 (MVP) | v2 | v3 (Future) |
|---|:---:|:---:|:---:|
| Periodic listing polling & delta detection[cite: 4] |  |  |  |
| Detail enrichment (Worker pool & rate limiter)[cite: 4] |  |  |  |
| Local SQLite storage & system tray integration[cite: 4] |  |  |  |
| Native toast notifications[cite: 4] |  |  |  |
| `query_params` custom feed filters[cite: 4] | ❌ |  |  |
| Attachment/Asset local downloading (`include_assets`)[cite: 2, 4] | ❌ |  |  |
| Notification grouping & Feed read/unread states[cite: 3, 4] | ❌ |  |  |
| Advanced query builder & Arabic/English FTS5 search[cite: 2, 4] | ❌ |  |  |
| Mobile companion app & LAN peer-to-peer sync[cite: 1, 4] | ❌ | ❌ |  |

---

## 🗄 Data Model Overview

Data is organized across four primary local structures[cite: 2]:

* **`projects`**: Primary storage containing snapshot details (budget, timeframe, proposal count, timestamps, read state)[cite: 2].
* **`owners`**: Shared profile references (display name, hire rate, project history) deduplicated across listings[cite: 2].
* **`project_skills`**: Dynamic skill mappings per project[cite: 2].
* **`assets`**: Optional local file references saved to disk when attachment fetching is active[cite: 2].
* **Search Index:** An FTS5 virtual table indexing title, description, and skill strings for instant search[cite: 2].

---

## 📖 Project Documentation

For detailed architecture decisions, design tokens, and technical specifications, refer to the documentation set[cite: 1]:

* [`system-components.md`](system-components.md) – High-level architecture map[cite: 1].
* [`MVP.md`](v1/product/README.md) – Scope and technical checklist for initial build[cite: 1].
* [`architecture-pipeline.md`](architecture-pipeline.md) – Polling mechanics, concurrency bounds, and queue mechanics[cite: 1].
* [`data-model-schema.md`](data-model-schema.md) – Embedded database schema specifications[cite: 1].
* [`DESIGN.md`](DESIGN.md) – Visual tokens, color palettes, RTL handling, and component base[cite: 1].
* [`roadmap-future.md`](v2/product/roadmap-future.md) – v3 mobileCompanion and LAN sync goals[cite: 1].

```
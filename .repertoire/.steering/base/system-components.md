# System Components — MostaqlK

> **Version scope:** Base (applies to all versions) + V1 implementation specifics.
> **Read alongside:** [`architecture-pipeline.md`](./product/architecture-pipeline.md) for the runtime flow · [`base/tech/`](./tech/README.md) for implementation conventions.

---

## System Overview

MostaqlK is a single-process, local-first MAUI desktop application that monitors the Mostaql freelance platform for new projects and notifies the user in real time. There is no cloud backend, no user account, and no external service dependency beyond the Mostaql website itself.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          MostaqlK (Single Process)                          │
│                                                                             │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │                       PIPELINE SUBSYSTEM                            │    │
│  │                                                                     │    │
│  │   ┌──────────┐   ┌──────────────┐   ┌──────────┐   ┌───────────┐  │    │
│  │   │  Poll    │→  │  Diff Engine │→  │ Discovery│→  │  Worker   │  │    │
│  │   │  Service │   │              │   │  Queue   │   │   Pool    │  │    │
│  │   └──────────┘   └──────────────┘   └──────────┘   └─────┬─────┘  │    │
│  │        ↑                 ↑                                │        │    │
│  │        │         ┌───────┴────────┐              ┌────────▼──────┐ │    │
│  │        │         │  In-Flight     │              │  Enrichment   │ │    │
│  │   ┌────┴──────┐  │  Tracker       │              │  Service      │ │    │
│  │   │  Rate     │  └────────────────┘              └────────┬──────┘ │    │
│  │   │  Limiter  │◄─────────────────────────────────────────┘        │    │
│  │   └───────────┘                                                     │    │
│  └────────────────────────────────────────────────────────────────────┘    │
│                                     │                                       │
│                          ┌──────────▼────────┐                             │
│                          │   Storage Engine   │                             │
│                          │  (SQLite + FTS5)   │                             │
│                          └──────────┬─────────┘                            │
│                                     │                                       │
│  ┌────────────────────────┐   ┌─────▼──────────────────────────────────┐   │
│  │  Notification System   │   │          UI System (MAUI)              │   │
│  │  (Windows Toasts)      │   │  Tray Icon · Main Window · MVVM        │   │
│  └────────────────────────┘   └────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Component Hierarchy

```
MostaqlK
│
├── Pipeline Subsystem
│   ├── Poll Service
│   ├── Diff Engine
│   │   ├── Committed State Provider (SQLite)
│   │   └── In-Flight State Provider
│   ├── In-Flight Tracker
│   ├── Discovery Queue
│   ├── Worker Pool
│   │   └── Enrichment Service
│   │       ├── HTTP Scraper
│   │       └── HTML Parser
│   └── Rate Limiter (Token Bucket)
│
├── Storage Engine
│   ├── Database (SQLite)
│   ├── Repository Layer
│   └── Search Index (FTS5)
│
├── Notification System
│   ├── Toast Dispatcher
│   └── Notification Grouper
│
├── UI System
│   ├── Tray Icon
│   ├── Main Window
│   │   ├── Project Feed
│   │   ├── Status Bar
│   │   └── Settings Panel
│   ├── Design System
│   │   ├── Skeleton Loading (ShimmerBox)
│   │   ├── Text Display (TruncatingLabel, LabelWithSubText)
│   │   └── Icon System (Three Tiers)
│   └── MVVM Architecture
│       ├── ViewModels
│       ├── Views
│       └── Models
│
└── Error System
    ├── Domain Error (DomainError / Result<T>)
    └── Module Error Files (Errors.cs per module)
```

---

## 1. Pipeline Subsystem

### Type
Subsystem

### Purpose
Orchestrates the two-tier project discovery and enrichment flow. Continuously monitors Mostaql for new projects and drives each newly found project through fetching, parsing, and persistence — without duplicates, within the configured rate budget, and with full isolation between individual project failures.

### Responsibilities
- Run a periodic listing poll on a configurable interval
- Diff poll results against known state to find genuinely new project IDs
- Enqueue new IDs for enrichment in FIFO order
- Drain the queue through a bounded worker pool
- Apply a shared rate budget across all outbound HTTP requests
- Guarantee no project ID is processed twice (in-flight tracking + DB backstop)
- Guarantee no ID is permanently stuck in-flight on any failure path

### In Scope
- Scheduling and driving all outbound HTTP requests
- Coordinating the poll → diff → enqueue → enrich → commit → notify lifecycle
- Crash recovery (stateless in-memory; naturally resumes via diff on next poll)

### Out of Scope
- Making HTTP requests directly (delegated to HTTP Scraper)
- Parsing HTML (delegated to HTML Parser)
- Writing to the database (delegated to Repository Layer)
- Sending notifications (delegated to Notification System)

### Related Components
- [Poll Service](#2-poll-service) — Tier 1 entry point
- [Diff Engine](#3-diff-engine) — decides what is actionable
- [Worker Pool](#6-worker-pool) — Tier 2 processing
- [Rate Limiter](#7-rate-limiter-token-bucket) — shared budget enforcement

---

## 2. Poll Service

### Type
Service

### Purpose
The entry point of the pipeline. Fires on a configurable interval, fetches the Mostaql project listing page, and hands the parsed project IDs to the Diff Engine to determine what is new.

### Responsibilities
- Maintain a timer that triggers at `poll_interval_seconds`
- Acquire a rate-limiter token before each listing fetch
- Invoke the HTTP Scraper to fetch the listing page
- Invoke the HTML Parser to extract project ID and summary fields from the listing
- Pass the candidate ID set to the Diff Engine
- Hand the `unseen` set to the In-Flight Tracker and Discovery Queue
- Update tray icon state (polling, error, idle)

### Features
- Configurable interval (default 30 seconds)
- Single active polling target (base URL or `query_params`-modified URL)
- Continues firing on schedule regardless of queue depth — never blocks waiting for workers

### Inputs
- Timer tick
- Configuration: `poll_interval_seconds`, `query_params`

### Outputs
- Candidate ID set (to Diff Engine)
- Tray state signal

### Error Handling
- A failed poll (network error, parse error) produces no candidate set — does not corrupt in-flight state or queue
- Error logged; tray reflects error state
- Next scheduled poll retries automatically (no special retry-faster logic)
- A listing parse failure that returns zero projects triggers a schema-sanity alarm (silently-empty parse is treated as a failure, not as "no new projects")

### Configuration
- `poll_interval_seconds` (default: 30)
- `max_requests_per_minute` (shared with workers)
- `query_params` (optional filter string)

### Internal Entities
- `IPollService` / `PollService`
- `PollErrors.cs` (Result contract errors)

### Implementation
`MostaqlK.Services.Pipeline/`

---

## 3. Diff Engine

### Type
Engine

### Purpose
Determines which candidate project IDs are genuinely new (not already committed or in-flight) using a three-state model. Designed as a pure set-logic component with pluggable state providers, making it reusable for both local polling (V1) and future peer-sync (V3).

### Responsibilities
- Receive a candidate ID set from the Poll Service
- Query each registered `IKnownStateProvider` for IDs already known
- Compute the `unseen` subset: candidates not in any provider's known set
- Return categorized sets: `unseen`, `in_flight`, `committed`

### Features
- Provider-agnostic: works with any combination of `IKnownStateProvider` implementations
- Pure set logic — no I/O of its own
- V1: two providers (SQLite committed, in-memory in-flight)
- V3 (future): peer manifest provider wired in without changing engine logic

### Inputs
- `candidates: IReadOnlySet<long>` — project IDs from the current poll
- `providers: IEnumerable<IKnownStateProvider>` — state sources

### Outputs
- `DiffResult { unseen, known }` — the `unseen` set is what gets enqueued

### In Scope
- Set subtraction across multiple state providers
- Classifying IDs into `unseen` / `in_flight` / `committed`

### Out of Scope
- Fetching, parsing, or enriching projects
- Writing to the DB or modifying in-flight state (those happen in response to the output)
- Conflict resolution or field-level merging (the no-update policy means ID presence/absence is the only comparison needed)

### Subcomponents
- **Committed State Provider** (`SqliteCommittedProvider`) — queries `projects.project_id` from SQLite
- **In-Flight State Provider** (`InFlightSetProvider`) — snapshots the `InFlightTracker`

### Internal Entities
- `DiffEngine`, `DiffResult`
- `IKnownStateProvider`
- `SqliteCommittedProvider`
- `InFlightSetProvider`

### Implementation
`MostaqlK.Services.Pipeline/DiffEngine/`

---

## 4. In-Flight Tracker

### Type
Infrastructure

### Purpose
Tracks the third state beyond "in DB" and "not in DB": IDs that have been enqueued but not yet committed. Prevents duplicate enrichment when a new poll fires while workers are still mid-enrichment.

### Responsibilities
- Atomically mark a project ID as in-flight (returns `false` if already present — natural race guard)
- Remove an ID from the set when enrichment finishes (success or permanent failure)
- Expose a snapshot of current IDs for the Diff Engine's In-Flight State Provider

### Features
- Lock-free concurrent access via `ConcurrentDictionary<long, byte>` used as a set
- `TryMarkInFlight` is an atomic test-and-set — two simultaneous polls cannot both enqueue the same ID
- `MarkComplete` is always called from `finally` — no ID can get stuck permanently in-flight

### Inputs
- `TryMarkInFlight(projectId)` — called by Poll Service before enqueueing
- `MarkComplete(projectId)` — called by Worker Pool after commit or permanent failure

### Outputs
- `bool` from `TryMarkInFlight` (false = already tracked; drop this ID)
- `HashSet<long>` snapshot from `Snapshot()` (consumed by Diff Engine)

### Constraints
- In-memory only, by design — does not persist across process restarts
- On restart, an empty set is the correct starting state (uncommitted IDs are re-discovered by the next poll's diff)

### Error Handling
- `MarkComplete` must be called from a `finally` block regardless of enrichment success or failure — this is a hard rule, not a best practice

### Related Components
- [Diff Engine](#3-diff-engine) — reads snapshot via `InFlightSetProvider`
- [Worker Pool](#6-worker-pool) — calls `MarkComplete` in `finally`

### Internal Entities
- `InFlightTracker`

### Implementation
`MostaqlK.Services.Pipeline/InFlightTracker.cs`

---

## 5. Discovery Queue

### Type
Infrastructure

### Purpose
A FIFO buffer between the Poll Service (producer) and the Worker Pool (consumers). Decouples the poll rhythm from the enrichment rate and provides the backlog-fairness guarantee: older discoveries are always processed before newer arrivals.

### Responsibilities
- Accept new project IDs from the Poll Service
- Deliver IDs to workers in FIFO order
- Grow unboundedly during large backlogs without stalling the poll loop

### Features
- Implemented via `System.Threading.Channels.Channel<long>` (async-friendly, thread-safe, lock-free)
- Unbounded (`CreateUnbounded<long>`) — the rate limiter provides backpressure on requests, not the queue
- FIFO ordering guarantees fairness across poll cycles (a later poll's arrivals never jump ahead of an existing backlog)

### Inputs
- `projectId` writes from Poll Service (via `channel.Writer.WriteAsync`)

### Outputs
- `projectId` reads to workers (via `channel.Reader.ReadAllAsync`)

### Out of Scope
- Deduplication (handled by In-Flight Tracker before enqueue)
- Prioritization (FIFO only, by design)
- Persistence (in-memory; on restart, the queue is empty and the next poll rebuilds the backlog naturally)

### Related Components
- [Poll Service](#2-poll-service) — producer
- [Worker Pool](#6-worker-pool) — consumer

---

## 6. Worker Pool

### Type
Engine

### Purpose
A fixed pool of concurrent async consumers that drain the Discovery Queue, enrich each project (fetch + parse + commit), and trigger notifications. Enforces the concurrency cap and coordinates with the shared Rate Limiter on every request.

### Responsibilities
- Maintain `max_concurrent_detail_fetches` async worker tasks (default 2–3)
- Each worker: acquire rate-limiter token → call Enrichment Service → commit to DB → trigger notification → release ID from In-Flight Tracker
- Guarantee `InFlightTracker.MarkComplete` is always called via `finally`, even on exception
- Implement per-ID retry with exponential backoff on transient failures
- Mark permanently failed IDs as `enrichment_status = 'failed'` in the DB

### Features
- Bounded concurrency (configurable worker count)
- Per-ID failure isolation (one project failing never affects others in the queue)
- Exponential backoff retry (1m → 2m → 4m, capped at 15m, max 5 attempts)
- Retries consume the same shared rate budget as first attempts (no priority lane)
- Graceful shutdown: stops accepting new items and waits for in-progress work to finish

### Inputs
- `projectId` from Discovery Queue
- Rate-limiter token

### Outputs
- Committed project row (to Storage Engine)
- Notification trigger (to Notification System)
- `MarkComplete(id)` call (to In-Flight Tracker)

### Error Handling
- **Transient failures** (network error, timeout, HTTP 5xx): retry with backoff, up to max attempts
- **Permanent failures** (max attempts exhausted, unrecoverable parse error): write `enrichment_status = 'failed'` to DB; release ID from in-flight tracker
- Each worker task is isolated — an unhandled exception in one worker does not kill other workers

### Configuration
- `max_concurrent_detail_fetches` (default: 2–3)
- Retry: max 5 attempts, backoff 1m/2m/4m/8m/15m

### Internal Entities
- `WorkerPool` / `EnrichmentWorker`

### Implementation
`MostaqlK.Services.Pipeline/WorkerPool/`

---

## 7. Rate Limiter (Token Bucket)

### Type
Infrastructure

### Purpose
Enforces the aggregate outbound HTTP request budget across all callers (both the Poll Service and all Worker Pool tasks). Prevents the app from behaving like a scraper hammering the Mostaql site, and backs off gracefully when already under load.

### Responsibilities
- Maintain a token bucket refilling at `max_requests_per_minute / 60` tokens per second
- Block any caller (poll loop or worker) until a token is available
- Cap the bucket at `max_requests_per_minute` capacity (prevents burst-after-idle)

### Features
- Single shared instance across both tiers — rate is a true aggregate, not per-tier
- Token bucket algorithm: continuous refill, no time-window batching
- Capacity equals the per-minute rate (no inflated burst allowance)
- Internal `SemaphoreSlim` for thread safety

### Inputs
- `WaitForTokenAsync()` call from any HTTP-making component

### Outputs
- Returns once a token has been acquired (possibly after a wait delay)

### Constraints
- A 429 response from the server causes the affected request's worker to back off exponentially — the rate limiter is not the retry mechanism, but the two work together
- No caller bypasses this limiter — even retries go through it

### Configuration
- `max_requests_per_minute` (default: 2)

### Internal Entities
- `TokenBucketRateLimiter`

### Implementation
`MostaqlK.Services.Pipeline/TokenBucketRateLimiter.cs`

---

## 8. Enrichment Service

### Type
Service

### Purpose
Fetches and parses the full detail page for a single project, then assembles a complete `ProjectDetails` record ready for persistence. Bridges Infrastructure (HTTP + HTML) with the pipeline's Application layer.

### Responsibilities
- Fetch the project's detail page URL via HTTP Scraper
- Parse the raw HTML into structured fields via HTML Parser
- Return a `Result<ProjectDetails>` — never throws for expected failures
- Convert Infrastructure exceptions (`HttpRequestException`, `ParseException`) into `DomainError` via `HttpErrors.cs` factory

### Features
- Optionally downloads project asset files when `include_assets = true`
- Dual-strategy HTML parsing (structural class/id selectors + label-driven fallback) for resilience against Mostaql markup changes

### Inputs
- `projectId: long`
- `url: string`

### Outputs
- `Result<ProjectDetails>.Ok` on success
- `Result<ProjectDetails>.Err` with `DomainError` on any expected failure

### Error Handling
- Uses the **Result contract** — never throws for HTTP errors, parse failures, or timeouts
- Errors created exclusively via `HttpErrors.cs` factory (Code + InternalMessage + ExternalMessage + FixMessage)
- `OperationCanceledException` always propagates — not caught

### Subcomponents
- **HTTP Scraper** — sends the HTTP request, returns raw HTML
- **HTML Parser** — converts raw HTML to a typed `ProjectDetails` record

### Internal Entities
- `IEnrichmentService` / `EnrichmentService`
- `HttpErrors.cs`

### Implementation
`MostaqlK.Services.Pipeline/EnrichmentService.cs`

---

## 9. HTTP Scraper

### Type
Integration

### Purpose
The single point of contact between the application and the Mostaql website. Sends HTTP requests, manages headers and user-agent, applies timeout and retry at the transport level, and returns raw HTML responses.

### Responsibilities
- Send `GET` requests to Mostaql URLs
- Apply configured request headers and user-agent
- Enforce per-request timeout
- Surface all failures as typed exceptions to the Enrichment Service (which converts them to `DomainError`)

### Features
- `HttpClient` singleton (long-lived, avoids socket exhaustion)
- Timeout configuration
- No authentication — anonymous requests only

### Inputs
- URL string
- `CancellationToken`

### Outputs
- Raw HTML string on success
- `HttpRequestException` / `TaskCanceledException` on failure (converted to `Result.Err` by Enrichment Service)

### Out of Scope
- HTML parsing (delegated to HTML Parser)
- Rate limiting (enforced upstream by Rate Limiter before the HTTP Scraper is called)
- Retry logic (handled by Worker Pool's retry loop)

### Internal Entities
- `IProjectScraper` / `MostaqlScraper`
- `HttpErrors.cs`

### Implementation
`MostaqlK.Infrastructure.Http/`

---

## 10. HTML Parser

### Type
Processor

### Purpose
Converts raw Mostaql HTML pages into strongly typed C# records. Implements a dual-strategy extraction approach: structural (class/id selectors) as primary, label-driven (Arabic text matching) as fallback — making the parser resilient to partial Mostaql markup changes.

### Responsibilities
- Parse listing pages: extract `project_id`, `title`, `url`, `client_name`, `posted_relative`, `proposal_count` per card
- Parse detail pages: extract all `ProjectDetails` fields (description, budget, delivery days, skills, owner stats, attachments)
- Normalize raw strings into typed values (relative times → absolute datetime, Arabic numerals → int, budget strings → min/max floats)
- Report `ParseException` when critical fields cannot be extracted

### Features
- **Dual-strategy parsing:** structural selectors first; falls back to Arabic label-text proximity walk if a selector is absent (robust to class/id renames)
- **Identifier-blind fallback:** walks the DOM by adjacency (next sibling, next `<td>`, parent's next sibling) rather than by class name — survives a full Mostaql redesign of non-critical fields
- **Schema sanity check:** validates that listing parse produced at least one project with a numeric ID and a non-empty title before returning (empty = parser failure, not "no projects")
- Isolated per page type — listing parser and detail parser are separate classes, so a markup change only touches one file

### Inputs
- Raw HTML string

### Outputs
- `IReadOnlyList<ProjectSummary>` (listing parse)
- `ProjectDetails` (detail parse)
- `ParseException` on critical extraction failure

### Out of Scope
- HTTP requests
- Database writes
- Diff logic

### Internal Entities
- `IProjectScraper` (interface shared with HTTP Scraper)
- `ListingParser`, `DetailParser`

### Implementation
`MostaqlK.Infrastructure.Http/Parsers/`

---

## 11. Storage Engine

### Type
Data System

### Purpose
Provides durable, queryable persistence for all discovered projects, owner profiles, skills, and assets using a local embedded database. The single source of truth for the application's committed state.

### Responsibilities
- Store project rows write-once (`INSERT OR IGNORE` — the no-update policy)
- Store owner profiles with selective update (`last_seen_at` and stats are the one exception)
- Store many-to-many `project_skills` rows
- Store asset metadata when `include_assets` is enabled
- Maintain the FTS5 full-text search index incrementally in the same transaction as each project insert
- Expose project state for the Diff Engine's Committed State Provider
- Enforce `project_id` uniqueness as a DB-level backstop against in-flight tracking bugs

### Features
- Embedded SQLite (single `.db` file on disk, no server)
- FTS5 virtual table shadowing `title`, `description`, and concatenated skills
- Write-once project rows (no `updated_at`, no status history table, no revisit queue)
- All enrichment writes (project + skills + assets + FTS) in a single atomic transaction
- Schema versioning with migration support (`DatabaseSchemaException` on mismatch, halts startup)

### In Scope
- Schema creation, migration, and version checking
- All CRUD operations behind the Repository Layer abstraction
- FTS index maintenance

### Out of Scope
- Full-text search query execution (called by UI ViewModels; the Storage Engine just maintains the index and provides the query interface)
- Asset file storage (assets table stores paths; binary files are written to disk by the Enrichment Service)

### Subcomponents
- **Database** — SQLite file, schema migrations
- **Repository Layer** — typed C# repositories (`IProjectRepository`, `IOwnerRepository`, `IAssetRepository`)
- **Search Index** — FTS5 virtual table + query interface

### Error Handling
- Uses the **Throw contract** at the Repository layer — `SqliteException` propagates to the Application service, which converts it to `Result.Err` via `PipelineErrors.cs`
- Schema mismatch: throws `DatabaseSchemaException` at startup; application cannot start with an incompatible schema

### Internal Entities
- `IProjectRepository`, `ProjectRepository`
- `IOwnerRepository`, `OwnerRepository`
- `DatabaseErrors.cs`

### Implementation
`MostaqlK.Infrastructure.Database/`

---

## 12. Notification System

### Type
Service

### Purpose
Informs the user about newly discovered projects via native Windows toast notifications. Supports individual per-project toasts and configurable batching (notification grouping) for high-volume scenarios.

### Responsibilities
- Dispatch a Windows toast notification on each project commit (or per-batch flush)
- Apply notification grouping logic when `notification_grouping_enabled = true`
- Accumulate pending notifications according to the configured grouping mode (`end_of_minute`, `after_minutes`, `after_count`)
- Fall back to individual detailed toast when batch size is exactly 1

### Features
- Native Windows toast API (no custom notification framework)
- Individual toast: title, owner name, time posted, proposal count, category, budget (if enrichment completed in time)
- Click action: opens the main window scrolled/filtered to the project (or to `is_read = false` filter for grouped toasts)
- Three grouping trigger modes with configurable thresholds
- Single-item rule: 1-item batch always produces an individual detailed toast, not a grouped message

### Inputs
- Committed `ProjectDetails` from the Worker Pool
- Notification configuration

### Outputs
- Windows toast (individual or grouped)

### Out of Scope
- Push notifications to mobile devices (V3 feature, deferred)
- In-app notification center / notification history (V3)

### Configuration
- `notification_grouping_enabled` (default: false)
- `notification_grouping_mode` (`end_of_minute` | `after_minutes` | `after_count`)
- `notification_grouping_param` (numeric threshold for `after_minutes` / `after_count`)

### Internal Entities
- `INotificationDispatcher`
- `NotificationGrouper`

### Implementation
`MostaqlK/Services/NotificationDispatcher.cs`

---

## 13. UI System

### Type
UI System

### Purpose
Provides the entire user-facing surface of the application: the system tray icon, the main window (project feed + settings), and a rich set of reusable MAUI components. Follows the MVVM pattern, the design system tokens, and the UI component engineering rules.

### Responsibilities
- Present the real-time project feed with unread/read state and enrichment status badges
- Surface tray icon with four states: idle, polling, backlog draining, error
- Provide settings panel for all configurable options
- Display skeleton loading states during any data fetch
- Surface `DomainError.ExternalMessage` and `FixMessage` to the user in Arabic when operations fail
- Support RTL layout natively (Arabic-first content direction)

### Features
- Flat, card-based project feed in reverse-chronological order
- Unread/read visual distinction (accent bar + typography weight)
- Enrichment status badges (`enriched` / `pending` / `failed`)
- Status bar: current mode, live rate-budget indicator
- Active query indicator showing current `query_params` target
- Footer: running totals, "N unread" counter, notification toggle
- Light and dark themes (system-default or manual toggle)
- RTL-native layout using MAUI logical properties and Unicode bidi isolation for mixed-direction content
- Onboarding letterbox illustration panels (first-run only)
- Sticker assets (empty-state, error-state, success)

### In Scope
- All visual presentation and interaction handling
- Driving ViewModel commands from View events
- Exposing `DomainError` fields to the UI as ViewModel observable properties

### Out of Scope
- Business logic (in services and domain layer)
- HTTP requests, database writes
- Notification dispatch

### Subcomponents
- [Tray Icon](#131-tray-icon)
- [Main Window](#132-main-window)
- [Design System](#133-design-system)
- [MVVM Architecture](#134-mvvm-architecture)

### Implementation
`MostaqlK/Views/`, `MostaqlK/ViewModels/`

---

### 13.1 Tray Icon

### Type
UI System

### Purpose
The always-present system tray entry point. Reflects the pipeline's current health/activity state and gives the user quick access to common actions without opening the main window.

### Responsibilities
- Show one of four icon states: idle, polling, backlog draining, error
- Right-click menu: Open window, Pause / Resume polling, Check now (force poll), Recent notifications (last 5–10), Settings, Quit
- Hide main window on close (does not exit the process)
- Only "Quit" from this menu exits the process

### Out of Scope
- Displaying individual project details (handled by Main Window / toasts)

---

### 13.2 Main Window

### Type
UI System

### Purpose
The primary interaction surface for browsing and managing discovered projects. Contains the project feed, status information, settings access, and the query/search interface.

### Subcomponents
- **Status Bar** — pipeline mode, rate-budget indicator, settings shortcut
- **Active Query Indicator** — shows current `query_params` target with edit shortcut
- **Project Feed** — reverse-chronological `CollectionView` of `ProjectCard` components
- **Settings Panel** — all user-configurable options (poll interval, rate, grouping, etc.)

---

### 13.3 Design System

### Type
Infrastructure

### Purpose
Provides the visual language, component primitives, and engineering rules that all Views must follow. Ensures visual consistency, accessibility, and correctness across the entire UI.

### Subcomponents
- **Skeleton Loading (ShimmerBox)** — animated shimmer placeholder; paired 1:1 with every content element; renders during any loading state; uses `TranslationX` sweep animation at 1400ms/linear
- **TruncatingLabel** — smart truncation with optional character-count cap (`MaxChars`); appends `U+2026` ellipsis; wraps MAUI's `TailTruncation`
- **LabelWithSubText** — compound label exposing a `SubText` slot; canonical binding target for `DomainError.ExternalMessage` and `FixMessage`; sub-text row hidden (not just empty) when null
- **Icon System (Three Tiers)** — Tier 1 neutral, Tier 2 brand/positive, Tier 3 conceptual per-row color; all icons require three visual states (Normal / Hover / Disabled) via `VisualStateManager`
- **Letterbox Panels** — fixed dark-canvas (non-theme-reactive) onboarding illustrations
- **Sticker Assets** — theme-reactive SVG illustrations for empty/error/success states

### Configuration
- Color tokens: `AccentPrimary` (`#2386C8`), `AccentPositive` (`#2E9E6B`), `SkeletonBase`, `SkeletonShimmer` (light and dark variants)
- Typography: Lyra El-Mesry (Arabic), clean grotesque (Latin/numerals)
- All spacing from the design token set (`Spacing.XS` through `Spacing.XL`); no ad-hoc numeric literals

---

### 13.4 MVVM Architecture

### Type
Architectural Pattern

### Purpose
Separates UI presentation (View), state and commands (ViewModel), and domain data (Model) to ensure testability, maintainability, and a clean contract between the pipeline and the UI.

### Responsibilities
- **ViewModel** catches `Result<T>.Err` from service calls; exposes `ExternalMessage` and `FixMessage` as observable properties bound to the view; never lets exceptions escape to the View
- **View** drives ViewModel commands via bindings; never contains business logic; never throws
- **Model** represents domain entities (`ProjectSummary`, `ProjectDetails`, `DomainError`)

### Error Handling
- ViewModels catch `Result.Err`; log `InternalMessage` (to developer logger); expose `ExternalMessage` → primary label; expose `FixMessage` → sub-text slot via `LabelWithSubText`
- Views are passive — an exception reaching a View is a programming bug

### Related Components
- [Error System](#14-error-system) — source of `DomainError` values
- [Design System](#133-design-system) — `LabelWithSubText` is the canonical error display component

---

## 14. Error System

### Type
Infrastructure

### Purpose
Provides a consistent, strongly typed error model that all modules must follow. Ensures every failure is traceable (via code + internal message), user-communicable (via Arabic external message), and optionally actionable (via optional fix message).

### Responsibilities
- Define the `DomainError` type (Code, InternalMessage, ExternalMessage, FixMessage, Cause)
- Define the `Result<T>` discriminated union (Ok / Err)
- Define per-module `Errors.cs` files as the single source of error construction for that module
- Define C# attributes (`[ErrorCode]`, `[ErrorCategory]`, `[NeitherContract]`, `[ErrorModule]`) for machine-readable annotation

### Features
- `DomainError` carries all four fields; `FixMessage` is the only nullable one
- Error codes follow `{DOMAIN}-{NNN}` format, never reused, sequentially assigned
- `Result<T>.Fail(DomainError)` is the canonical failure constructor
- `BatchResult<T>` pattern for pipeline batch operations (aggregate, not fail-fast)
- `AggregateException.Flatten()` required before processing multi-task failures
- `OperationCanceledException` is never caught or wrapped — always propagates

### In Scope
- `DomainError`, `Result<T>`, `BatchResult<T>`, `ItemFailure` types (in `MostaqlK.Core`)
- Per-module `Errors.cs` factory methods
- Error code registry

### Out of Scope
- Logging (responsibility of the caller — log `InternalMessage`, not `ExternalMessage`)
- UI presentation (responsibility of ViewModel + `LabelWithSubText`)

### Constraints
- No module may construct `Result<T>.Err` with a bare string literal outside of `Errors.cs`
- Factory methods in `Errors.cs` are `internal`; error code constants are `public`
- `DomainError.Cause` must always be populated when catching an exception

### Error Handling Registry (Module Prefixes)
| Prefix | Module |
|--------|--------|
| `CORE` | `MostaqlK.Core` |
| `DB` | `MostaqlK.Infrastructure.Database` |
| `HTTP` | `MostaqlK.Infrastructure.Http` |
| `PARSE` | HTML parsing (within Http module) |
| `POLL` | Poll pipeline service |
| `ENRICH` | Enrichment pipeline service |
| `DIFF` | Diff engine |
| `SCORE` | Recommendation engine (V2) |
| `UI` | ViewModel / View layer |

### Internal Entities
- `DomainError`, `ErrorCode` (`MostaqlK.Core/DomainError.cs`)
- `Result<T>` (`MostaqlK.Core/Result.cs`)
- `BatchResult<T>`, `ItemFailure` (`MostaqlK.Core/Domain/BatchResult.cs`)
- `ErrorAttributes.cs` (`MostaqlK.Core/ErrorAttributes.cs`)
- `Errors.cs` (per module)

### Implementation
`MostaqlK.Core/` + `{Module}/Errors.cs` in each project

---

## Component Relationship Map

```
Poll Service
  │  acquires token from →  Rate Limiter
  │  fetches via →          HTTP Scraper
  │  parses via →           HTML Parser (listing mode)
  │  passes candidates →    Diff Engine
  │                             │  queries →  Committed State Provider (SQLite)
  │                             │  queries →  In-Flight State Provider (InFlightTracker)
  │                             ↓
  │                         {unseen IDs}
  │  marks in-flight via →  In-Flight Tracker
  └─ enqueues to →          Discovery Queue
                                  │
                                  ↓ (per ID, × N workers)
                            Worker Pool
                              │  acquires token from →  Rate Limiter
                              │  enriches via →          Enrichment Service
                              │                              │ fetches via → HTTP Scraper
                              │                              │ parses via →  HTML Parser (detail mode)
                              │  commits to →             Storage Engine (Repository Layer)
                              │  notifies via →           Notification System
                              └─ releases via →           In-Flight Tracker (MarkComplete in finally)

Storage Engine ←── reads ── Diff Engine (Committed State Provider)
Storage Engine ←── reads ── UI System (ViewModels / Project Feed)
Error System ────── used by ── all modules (DomainError factories)
UI System ────────── displays ── Error System (ExternalMessage + FixMessage via LabelWithSubText)
```

---

## Component Identification Summary

| Component | Type | Removing It Would… |
|---|---|---|
| Poll Service | Service | Stop all discovery — the app becomes inert |
| Diff Engine | Engine | Cause duplicate enrichment / data corruption under concurrency |
| In-Flight Tracker | Infrastructure | Cause duplicate enrichment on every poll |
| Discovery Queue | Infrastructure | Block poll loop from workers (no async decoupling) |
| Worker Pool | Engine | Projects are never enriched or committed |
| Rate Limiter | Infrastructure | App behaves as a scraper; rate-banned by Mostaql |
| Enrichment Service | Service | No detail data is ever fetched or parsed |
| HTTP Scraper | Integration | No outbound communication possible |
| HTML Parser | Processor | Raw HTML can never become structured data |
| Storage Engine | Data System | Nothing is persisted; pipeline output is lost |
| Notification System | Service | User is never informed of new projects |
| UI System | UI System | No user interaction possible |
| Error System | Infrastructure | Errors are unstructured, untraceable, and not surfaceable to the user |

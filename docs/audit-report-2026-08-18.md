# MostaqlK Codebase Audit Report

**Date:** 2026-08-18
**Scope:** Read-only review of the current codebase, UI, and patterns against the steering docs (`base`, `v1`, and relevant `v2` docs) and `UNITS.md`.
**Nature:** This is a snapshot audit only — no code changes were made. It exists to establish a shared, evidence-backed baseline before scoping the next "big quest." Every claim below cites a concrete file path (and line numbers where available).

---

### Overall picture

The project is considerably further along than a fresh MVP. The core V1 pipeline (`poll → discover → enrich → store → notify → display`) is fully implemented end-to-end with real, non-trivial logic (token-bucket rate limiting, a 3-state diff engine, WAL-mode SQLite with FTS5), and a meaningful chunk of what the docs label "V2" (query params, notification grouping, Arabic-aware search) is already built as well.

---

### (a) Architecture / pipeline — with citations

| Stage | Class / Method | File | Notes |
|---|---|---|---|
| Poll | `PollService.RunLoopAsync` (loop) / `PollOnceAsync` (single cycle) | `Services/Pipeline/PollService.cs:90-184`, `:186-250` | Orchestrates each scrape+diff cycle. |
| Discover | `MostaqlScraper.FetchListingAsync` → `ListingParser.Parse` | `Infrastructure/Http/MostaqlScraper.cs:35-63` (parse call at line 56) | Extracts `ProjectSummary` objects from feed HTML. |
| Enrich | `EnrichmentWorker.ProcessAsync` → `_enrichmentService.EnrichAsync` | `Services/Pipeline/WorkerPool/EnrichmentWorker.cs:102-188` (call at line 110) | Per-project detail fetch/parse. |
| Store | `ProjectRepository.InsertSummaryAsync` (discovery) / `UpsertDetailsAsync` (enrichment) | `Infrastructure/Database/ProjectRepository.cs:19-80`, `:83-116+` | Two-phase write matching store-and-forget policy. |
| Notify | `NotificationDispatcher.NotifyNewProjectsAsync` → `HandleFlush` → `WindowsToastSender.SendAsync` | `Services/NotificationDispatcher.cs:46-76`, `:78-116` (send call at line 105) | Feeds `NotificationGrouper` before delivery. |
| Display | `MainWindowPage.xaml` / `ProjectFeedViewModel.cs` | `Features/Projects/` | Renders feed + unread highlighting. |

**Worker pool & rate limiter (vs `worker-pool-and-rate-limiter.md`):**
- `WorkerPool` (`Services/Pipeline/WorkerPool/WorkerPool.cs:11`) manages a fixed pool of `EnrichmentWorker` instances; default `WorkerCount` is **3** (line 26).
- `TokenBucketRateLimiter` (`Services/Pipeline/TokenBucketRateLimiter.cs:24`) implements the token-bucket algorithm described in the doc, including a `SafeRequests` mode with `SafeModeMinimumSpacing` of **1 second** (line 42).

---

### (b) Diff engine & concurrency — with citations

- **Discovery backlog hydration:** `WorkerPool.StartAsync` (`Services/Pipeline/WorkerPool/WorkerPool.cs:51`) rehydrates the in-memory queue from `_projectRepository.GetBacklogIdsAsync` on restart — this is the crash-recovery behavior `concurrency-model.md` requires.
- **Diff engine:** `DiffEngine.DiffAsync` (`Services/Pipeline/DiffEngine/DiffEngine.cs:23-56`) compares freshly-polled project IDs against:
  - `committedIds` — sourced from `SqliteCommittedProvider` (already-stored projects), and
  - `inFlightIds` — sourced from `InFlightSetProvider` (currently-enriching projects).
  This implements the documented `unseen` / `in_flight` / `committed` three-state model.
- **Concurrency guards:**
  - SQLite WAL mode: `SqliteConnectionFactory.CreateConnection` (`Infrastructure/Database/SqliteConnectionFactory.cs:52`) issues `PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;`.
  - Transactional writes: `ProjectRepository.cs` wraps all writes in explicit transactions (e.g. `InsertSummaryAsync` at line 24).
  - In-memory thread safety: `NotificationDispatcher.cs` guards its history buffer with a dedicated `Lock _historyGate` (line 16).

---

### (c) Storage / data model — with citations

Schema is defined in `SqliteConnectionFactory.InitialSchemaSql` (`Infrastructure/Database/SqliteConnectionFactory.cs:158-228`):

| Table | Lines | Purpose |
|---|---|---|
| `projects` | 159-177 | Primary project metadata store, includes `enrichment_status` column. |
| `owners` | 179-192 | Client/owner information. |
| `project_skills` / `assets` | 194-213 | Normalized child tables for skills tags and attachments. |
| `discovery_backlog` | 215-219 | Tracks discovered-but-not-yet-enriched project IDs (backs the diff engine's crash recovery). |
| `projects_fts` | 221-227 | FTS5 virtual table with the `unicode61` tokenizer for Arabic-aware search. |
| `app_secrets` | 147-151 | Encrypted key-value store for cookies/tokens. |

This matches `data-model-schema.md`'s documented tables, and the presence of `projects_fts` with `unicode61` confirms the V2 search-and-filtering storage foundation (per `v2/product/search-and-filtering.md`) is already built, not just planned.

---

### (d) Error handling / resilience — with citations

- **Retry policy:** `EnrichmentWorker.cs:17-24` defines `RetryDelays` as a fixed backoff ladder: **1m, 2m, 4m, 8m, 15m** (5 attempts total).
- **Implementation:** `ProcessAsync` (line 107) loops through these delays; on exhaustion it logs `EnrichErrors.MaxAttemptsExhausted` (line 138) and transitions the worker to `WorkerState.Error`. This is a concrete, working instance of the retry/backoff policy required by `error-handling-and-resilience.md`.

---

### (e) UI implementation status — with citations

| Unit | File | Status | Evidence |
|---|---|---|---|
| `ModalPresenter` | `UI/PlatformConcepts/ModalPresenter.cs:26` | **Scaffold** | Returns bare `new ContentView()`. |
| `Drawer` | `UI/PlatformConcepts/Drawer.cs:25` | **Scaffold** | Returns bare `new ContentView()`. |
| `ActionMenu` | `UI/PlatformConcepts/ActionMenu.cs:27` | **Scaffold** | Returns bare `new ContentView()`. |
| `DesignTokens` | `UI/DesignSystem/DesignTokens.cs:11-52` | **Implemented** (UNITS.md is stale) | Contains real brand colors, `XS`–`XL` spacing scale, and corner-radius tokens — this is a discrepancy between `UNITS.md` (lists it as "Scaffold") and the actual code, which should be corrected in `UNITS.md`. |
| `TruncatingLabel` | `UI/DesignSystem/TruncatingLabel.cs:20-24` | **Scaffold** | Only sets `LineBreakMode.TailTruncation`; the `MaxChars`-based truncation logic itself is missing (see TODO at line 23). |

Planned-but-unstarted units (`IconSystem/`, `Letterbox/`, `Stickers/`) remain folder placeholders only; `Letterbox` is validated in HTML at `.repertoire/design/mvp/onboarding.html` but has no MAUI implementation.

**Action item:** `UNITS.md`'s `DesignTokens` row should be updated from "Scaffold" to "Implemented" to reflect actual code state.

---

### (f) Configuration — with citations

`SettingsViewModel.cs` (`Features/Settings/ViewModels/`) persists settings via MAUI `Preferences` under these keys:

`settings_poll_interval_seconds`, `settings_max_requests_per_minute`, `settings_max_concurrent_detail_fetches`, `settings_query_params`, `settings_include_assets`, `settings_notification_grouping_enabled`, `settings_grouping_mode`, `settings_grouping_threshold`, `settings_is_dark_mode`, `settings_safe_requests`.

- **Live reconfiguration:** the `RequestsPerMinute` and `SafeRequests` property setters call `_rateLimiter.Apply()` immediately (line 18) — rate-limit changes take effect without an app restart, ahead of what a minimal `configuration-reference.md` reading would imply.
- All of these keys map to settings already documented in `v1/product/configuration-reference.md` plus the V2-scope `query_params`/`include_assets`/grouping settings from `overview.md § v2` — no undocumented settings or documented-but-missing settings were found in this pass.

---

### (g) Known gaps — with exact citations

1. **Legacy dead-code catch blocks** in `EnrichmentWorker.cs`:
   - Line **171** — `catch (NotImplementedException)` wrapping the now-fully-implemented `UpsertDetailsAsync` storage call.
   - Line **184** — `catch (NotImplementedException)` wrapping the now-fully-implemented `NotifyNewProjectsAsync` call.
   - These are defensive leftovers from an earlier development stage where those dependencies were stubs; they no longer serve a purpose and should be removed as cleanup.

2. **Activation filter gap** in `ToastActivator.cs`:
   - `OnActivated` (`Infrastructure/Notifications/ToastActivator.cs:70-109`) parses launch arguments into an `args` dictionary but only ever consumes `projectId` (line 95).
   - The `filter=unread` argument that grouped notifications attach on launch is parsed into `args` but never read; line 101 navigates to the main page unconditionally, so the "jump straight to unread" behavior implied by grouped-notification UX is not actually wired up.

3. **Unread state is UI-only:** `ProjectCardViewModel.cs:249` has a TODO to persist the "read" flag via `IProjectRepository` — currently the read/unread toggle only lives in memory/UI state and does not survive an app restart.

4. **Missing RTL switch on startup:** `App.xaml.cs:55` has a `TODO(RTL)` — the Arabic-first `FlowDirection` switch referenced in the base tech README ("Arabic-first data handling") is not wired at startup.

---

### (h) Test coverage detail

`MostaqlK.UITests/` (Appium-based):

- **`DataSyncTests.cs`** — verifies UI/DB synchronization; specifically `CountFtsMatches` (line 92) proves the search box correctly queries the FTS5 index end-to-end.
- **`ProjectsPageTests.cs`** — validates user flows via `InteractionLogger` marks: `RefreshCommand` (line 47) is verified by reading `interaction-log.txt`; feed scrolling and navigation to project details are exercised in `OneTimeSetUp` (line 81).

**Coverage gaps (zero test coverage found):**
- Advanced/full search UI navigation — `MainWindowPage.xaml.cs:87` itself has a TODO marking this as unbuilt/untested.
- Notification grouping settings persistence (the `settings_notification_grouping_enabled` / `settings_grouping_mode` / `settings_grouping_threshold` flow) has no Appium coverage.
- The unread-filter activation path from `ToastActivator.cs` (see gap #2 above) is untestable as-is since it isn't implemented, and isn't covered once it is.

---

### (i) All TODO/FIXME/NotImplementedException occurrences found

| Location | Note |
|---|---|
| `App.xaml.cs:55` | `TODO(RTL)` — Arabic-first `FlowDirection` switch missing at startup. |
| `ProjectCardViewModel.cs:249` | `TODO` — persist read/unread state via `IProjectRepository` (currently UI-only, see gap #3). |
| `Drawer.cs:15-18`, `ModalPresenter.cs:16-19`, `ActionMenu.cs:16-19` | `TODO` — mobile (Android/iOS) shapes not implemented; explicitly V3 scope, not a V1 defect. |
| `TruncatingLabel.cs:23` | `TODO` — apply actual `MaxChars`-based truncation logic. |
| `InteractionLogger.cs:67` | `throw new NotImplementedException` for the Android/iOS `InteractionLogPath`; V1 is Windows-only so this is currently dead on the shipping platform but would block a V3 mobile build if hit. |
| `EnrichmentWorker.cs:171, 184` | `catch (NotImplementedException)` — legacy defensive catches for paths that are now fully implemented (see gap #1). |
| `MainWindowPage.xaml.cs:87` | `TODO` — advanced/full search navigation not yet built (see test-coverage gap above). |

---

### Next step

Awaiting the specific problem/requirement ("the big quest") to scope against this evidence-backed baseline, including checking `UNITS.md` for reuse (and correcting its stale `DesignTokens` status) and the relevant `.repertoire/design/mvp/` mockups before any implementation.

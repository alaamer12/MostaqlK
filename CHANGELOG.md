# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased] — multi-source pre-phase (1.0.4.dev3…dev8)

### Added

- `Core/Sources/` multi-source contracts (`SourceId`, `SourceProjectId`, `ISourceDescriptor`, `SourceCapabilities`, `SourceRegistry`, discovery/enrichment + listing/detail strategy interfaces, `ISettingsRepository`) and `Sources/Mostaql/` — the first vertical source module holding every mostaql.com fact (descriptor, fetcher, parsers, listing/enrichment services, `AddMostaqlSource()` DI registration).
- Versioned SQLite schema steps `PRAGMA user_version` 1→2→3 (`Migrations/0002_add_source_identity.sql`, `0003_add_source_unique_keys.sql`): `source_id` + `site_project_id` on projects, `source_id` on owners/backlog, unique `(source_id, site_project_id)` and `(source_id, owner_id)`, backlog rebuilt keyed `(source_id, project_id)`; Mostaql's internal-id band is 0 so all pre-existing rows, child tables, and FTS references stay valid untouched. Fresh installs bootstrap at the current version directly. Verified by the new `tools/DbMigrationTests` harness against synthetic v1 data, a real 757-project database copy, and the live user database on first dev8 launch.
- Per-source pipeline engine: `SourcePipeline` + `SourcePipelineFactory` + `SourcePipelineOrchestrator` give each registered website its own queue, in-flight tracker, rate budget, diff providers, poll loop, worker pool, and `SourcePipelineStatus` telemetry; `GlobalAppStatusService` became the active-source projection facade (bindings unchanged) with tray aggregates.
- Brand tokens unit: `UI/DesignSystem/BrandTokens/` (`IBrandTokenProvider` + `SourceBrandTokenProvider`) sourced from each descriptor's `BrandTokenSet`.

### Changed

- Composite `SourceProjectId` identity threaded through repositories, discovery queue, in-flight tracker, diff engine, workers, backlog, notifications, routes (`ProjectDetails?sourceId=&projectId=`, absent `sourceId` = default source so old deep links keep working), and toast activation arguments.
- SQLite repositories now map rows through Dapper + explicit database-shape DTOs (`Infrastructure/Database/RowMaps.cs`) instead of hand-written ordinal reader loops; SQL statements, write paths, error shapes and mapping defaults are byte-preserved (verified against a read-only replay of the live database).
- Web-parsing logic generalized into shared source-blind engines (`Core/Parsing/`: `HtmlStructureEngine`, `FieldInferenceEngine`, `ParseProfile`, `SelectorMap`); the Mostaql parsers are thinned to per-source data plus call order so a second site reuses the same engines with its own profile instead of duplicated code (135 golden-parser tests unchanged). Added source-contribution seam contracts (`ISourcePageContribution`, `ISourceSettingsSectionContribution`, `SourceContributionRegistry`) so a source can grow its own pages/settings without the shared UI branching on it.
- FTS search is source-scoped: `FtsQueryService.SearchAsync(source, query)` filters by `source_id` and resolves full composite identity (`Source` + site id) like every other reader, replacing the historical raw-internal-id/no-source row shape.
- Shared code de-coupled from mostaql.com: error texts take the site name from the descriptor; link chip renamed `MostaqlLinkButton` → `SourceLinkButton` (descriptor-driven label + brand palette); toast button copy resolves the source display name; `BudgetFormatter` takes the source's currency suffix; card/details view-models build fallback URLs via `descriptor.BuildProjectUrl`; owner fallback meta comes from the descriptor; settings namespaced per source with legacy flat-key read-through (lazy migration, no data-loss step); per-source cookie store/jar with real Netscape domain-column filtering and seamless rewrite of the legacy secret row; `HttpClient` headers built from descriptor-supplied policy values; the site's session-cookie name is a descriptor fact (`SessionCookieName`).

## [1.0.4] - 2026-09-02

### Added

- Implemented dedicated thread-aware crash diagnostics service (`CrashReporter`) capturing thread telemetry (ManagedThreadId, IsThreadPool, IsBackground, Priority), environment metrics (WorkingSet, GC memory, process uptime), and complete inner exception hierarchy logging to `%LocalAppData%/MostaqlK/log/crash.log`.
- Added `-ResetDatabase` opt-in switch to all PowerShell release and deployment scripts (`release-windows.ps1`, `release-macos.ps1`, `release-mobile.ps1`, `deploy.ps1`, `github-release.ps1`), preserving local SQLite database state by default across release builds.
- Added dynamic runtime version binding to `AppSidebar` (`VersionLabel`) via `AppInfo.Current.VersionString` with design-time fallback.

### Changed

- Decoupled background scraping and polling loops from startup window initialization with deferred execution (`EnsurePipelineStarted`), adding a non-blocking breathing delay after initial UI layout to prevent UI thread contention.
- Updated `version-bumping` skill to track and synchronize in-app navigation side-panel and about-page version elements.

### Fixed

- Fixed splash screen and main window startup delays caused by premature pipeline execution during XAML inflation and initial layout.
- Hardened long-running background workers (`EnrichmentWorker`, `WorkerPool`, `PollService`, `TokenBucketRateLimiter`, `MostaqlScraper`) against process crashes, unobserved task faults, and arithmetic/TimeSpan calculation overflows.

## [1.0.3] - 2026-08-26

### Fixed

- Fixed Windows system tray icons to use high-DPI rounded-centroid brand logos with dynamic pipeline status badges (Idle = Orange, Polling = Blue, Processing = Green, Error = Red) instead of generic Win32 stock placeholder glyphs (`IDI_APPLICATION`, `IDI_WARNING`, `IDI_QUESTION`).

### Added

- Added `tray-inspection` agent skill with Win32 memory scanning, tray item enumeration, live icon extraction routines, and tray diagnostics workflow.

## [1.0.2] - 2026-08-26

### Fixed

- Fixed preferences persistence (onboarding completion status, window close behavior, and user settings) across application restarts and tray exits in portable/unpackaged mode by implementing `FilePreferences`.
- Fixed theme resolution inconsistency where unconfigured theme state switched to light mode instead of matching the system theme across application restarts.
- Fixed process locking errors during portable release builds when existing background instances held locks on the database and output executable.

## [1.0.1]

### Fixed

- Fixed tray icon not visible after leaving the app in the background.

## [1.0.0]

### Added

- Initial release of MostaqlK desktop application.

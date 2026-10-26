# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Two-channel build support: `main` ships stable (`v1.0.4`) and `canary` ships prerelease builds (`v1.0.4.devN-canary`). CI derives the channel from the branch and passes `-p:Channel=canary`, which stamps `CHANNEL_CANARY` and appends `.Canary` to every persistent identity.
- `ChannelIdentity` (`Core/Platform/ChannelIdentity.cs`) as the single source for the app-data root (`%LocalAppData%\MostaqlK.Canary`), named kernel objects (singleton/wake/watchdog mutexes, AppInstance key), AUMID, toast COM CLSID, HKCU Run value and tray temp directory, so stable and canary binaries can run side by side with total isolation.
- Canary-only toast activation CLSID and Start Menu shortcut (`MostaqlK.Canary.lnk`), and a `.Canary` badge on the splash, tray tooltip, sidebar and About version labels.

### Changed
- Stable release tags and artifacts now use clean SemVer (`v1.0.4`) instead of exposing the internal `devN` counter; the dev counter and `-canary` suffix are canary-channel only.
- Release scripts accept `-Channel`; the canary channel skips the `tools/reset*.ps1` scripts because they target the stable data root.
- Bundle-extraction guard relaunches a process that landed in the other channel's `bundle-cache` (the single-file apphost extracts before managed code runs, using a user-scoped env var both channels share).

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

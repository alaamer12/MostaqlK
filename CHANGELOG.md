# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

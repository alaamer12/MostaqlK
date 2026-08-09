# settings-tray-notifications-builder — Report

## Goal
Implement Step 7 of the MostaqlK V1 plan: real, persisted, live-applied Settings; a real
Notifications flyout/center; a filled-in About page; and real Tray Icon state/menu behavior.

## Files created
- `Platforms/Windows/TrayIconNativeHost.cs` — minimal native Windows tray icon host using
  Win32 `Shell_NotifyIcon` P/Invoke (add/modify/delete icon, routes tray clicks to the
  "Open" menu action).
- `.repertoire/agents/settings-tray-notifications-builder.md` — this report.

## Files modified
- `Services/Pipeline/TokenBucketRateLimiter.cs` — `Capacity`/`RefillPerSecond` are now
  settable; added `Reconfigure(capacity, refillPerSecond)`.
- `Services/Pipeline/PollService.cs` / `IPollService.cs` — added mutable
  `PollIntervalSeconds`, `Status`/`StatusChanged` (new `PollServiceStatus` enum:
  Idle/Polling/BacklogDraining/Error), `IsPaused`/`SetPaused`, `RequestCheckNow`. The poll
  loop now uses a `Task.Delay` + `SemaphoreSlim` race instead of a fixed `PeriodicTimer` so
  the interval can change live and "check now" can interrupt the wait.
- `Services/Pipeline/DiscoveryQueue.cs` — added `Count` (backlog size) for tray-icon state.
- `Services/NotificationDispatcher.cs` / `INotificationDispatcher.cs` — added a bounded
  (last 10, newest-first) in-memory `RecentHistory` + `HistoryChanged` event, populated from
  `HandleFlush`. No DB persistence (in-memory only, per V1 scope).
- `Infrastructure/Database/IProjectRepository.cs` / `ProjectRepository.cs` — added
  `CountAddedTodayAsync()` (SQL `date(discovered_at) = date('now')`) to back the "مشاريع
  مضافة اليوم" stat card.
- `Features/Settings/ViewModels/SettingsViewModel.cs` — rewritten: real bindable properties
  (`PollIntervalSeconds`, `RequestsPerMinute`, `GroupingMode`, `GroupingThreshold`,
  `IsDarkMode`, `ProjectsAddedTodayCount`) with validation (`ValidationMessage`/
  `HasValidationError`), `Preferences`-backed load/persist, and live-apply on every valid
  change into `IPollService`/`TokenBucketRateLimiter`/`NotificationGrouper`. Dark mode is
  applied via `Application.Current.UserAppTheme` (no prior theming mechanism existed).
- `Features/Settings/Views/SettingsPanel.xaml` — rebuilt against `settings.html` using
  `AppEntry`/`AppToggle` (replacing the plain `Switch`) and a validation-message label.
- `Features/Notifications/ViewModels/NotificationCenterViewModel.cs` — sources
  `RecentNotifications` from `INotificationDispatcher.RecentHistory`/`HistoryChanged`;
  `OpenProjectCommand` navigates to `ProjectDetailsPage?projectId=...` (same route/param
  used by `ProjectFeedViewModel`).
- `Features/Notifications/Views/RecentNotificationsFlyout.xaml` — real list (title +
  `PostedRelative`), tap-to-navigate, empty-state view.
- `Features/Projects/Views/MainWindowPage.xaml(.cs)` — hosts `RecentNotificationsFlyout` as
  an overlay; sidebar "التنبيهات" button toggles it; exposes `OpenNotificationsFlyout()` for
  the tray icon to call.
- `Features/Projects/Views/AboutPage.xaml(.cs)` — real content: app name, version/build from
  `AppInfo.Current.VersionString`/`BuildString` (not hardcoded), description, a tappable
  link opened via `Launcher.Default`.
- `UI/TrayIcon/TrayIconService.cs` — rewritten: `State`/`StateChanged` mirrored live from
  `IPollService.StatusChanged` + `DiscoveryQueue.Count`; all 6 menu commands wired to real
  actions (Open → navigate to `MainWindowPage`; Pause/Resume → `PollService.SetPaused`;
  Check now → `PollService.RequestCheckNow`; Recent notifications → opens the main window's
  flyout overlay; Settings → navigate to `SettingsPanel`; Quit → `Application.Current.Quit()`).
- `MauiProgram.cs` — registered `TrayIconService`; added `#if WINDOWS` lifecycle wiring
  (`ConfigureLifecycleEvents` → `AddWindows().OnWindowCreated/OnClosed`) to construct/dispose
  `TrayIconNativeHost` using the native window handle.
- `UNITS.md` — flipped `AppToggle` and `TrayIconService` from `Scaffold` to `Implemented`.

## Preferences keys used
`settings_poll_interval_seconds`, `settings_max_requests_per_minute`,
`settings_grouping_mode`, `settings_grouping_threshold`, `settings_is_dark_mode`.

## Live-apply mechanism per setting
- Poll interval → `IPollService.PollIntervalSeconds` (read every loop iteration).
- Requests/minute → `TokenBucketRateLimiter.Reconfigure(capacity, capacity/60.0)`.
- Grouping mode/threshold → `NotificationGrouper.Mode`/`AfterMinutesThreshold`/
  `AfterCountThreshold` (already live-reconfigurable per Step 5).
- Dark mode → `Application.Current.UserAppTheme`.

## Tray icon native hosting
Win32 `Shell_NotifyIcon` via P/Invoke in `Platforms/Windows/TrayIconNativeHost.cs`, created
from a Windows-only lifecycle hook in `MauiProgram.cs` (`OnWindowCreated` →
`WinRT.Interop.WindowNative.GetWindowHandle`). Icon glyph swaps between the 4 stock Win32
icons (`IDI_APPLICATION`/`IDI_QUESTION`/`IDI_WARNING`/`IDI_ERROR`) per `TrayIconState`.

## Known limitations
- The native tray icon does not yet host a real right-click popup context menu (Win32
  `TrackPopupMenu` + a subclassed `WndProc` were out of scope for this pass); left/right
  click on the icon currently triggers only the "Open" action. `TrayIconService.MenuItems`
  is fully wired and ready for a future popup-menu implementation.
- No dedicated tray icon artwork; uses Windows stock icons as placeholders.
- Notification history and unread badge count are in-memory only (reset on restart), per
  V1 scope (matches `NotificationDispatcher`'s existing design).

## Verification
- `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` — **succeeded, 0 errors**
  (only pre-existing MVVMTK0045 AOT-compatibility warnings on `[ObservableProperty]` fields
  across several view-models, unrelated to this change).
- Manual code-trace: settings changes flow from `SettingsViewModel` → `Preferences` +
  live service properties; `TrayIconService.State` changes are reachable from
  `PollService.StatusChanged` (Polling/Error) and `DiscoveryQueue.Count` (BacklogDraining),
  not hardcoded to Idle.

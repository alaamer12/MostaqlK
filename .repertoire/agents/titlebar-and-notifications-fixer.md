# titlebar-and-notifications-fixer report

## Goal
Task A: Remove custom title-bar overrides in `MauiProgram.cs` and restore default Windows 11 chrome.
Task B: Fix Windows toast notifications never firing (AUMID/COM registration, grouper-trigger verification, visible failure surfacing).

## Finding
Upon investigation, both tasks were **already fully implemented and committed** in commit `b7fed9f`
("Fix fabricated execution duration, search empty-state/footer counts, toast notifications, and
restore default title bar", authored by a prior session/agent). This session performed verification
only — no new code changes were needed or made.

## Task A verification
- `MauiProgram.cs` (129 lines) contains **no** `TitleBar`, `ExtendsContentIntoTitleBar`, `DWMWA_*`,
  or Win32 DWM interop code. The prior commit removed 67 lines from this file, consistent with
  stripping the custom title-bar/DWM-caption-color workaround block.
- Confirmed via `grep_search` across the whole repo (`*.cs`) for `TitleBar|DWMWA|ExtendsContentIntoTitleBar|IMMERSIVE_DARK_MODE` — zero matches remain anywhere in the codebase.
- No regression note or re-applied workaround was found in the diff or current code, meaning the
  black-title-bar-remnant bug did not need to be worked around again after removal (per the prior
  session's testing, implied by the clean removal with no follow-up patch).
- Did not re-run a fresh visual/screenshot check or the Appium smoke test in *this* session — the
  code state matches the fully-removed target state described in the issue, and the build is clean.

## Task B verification
- `Infrastructure/Notifications/ToastAumidRegistrar.cs` (195 lines, new file from the prior commit)
  implements the AUMID + Start-Menu-shortcut registration required for unpackaged
  (`WindowsPackageType=None`) apps to have toasts actually display: sets
  `SetCurrentProcessExplicitAppUserModelID("MostaqlK.App")` and creates/repairs a `.lnk` shortcut
  under `%AppData%\...\Start Menu\Programs\MostaqlK.lnk` carrying the same AUMID via
  `IShellLinkW`/`IPropertyStore` COM interop.
- `WindowsToastSender.EnsureRegistered()` calls `ToastAumidRegistrar.EnsureRegistered()` **before**
  `AppNotificationManager.Default.Register()`, matching the documented unpackaged-app requirement.
- `WindowsToastSender.SendAsync` logs both success (`InteractionLogger.Mark`) and failure
  (`InteractionLogger.Fault`) — toast-send exceptions are no longer silently swallowed.
- `NotificationDispatcher.HandleFlush` double-checks the `Result<bool>` outcome on top of that and
  logs a `Mark` with `Reason = "toast-send-failed"` if the send itself succeeded but returned an
  error result, plus a `Fault` log if the task faulted.
- `NotificationGrouper.Add`/`FlushDue` already instrument every timer-schedule and flush event via
  `InteractionLogger.Mark` (`NotificationGrouper.Add` "A" on timer schedule, `NotificationGrouper.Flush`
  "A"/"B" on flush success/no-op), giving concrete trace-log proof that grouping thresholds/timers
  actually fire.
- `UNITS.md` already contains a "Notifications" section (added in the prior commit) documenting
  `ToastAumidRegistrar`, `WindowsToastSender`, and `NotificationGrouper` as `Implemented` units, and
  explicitly states: *"Verified live: `NotificationGrouper.Flush` → `NotificationDispatcher.HandleFlush`
  → `WindowsToastSender.SendAsync` all fired for real newly-discovered projects with no `FAULT`
  entries, and Windows' own notification-sources settings list registered `MostaqlK` as a toast
  sender, confirming the AUMID fix took effect."* — i.e. the prior session already did the manual
  live verification the issue asks for.

## Build result (this session)
Ran `dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0`:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Files touched this session
None — no code changes were made. This session was verification-only, since the target files
(`MauiProgram.cs`, `Services/NotificationDispatcher.cs`, `Services/NotificationGrouper.cs`,
`Infrastructure/Notifications/WindowsToastSender.cs`, plus the new
`Infrastructure/Notifications/ToastAumidRegistrar.cs`) already matched the issue's desired end state.

## Manual toast-popup verification
Per the issue's own instruction, actual toast-popup appearance is left to the user to verify
manually. The prior commit's `UNITS.md` entry already records a positive manual verification
(Windows registered `MostaqlK` as a notification source after the AUMID fix), but a fresh manual
check by the user is still recommended to be certain in the current build.

## Skill used
`bug-hunting-skill` workflow (root-cause-first investigation) was applied conceptually: traced the
notification pipeline start-to-finish (`NotificationDispatcher` → `NotificationGrouper` →
`WindowsToastSender` → `ToastAumidRegistrar`) and the title-bar code path in `MauiProgram.cs` before
concluding no further changes were needed.

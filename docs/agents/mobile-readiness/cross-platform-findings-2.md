# Cross-Platform Findings — Agent 2 (UI/*, Features/*, Core/*)

Audit scope: `UI/*`, `Features/*`, `Core/*`, looking for Windows-only assumptions leaking into
shared UI/feature/core code instead of being isolated behind `PlatformSelect.For<T>()` or
`Platforms/Windows/` partials, per `.repertoire/.steering/v1/tech/cross-platform-ui-conventions.md`
and `UNITS.md`.

## Finding 1: Ad hoc `#if WINDOWS` for "Reveal in Explorer" instead of a shared capability
- File: `Features/Projects/ViewModels/ProjectDetailsViewModel.cs:68-78`
- Description: `AttachmentItemViewModel.RevealAsync()` branches on `#if WINDOWS` directly inside a
  Features view-model to call `System.Diagnostics.Process.Start("explorer.exe", ...)`, with a
  generic `Launcher.OpenAsync` fallback for other platforms. This is a genuine Windows-only
  capability ("reveal file in native file manager") implemented ad hoc in shared feature code
  rather than through a shared platform-capability abstraction, so every future call site that
  needs the same behavior will likely re-invent its own `#if WINDOWS` block instead of reusing one.
- Suggested fix: Extract a small platform-neutral service/interface (e.g. `IFileRevealService`)
  resolved via `PlatformSelect.For<T>()` or backed by a `Platforms/Windows/` partial, so
  `ProjectDetailsViewModel` never contains a compiler directive itself.

## Finding 2: `MotionPreferences` embeds a raw WinUI type inline via `#if WINDOWS` instead of a `.Windows.cs` partial
- File: `UI/PlatformComponents/MotionPreferences.cs:19-31`
- Description: Per `UNITS.md` and the cross-platform conventions doc, "Platform Components" that
  need per-OS behavior are supposed to be a shared partial class plus a `<Unit>.Windows.cs` file
  (Mechanism 1), the same pattern used by `AppButton`, `PressableEffect`, and `SplitterHandle`.
  `MotionPreferences` instead keeps its Windows-only body (`new Windows.UI.ViewManagement.UISettings()`)
  inline inside the shared `MotionPreferences.cs` file behind a raw `#if WINDOWS` block, which is
  exactly the "ad hoc `#if WINDOWS` scattered in shared code" pattern the conventions doc is meant
  to prevent — every other unit's shared file has zero WinUI type references.
- Suggested fix: Split into `MotionPreferences.cs` (shared property/API) +
  `MotionPreferences.Windows.cs` (the `Windows.UI.ViewManagement.UISettings` lookup), matching the
  `PressableEffect`/`SplitterHandle` partial-class pattern already used elsewhere in
  `UI/PlatformComponents/`.

## Finding 3: `UI/TrayIcon/TrayIconService.cs` — no cross-platform break found, verified clean
- File: `UI/TrayIcon/TrayIconService.cs` (whole file)
- Description: Not a defect — recorded for completeness since Tray Icon was called out
  explicitly in the audit brief. `TrayIconService` itself contains no WinUI/Win32 types; it only
  holds state (`TrayIconState`) and menu commands, delegating all native `Shell_NotifyIcon`
  interop to `Platforms/Windows/TrayIconNativeHost.cs`, exactly as documented under "Tray Icon" in
  `UNITS.md`. No unconditional call site into `TrayIconService`/tray APIs was found anywhere
  inside `UI/*`, `Features/*`, or `Core/*` — the only consumers are `MauiProgram.cs`,
  `Infrastructure/Notifications/ToastActivator.cs`, and
  `Infrastructure/Notifications/WinAppSdkVariation.cs`, all outside this audit's scope (likely
  belonging to another agent's area) and all already resolving `TrayIconService` via DI rather
  than referencing Windows types directly.
- Suggested fix: None required for this scope; flag to the agent auditing
  `Infrastructure/*`/root startup files (`MauiProgram.cs`, `App.xaml.cs`) to confirm those call
  sites are themselves properly Windows-gated.

## Finding 4: `Core/*` is fully platform-clean
- File: `Core/*` (whole directory)
- Description: Not a defect — recorded for completeness. `Core/DomainError.cs`,
  `Core/ErrorAttributes.cs`, `Core/ErrorCodeRegistry.cs`, `Core/ErrorOutcomeAttribute.cs`,
  `Core/Result.cs`, `Core/Domain/*`, and `Core/Formatting/*` (`ArabicProposalParser`,
  `ArabicRelativeTime`, `BudgetFormatter`, `LastScanText`) contain no Windows-specific types,
  `#if WINDOWS` blocks, or platform APIs of any kind. No action needed here.
- Suggested fix: None.

## Finding 5: `UI/PlatformConcepts/ActionMenu.cs` and `Drawer.cs` are intentional Windows-only scaffolds, not violations
- File: `UI/PlatformConcepts/ActionMenu.cs`, `UI/PlatformConcepts/Drawer.cs`
- Description: Not a defect — recorded for completeness. Both correctly use
  `PlatformSelect.For<T>()` with `android`/`ios`/`macCatalyst` branches explicitly set to `null`
  (documented as "added only when V3 mobile work starts"), matching the V1-reality-check rule in
  `cross-platform-ui-conventions.md`. These are the sanctioned pattern, not a cross-platform break.
- Suggested fix: None; other agents should not flag these `null` branches as missing mobile
  support — they are deliberate per V1 scope.

## Summary
- Finding 1: Ad hoc `#if WINDOWS` for "Reveal in Explorer" instead of a shared capability
- Finding 2: `MotionPreferences` embeds a raw WinUI type inline via `#if WINDOWS` instead of a `.Windows.cs` partial
- Finding 3: `UI/TrayIcon/TrayIconService.cs` — no cross-platform break found, verified clean
- Finding 4: `Core/*` is fully platform-clean
- Finding 5: `UI/PlatformConcepts/ActionMenu.cs` and `Drawer.cs` are intentional Windows-only scaffolds, not violations

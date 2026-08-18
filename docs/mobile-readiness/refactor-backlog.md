# Mobile Readiness Refactor Backlog

**Date:** 2026-08-18
**Source:** Consolidated from `cross-platform-findings-1.md`, `cross-platform-findings-2.md`, `abstraction-findings-3.md`, `abstraction-findings-4.md` (4 parallel audit agents).
**Purpose:** Single, deduplicated, prioritized list of work to execute in Steps 3-6 of the mobile-readiness-refactor-audit plan.

---

## P0 — Cross-platform breaks (top priority, user-named)

### 1. Notification stack is Windows-only with no interface boundary
- Files: `Infrastructure/Notifications/WindowsToastSender.cs`, `ToastActivator.cs`, `ToastAumidRegistrar.cs`, `WinRtVariation.cs`, `WinAppSdkVariation.cs`; `Services/NotificationDispatcher.cs`; `MauiProgram.cs:214`.
- Problem: All notification send/activation code lives in the platform-neutral `Infrastructure/Notifications/` folder using raw WinRT/COM/registry/shell APIs with **no `#if WINDOWS` guard at all**. Since `MostaqlK.csproj` already lists `net10.0-android` as a target framework, this is a real Android **compile-break risk**, not just a design smell. `NotificationDispatcher` and `MauiProgram.cs` both depend on the concrete `WindowsToastSender` type directly — no `INotificationSender`/`INotificationActivationHandler` interface exists.
- Fix: Introduce `INotificationSender` + `INotificationActivationHandler` interfaces (Step 4). Relocate `WindowsToastSender`, `ToastActivator`, `ToastAumidRegistrar`, `WinRtVariation`, `WinAppSdkVariation` under `Platforms/Windows/Notifications/` (or guard with `#if WINDOWS` in place), implementing the new interfaces. `NotificationDispatcher` depends only on the interfaces. Register Windows impl via DI in `MauiProgram.cs`.

### 2. Tray icon has no mobile equivalent — needs explicit capability mapping
- Files: `UI/TrayIcon/TrayIconService.cs` (clean, platform-neutral state holder) + its consumers in `MauiProgram.cs`, `Infrastructure/Notifications/ToastActivator.cs`, `Infrastructure/Notifications/WinAppSdkVariation.cs`; native interop in `Platforms/Windows/TrayIconNativeHost.cs`.
- Problem: `TrayIconService` itself is already reasonably clean (delegates native calls to `Platforms/Windows/TrayIconNativeHost.cs`), but there is **no formal "this capability doesn't exist on mobile" declaration** — it's constructed unconditionally wherever referenced rather than through an explicit `PlatformCapability<T>`-style mapping that would make the `Mobile => null` answer visible and typed.
- Fix: Build `Core/Platform/PlatformCapability.cs` (Step 3) and route `TrayIconService` construction/registration through it (`Windows => TrayIconService`, `Mobile => null`) (Step 4). Update all call sites (`MauiProgram.cs`, notification activation paths) to null-check/no-op gracefully.

### 3. Ad hoc `#if WINDOWS` scattered directly in shared UI/feature code (not the sanctioned partial-class pattern) — [DONE]
- Files:
  - `Features/Projects/ViewModels/ProjectDetailsViewModel.cs:68-78` (`AttachmentItemViewModel.RevealAsync()` — calls `explorer.exe` directly via `#if WINDOWS`).
  - `UI/PlatformComponents/MotionPreferences.cs:19-31` (embeds `Windows.UI.ViewManagement.UISettings` inline instead of a `.Windows.cs` partial like `AppButton`/`PressableEffect`/`SplitterHandle`).
- Problem: Both bypass the established `Platforms/Windows/` / `.Windows.cs`-partial / `PlatformSelect.For<T>()` conventions documented in `cross-platform-ui-conventions.md`, instead hand-rolling their own compiler directive.
- Fix: Extract `IFileRevealService` (or similar) for #1, resolved via `PlatformSelect.For<T>()`; split `MotionPreferences.cs` into shared + `MotionPreferences.Windows.cs` partial for #2.
- **Done:** `IFileRevealService` + `FileRevealService` (via `PlatformCapability<T>.Resolve`) with Windows `explorer.exe /select` + default folder-open impl; `MotionPreferences` split into shared shell + `MotionPreferences.Windows.cs` partial (`ResolveReducedMotion`).

### 4. Diagnostics log path assumes Windows-style filesystem — [DONE]
- File: `Services/Diagnostics/InteractionLogger.cs` (`ResolveLogFilePath`).
- Problem: Uses `Environment.SpecialFolder.LocalApplicationData` + raw file I/O instead of MAUI's cross-platform `FileSystem.AppDataDirectory`; separately, `InteractionLogger.cs:67` has `throw new NotImplementedException` for Android/iOS log path.
- Fix: Route through `FileSystem.AppDataDirectory` (cross-platform MAUI API) instead of `Environment.SpecialFolder`, removing the need for the NotImplementedException branch entirely.
- **Done:** `ResolveLogFilePath` now uses `Microsoft.Maui.Storage.FileSystem.AppDataDirectory` (aligns with UITests' `InteractionLogPath`).

**Not flagged (verified clean):** `Core/*` (fully platform-neutral), `UI/PlatformConcepts/ActionMenu.cs`/`Drawer.cs` `null` branches for android/ios (intentional V1-scope stubs, not violations), `SecretProtector`, `CloseBehaviorService`, `AppLifecycleService`, `Services/Pipeline/*`, `Infrastructure/Database|Http/*`.

---

## P0 — Abstraction breaks (top priority, user-named: ConfirmationBox)

### 5. Real confirmation-dialog pair exists but bypasses the `ModalPresenter` scaffold
- Files: `Platforms/Windows/ConfirmationDialog.cs` (base), `Platforms/Windows/CloseConfirmationDialog.cs` (specialization); scaffold at `UI/PlatformConcepts/ModalPresenter.cs:13-28`.
- Problem: `ConfirmationDialog`/`CloseConfirmationDialog` already implement exactly the base→specialization shape the project wants (their own doc comment compares them to `DebouncedEntry`/`SearchInputField`), but they're native WinUI `ContentDialog` types under `Platforms/Windows/`, never routed through `ModalPresenter` — which remains `return new ContentView();`.
- Fix (Step 5): Implement `ModalPresenter` for real (Windows shape backed by `ContentDialog`), fold `ConfirmationDialog`/`CloseConfirmationDialog` into new `UI/DesignSystem/ConfirmationBox.cs` (base) + `ExitConfirmationBox.cs` (specialization) built on `ModalPresenter.Current`.

### 6. `ActionMenu` and `Drawer` scaffolds have real-world use cases already built independently
- Files: `Platforms/Windows/TrayIconNativeHost.cs:128-169` (`ShowContextMenu` — raw Win32 `CreatePopupMenu`/`TrackPopupMenuEx`, bypasses `ActionMenu`); `Features/Notifications/Views/RecentNotificationsFlyout.xaml(.cs)` + `Features/Projects/Views/MainWindowPage.xaml(.cs)` (hand-rolled popover/backdrop, bypasses `Drawer`); scaffolds at `UI/PlatformConcepts/ActionMenu.cs:13-30`, `Drawer.cs:12-27`.
- Problem: Both scaffolds' own TODOs say "implement once a concrete use case exists" — those use cases already exist, built independently instead.
- Fix (Step 5, secondary to ConfirmationBox): Either (a) implement `ActionMenu`/`Drawer` for real using these as first consumers, or (b) for the tray menu specifically, document it as an intentionally native-only concern (tray icons have no MAUI visual tree) and update `ActionMenu`'s doc comment accordingly. Recommendation: fix `Drawer`/`RecentNotificationsFlyout` for real (in-app, has a MAUI visual tree); leave tray context menu as native-only with corrected doc comment (out of budget for this refactor, tracked as backlog item).

### 7. Second destructive-action call site with no confirmation at all
- File: `Features/Settings/ViewModels/SettingsViewModel.cs:264-268` (`ClearSessionCookieAsync`).
- Problem: No confirmation prompt before clearing the session cookie, unlike the exit flow.
- Fix: Once `ConfirmationBox` exists, wire a second usage (parameterized or a `ClearSessionConfirmationBox` specialization) here.

---

## P1 — Secondary abstraction/cleanup findings (address in Step 6, time permitting)

### 8. `UNITS.md` stale status: `DesignTokens` marked "Scaffold" but is fully Implemented
- File: `UI/DesignSystem/DesignTokens.cs` — real colors/spacing/corner-radius, no stubs.
- Fix: Update `UNITS.md` row to `Implemented`.

### 9. `TruncatingLabel.MaxChars` is a genuine no-op scaffold
- File: `UI/DesignSystem/TruncatingLabel.cs:1-26` (TODOs at lines 6, 23).
- Fix: Implement a property-changed handler that truncates `Text` to `MaxChars` and appends `…`.

### 10. Debounce mechanism duplicated between `DebouncedEntry` and `ProjectFeedViewModel` — [DONE]
- Files: `UI/PlatformComponents/DebouncedEntry/DebouncedEntry.cs:6-84` vs `Features/Projects/ViewModels/ProjectFeedViewModel.cs:50,175-191`.
- Fix: Extract the cancel-and-restart `CancellationTokenSource` mechanic into a shared non-UI helper (e.g. `Core/Debouncer`), consumed by both.
- **Done:** `Core/Debouncer.cs` extracted; both `DebouncedEntry` and `ProjectFeedViewModel.ScheduleAutoReload` consume it. Public APIs unchanged.

### 11. Repeated range-validation shape across 4 `SettingsViewModel` properties — [DONE]
- File: `Features/Settings/ViewModels/SettingsViewModel.cs:374-421, 466-482`.
- Fix: Add a shared `TryValidateRange(value, min, max, errorMessage)` helper.
- **Done:** Private `TryValidateRange` helper added; all four `OnXChanged` handlers refactored. Messages/behavior unchanged.

### 12. Skill-tag parsing duplicated across 3 computed properties instead of a `Core/Formatting` unit — [DONE]
- File: `Features/Projects/ViewModels/ProjectCardViewModel.cs:173-202`.
- Fix: Extract a `SkillsFormatter` in `Core/Formatting/`, consumed by `SkillTags`/`SkillsDisplay`/`SkillItems`.
- **Done:** `Core/Formatting/SkillsFormatter.cs` (`ParseTags`/`FormatDisplay`); `ProjectCardViewModel` properties now delegate to it.

---

## Execution order (per plan Steps 3-6)
1. **Step 3**: `Core/Platform/CurrentPlatform.cs` + `Core/Platform/PlatformCapability.cs` (enables items #1, #2).
2. **Step 4**: Notification interfaces (#1) + tray icon capability mapping (#2) + fix ad hoc `#if WINDOWS` sites (#3, #4 as time allows).
3. **Step 5**: Real `ModalPresenter`/`ActionMenu` + `ConfirmationBox`/`ExitConfirmationBox` (#5, #6, #7).
4. **Step 6**: Remaining P1 items (#8-#12) by priority, plus regression verification.

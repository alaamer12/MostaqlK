# `#if WINDOWS`/`#if ANDROID`/`#if IOS` Audit — Live Re-scan (2026-08-18)

Full-repo `grep -rn "#if WINDOWS\|#if ANDROID\|#if IOS" --include=*.cs` re-run against the live tree. 32 raw hits (comment-text mentions of "#if WINDOWS" excluded from this count — see Clean section). Each real directive triaged below.

## Canonical — Exempt (1)
- `Core/Platform/CurrentPlatform.cs:31-36` — the `#if WINDOWS`/`#elif ANDROID`/`#elif IOS` ladder assigning `AppPlatform.Current`. This IS the one legal barrel; every other file must read from here instead of re-checking directives.

## Real Violations — body mixes >1 platform's concern in one file (5)

| # | File | Lines | Shape | Fix |
|---|---|---|---|---|
| 1 | `UI/PlatformConcepts/ModalPresenter.cs` | 38-41, 58-117 | `ConfirmationOptions.DefaultButton` and `ShowConfirmationAsync` fully duplicated across `#if WINDOWS ... #else ... #endif` | Split into `ModalPresenter.cs` (shared shell, `partial`) + `ModalPresenter.Windows.cs` (real `ContentDialog`) + `ModalPresenter.Android.cs`/`.iOS.cs` (today's stub, shared via `_ModalPresenter.Mobile.cs`) |
| 2 | `UI/DesignSystem/ConfirmationBox.cs` | 21-71 | `ShowAsync`/`TryGetActiveNativeWindow` each fully duplicated across `#if WINDOWS`/`#else` | Same 3/4-way split as ModalPresenter |
| 3 | `UI/DesignSystem/ExitConfirmationBox.cs` | 20-24 | Method *signature itself* switches by `#if WINDOWS` (parameter type `Microsoft.UI.Xaml.Window` vs `object?`) | Same split; shared shell keeps a platform-neutral parameter type |
| 4 | `App.xaml.cs` | 39-41, 59-69, 282-284, 302-304, 332-334 | 5 separate inline `#if WINDOWS` blocks in the composition-root constructor/window-sizing methods — each currently a single call into an already-Windows-only class (`AppWindowMetrics`, `WindowsToastSender.EnsureRegisteredEagerly`) | Route every block through one resolved `IPlatformStartupHooks` instance instead of 5 separate inline `#if` blocks |
| 5 | `MauiProgram.cs` | 22-24, 59-61, 109-113, 183-357, 369-371 | A conditional `using`, a scrollbar-suppression call, an `INotificationSender` DI registration line, **one large ~175-line `ConfigureLifecycleEvents` block** (title bar, tray icon wiring, exit-confirmation, close-to-tray), and an `appRef` assignment — all inline `#if WINDOWS` in the composition root. The `ConfigureLifecycleEvents` block is the single biggest structural risk in the whole audit: it is not "a single line call into a Windows-only class", it IS the Windows-only logic, written inline. | Extract the entire lifecycle-wiring body into `Platforms/Windows/WindowsStartupHooks.cs` (implements `IPlatformStartupHooks`); `MauiProgram.cs` calls `_startupHooks?.ConfigureLifecycle(builder, app-accessor)` once |

## Borderline — whole file guarded, no `#else`, just needs the `.Windows.cs` suffix (5)
- `Infrastructure/Notifications/WindowsToastSender.cs`
- `Infrastructure/Notifications/ToastActivator.cs`
- `Infrastructure/Notifications/ToastAumidRegistrar.cs`
- `Infrastructure/Notifications/WinRtVariation.cs`
- `Infrastructure/Notifications/WinAppSdkVariation.cs`

No mixed-concern bug (nothing to "fix" behaviorally), but the filename doesn't yet carry the meaning the rest of the codebase now uses. Lowest priority — rename-only, remove the now-redundant whole-file `#if WINDOWS`/`#endif` wrapper since the suffix conveys that meaning instead.

## Already Clean — comment-only mentions, confirmed by opening the file (7)
- `Core/Platform/PlatformCapability.cs` (doc-comment mentions `#if WINDOWS` as prose, no directive)
- `Features/Projects/ViewModels/ProjectDetailsViewModel.cs:69` (comment says "no ad hoc `#if WINDOWS`")
- `Services/IFileRevealService.cs` (doc comment)
- `UI/DesignSystem/EnrichmentShimmerOverlay.cs` (doc comment)
- `UI/PlatformComponents/AppIcon/AppIcon.cs` (doc comment)
- `UI/PlatformComponents/MotionPreferences.cs` (doc comment — real logic already lives in `MotionPreferences.Windows.cs`)
- `UI/PlatformComponents/PlatformSelect.cs` (doc comment)
- `UI/DesignSystem/PressableEffect.Windows.cs` (filename itself is `.Windows.cs` — a real directive here would be redundant, and grep shows only a comment)
- `Platforms/Windows/PlatformServiceRegistration.cs` (lives under `Platforms/Windows/` already — a comment mention only)

## Priority order for fixes (biggest structural risk first)
1. `ModalPresenter.cs` / `ConfirmationBox.cs` / `ExitConfirmationBox.cs` (multi-member classes, 3 files, one pattern)
2. `MauiProgram.cs`'s `ConfigureLifecycleEvents` block (biggest single violation) + its smaller inline blocks, via `IPlatformStartupHooks`
3. `App.xaml.cs`'s 5 smaller inline blocks, via the same `IPlatformStartupHooks`
4. Notification file renames (cosmetic, no behavior risk)

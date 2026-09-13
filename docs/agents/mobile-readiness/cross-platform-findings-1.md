# Cross-Platform Findings — Agent 1 (Infrastructure/*, Services/*, Platforms/Windows/*)

Audit for Android/mobile readiness. Findings are appended incrementally as discovered.

## Finding 1: `WindowsToastSender` registered directly, no interface boundary
- File: MauiProgram.cs:214, Services/NotificationDispatcher.cs:13,38
- Description: `builder.Services.AddSingleton<WindowsToastSender>()` is registered unconditionally (no `#if WINDOWS` guard) in shared `MauiProgram.cs`, and `NotificationDispatcher` (a platform-neutral `Services/` class) takes a hard constructor dependency on the concrete `WindowsToastSender` type instead of an abstraction. There is no mobile equivalent path — on Android this either fails to resolve/compile or silently pulls in Windows-only notification code.
- Suggested fix: introduce `INotificationSender` (or similar) interface in `Services/`, move `WindowsToastSender` implementation-detail wiring behind `#if WINDOWS` in `MauiProgram.cs`, and have `NotificationDispatcher` depend only on the interface.

## Finding 2: `ToastActivator` COM/WinRT activation code lives outside `Platforms/Windows/` — likely Android build break
- File: Infrastructure/Notifications/ToastActivator.cs:1-206
- Description: Entire file is Win32 COM interop (`CoRegisterClassObject`, `INotificationActivationCallback`, `ComImport`) with zero `#if WINDOWS` guards, sitting in the platform-neutral `Infrastructure/Notifications/` folder rather than `Platforms/Windows/` or behind a `.Windows.cs` partial as `structure.md` prescribes ("Put Windows- or Android-only code behind interfaces and implementations under `Platforms/`"). Since `MostaqlK.csproj` already lists `net10.0-android` in `TargetFrameworks` and this folder is not under `Platforms/`, MSBuild's implicit platform-folder exclusion does not apply — this file is a real compile-time risk for the Android target, not just a design smell. It has no mobile counterpart/interface at all — it is the "activation" half of the two known Windows-only notification code paths named in the audit brief.
- Suggested fix: move to `Platforms/Windows/Notifications/ToastActivator.cs`, guard with `#if WINDOWS`, and expose activation through a shared `INotificationActivationHandler`/similar contract that `NotificationDispatcher` can resolve per platform.

## Finding 3: `ToastAumidRegistrar` Win32 registry/shell COM code lives outside `Platforms/Windows/` — likely Android build break
- File: Infrastructure/Notifications/ToastAumidRegistrar.cs:1-259
- Description: Uses `Microsoft.Win32.Registry`, raw `IShellLinkW`/`IPropertyStore` COM interop, and `shell32.dll` P/Invoke unconditionally, with no `#if WINDOWS` guard, in the shared `Infrastructure/Notifications/` folder (not under `Platforms/`, so it is not implicitly excluded from the `net10.0-android` target already declared in `MostaqlK.csproj`). This is AUMID/shortcut plumbing that has no Android equivalent whatsoever (Android has no AUMID/shortcut concept), so it should not be reachable from a build targeting mobile at all.
- Suggested fix: relocate under `Platforms/Windows/` (or `.Windows.cs` partial) and guard with `#if WINDOWS`; called only from `WindowsToastSender`'s Windows-only registration path.

## Finding 4: `WinRtVariation`/`WinAppSdkVariation` reference WinRT/App SDK types with no `#if WINDOWS` guard
- File: Infrastructure/Notifications/WinRtVariation.cs:1-2, Infrastructure/Notifications/WinAppSdkVariation.cs:1-2
- Description: `WinRtVariation.cs` imports `Windows.UI.Notifications`/`Windows.Data.Xml.Dom` and `WinAppSdkVariation.cs` imports `Microsoft.Windows.AppNotifications(.Builder)` at the top of otherwise-shared `Infrastructure/Notifications/` files, again outside `Platforms/Windows/` and without `#if WINDOWS`. Both implement `IToastVariation` (a reasonable interface boundary already exists via `IToastVariation`/`WindowsToastSender`'s dual-variation dispatch), but the concrete classes themselves are not isolated to a Windows-only compilation unit, so they carry the same Android-target compile risk as Finding 2/3.
- Suggested fix: keep `IToastVariation` in `Infrastructure/Notifications/` (interface is fine platform-neutral), but move `WinRtVariation`/`WinAppSdkVariation`/`ToastActivator`/`ToastAumidRegistrar` together under `Platforms/Windows/Notifications/`, guarded by `#if WINDOWS`; `WindowsToastSender` itself should be renamed/relocated per Finding 1 or resolve its variations via a Windows-only factory registered from `MauiProgram.cs`'s existing `#if WINDOWS` block.

## Summary

- Finding 1: `WindowsToastSender` registered directly, no interface boundary (`MauiProgram.cs`, `Services/NotificationDispatcher.cs`)
- Finding 2: `ToastActivator` COM/WinRT activation code lives outside `Platforms/Windows/` — likely Android build break (`Infrastructure/Notifications/ToastActivator.cs`)
- Finding 3: `ToastAumidRegistrar` Win32 registry/shell COM code lives outside `Platforms/Windows/` — likely Android build break (`Infrastructure/Notifications/ToastAumidRegistrar.cs`)
- Finding 4: `WinRtVariation`/`WinAppSdkVariation` reference WinRT/App SDK types with no `#if WINDOWS` guard (`Infrastructure/Notifications/WinRtVariation.cs`, `Infrastructure/Notifications/WinAppSdkVariation.cs`)

Not flagged (verified fine): `SecretProtector` (runtime `OperatingSystem.IsWindows()` checks with a genuine non-Windows AES-GCM fallback), `CloseBehaviorService`/`AppLifecycleService` (pure `Preferences`-backed, platform-neutral), `TrayIconNativeHost`/`ConfirmationDialog`/`CloseConfirmationDialog`/`Program.cs`/`App.xaml.cs` under `Platforms/Windows/` (correctly isolated), `TrayIconService` (`UI/TrayIcon/`, platform-neutral state/menu holder; native Win32 tray plumbing correctly isolated to `Platforms/Windows/TrayIconNativeHost.cs` and only constructed inside `MauiProgram.cs`'s `#if WINDOWS` block), and the rest of `Services/Pipeline/*` and `Infrastructure/Database|Http/*` (verified platform-neutral, no Windows-only APIs).


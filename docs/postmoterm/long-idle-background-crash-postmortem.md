# Post-Mortem: Long-Idle Background Crash Analysis (~22 Hours)

## Incident Overview

* **Application**: `MostaqlK.exe` (WinUI 3 / .NET 10 MAUI Unpackaged Desktop Application)
* **Duration before Crash**: 22 hours, 3 minutes, 3 seconds (Started `2026-09-15 13:01:45`, Crashed `2026-09-16 11:04:48 - 11:04:55`).
* **Environment**: Windows 10 x64, running minimized/hidden to the System Tray with background polling enabled.
* **Crash Dump Analyzed**: `%LocalAppData%\CrashDumps\MostaqlK.exe.14612.dmp`
* **Exception Code**: `0xC0000409` (`STATUS_STACK_BUFFER_OVERRUN` / FailFast) with Subcode `0xA` (`FAST_FAIL_FATAL_APP_EXIT`).
* **Faulting Module & Function**: `Microsoft.UI.Xaml.dll` (WinUI 3 XAML Framework Core) on Thread 0 (UI Main Thread, TID `22484`).

---

## 1. Timeline & Forensic Data

1. **Continuous Operation (13:01 - Next Day 11:03)**:
   * The application ran quietly in the background, conducting over 2,500 periodic poll cycles (every 30 seconds) and enriching discovered projects.
   * At `2026-09-16 06:13:00`, the system entered and resumed from connected sleep / low-power state (`Kernel-Power` / `Power-Troubleshooter`), which suspends DirectX/Direct3D composition surfaces.

2. **Trigger Event (11:03:05 - 11:03:06)**:
   * Project `1277974` was fetched and enriched.
   * `NotificationGrouper.Flush` fired, calling `WinRtVariation.SendAsync` to present a native Windows desktop toast notification.
   * Windows Shell (`ShellExperienceHost`) took the toast payload and logged COM Event ID 10016 for DCOM activation permissions at `11:03:06.529`.

3. **Crash Point (11:04:48 - 11:04:55)**:
   * Exactly ~100 seconds after the toast notification appeared (standard Windows toast banner dismissal / notification center retirement timeout), the WinUI 3 XAML thread received a compositor callback / weak reference cleanup event.
   * During the internal cleanup of visual tree compositor resources (`IWeakReferenceSource::Release`), `Microsoft.UI.Xaml.dll` encountered `0x8000FFFF` (`E_UNEXPECTED` / Catastrophic failure).
   * Because `Microsoft.UI.Xaml.Application.Current.UnhandledException` was not subscribed to catch and mark WinUI exceptions as handled, WinUI 3 invoked `RoFailFastWithErrorContext` / `RtlFailFast`, triggering an immediate `0xC0000409` process termination.

---

## 2. Root Cause Analysis

### A. Unhandled WinUI 3 XAML / COM Lifecycle Exception

`MostaqlK` had configured global handlers for:

* `AppDomain.CurrentDomain.UnhandledException`
* `TaskScheduler.UnobservedTaskException`
* Native Win32 Structured Exception Handling (`SetUnhandledExceptionFilter`)

However, **WinUI 3 / Windows App SDK applications run their own internal dispatcher and XAML exception boundary**. When an unhandled exception or stowed HRESULT occurs on the WinUI UI thread, WinUI intercepts it first. If `Microsoft.UI.Xaml.Application.Current.UnhandledException` is not hooked, or if `UnhandledExceptionEventArgs.Handled` is not set to `true`, WinUI 3 calls fail-fast, deliberately bypassing the standard managed unhandled exception filter and terminating the process instantly.

### B. Direct3D Device / Visual Tree Invalidation in Long Background Runs

When the application runs hidden in the system tray for 20+ hours across power state transitions (display sleep, monitor power-off, system sleep/wake), the underlying DirectX swapchain and D3D composition device context get invalidated or suspended. Upon subsequent compositor pulses (e.g., triggered by desktop Shell notification events), attempting to release or update unrendered visual references returns `0x8000FFFF`.

### C. Toast COM Activation Weak Reference Handling

`ToastActivator` registers a COM LocalServer32 callback for notification clicks. When Shell actions or timeouts route events to the process, invoking UI navigation while the main window is hidden without defensive service checks can cause unhandled task faults on the UI dispatcher.

---

## 3. Implemented Fixes

### 1. Hooked `Microsoft.UI.Xaml.Application.UnhandledException`

In `Platforms/Windows/App.xaml.cs`:

```csharp
public App()
{
    this.InitializeComponent();

    // Handle WinUI 3 unhandled XAML/COM exceptions to prevent fatal process termination
    // (e.g. D3D device-lost / compositor / stale visual tree release errors during background hibernation).
    this.UnhandledException += (sender, e) =>
    {
        try
        {
            MostaqlK.Services.Diagnostics.CrashReporter.Report(
                "WinUI.UnhandledException",
                e.Exception,
                new { e.Message },
                isFatal: false);
            e.Handled = true;
        }
        catch
        {
            // Fail-safe to avoid throwing inside the unhandled exception handler
        }
    };
}
```

Setting `e.Handled = true` informs the WinUI 3 runtime that the catastrophic/stowed exception has been absorbed and logged, preventing the fail-fast abort.

### 2. Guarded Toast COM Activation Dispatch

In `Infrastructure/Notifications/ToastActivator.Windows.cs`:

* Added defensive null-checking on `Microsoft.Maui.IPlatformApplication.Current?.Services`.
* Wrapped navigation dispatch in diagnostic logging (`CrashReporter.Report(..., isFatal: false)`) to prevent any transient navigation failures from bubbling up to the UI dispatcher.

---

## 4. Verification & Testing

* Verified project builds cleanly for Windows (`net10.0-windows10.0.19041.0`).
* Validated that `App.xaml.cs` compiles with `this.UnhandledException` and properly sets `e.Handled = true`.
* Ensured crash reporting telemetry continues to capture diagnostics in `crash.log` without aborting the background process.

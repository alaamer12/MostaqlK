# Windows Single-File Bundle Eviction, Native SEH Crashes & Sentry Telemetry

## Technical Guide & Retrospective

---

### Executive Summary

When running long-lived background desktop applications on Windows built with **.NET MAUI / WinUI 3 (Windows App SDK)** packaged as a single-file portable executable (`<PublishSingleFile>true</PublishSingleFile>`), the application is vulnerable to silent native crashes after extended idle time (~10+ hours) if native assets are extracted into default temporary storage (`%TEMP%`).

Furthermore, because these crashes originate as **native unmanaged Win32 Structured Exception Handling (SEH) faults** (`0xC0000005` Access Violation in `Microsoft.UI.Xaml.dll`), standard managed exception hooks (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) and crash-reporting SDKs (such as **Sentry**) do not capture them in real time.

This document details the exact root cause, operating system mechanisms, failure lifecycle, and the multi-layer architectural solution implemented in MostaqlK to permanently eliminate this vulnerability and capture any future unmanaged native crashes.

---

### 1. Problem Statement & Crash Anatomy

#### The Incident
* **Runtime Duration:** The application operated in background tray mode successfully for ~10 hours, polling endpoints and dispatching Windows notifications.
* **Trigger Event:** At `08:02:25.904`, the user clicked the system tray icon to restore the application window (`AppLifecycle.IsInBackgroundChanged -> IsInBackground: false`).
* **Failure Sequence:**
  1. WinUI 3 attempted to re-hydrate the visual tree and load native XAML resources.
  2. At `08:02:26.689`, a `System.IO.FileNotFoundException` was raised internally when accessing extracted single-file bundle assets.
  3. At `08:02:27.239`, native `Microsoft.UI.Xaml.dll` encountered an unhandled Access Violation (`0xC0000005` at fault offset `0x0000000000940f4d`).
  4. The Windows OS kernel terminated the process instantly.
  5. Sentry recorded zero crash events, and `crash.log` showed no managed exception entry for that timestamp.

#### Windows Application Event Log Record
```text
Log Name:      Application
Source:        Application Error
Date:          2026-09-05T08:02:27.2390000Z
Event ID:      1000
Task Category: Application Crashing Events
Level:         Error
Faulting application name: MostaqlK.exe, version: 1.0.4.0
Faulting module name:      Microsoft.UI.Xaml.dll, version: 3.1.7.0
Exception code:            0xc0000005
Fault offset:              0x0000000000940f4d
Faulting process id:       0xc98
Faulting application path: F:\Projects\Mobile\C#\MostaqlK\bin\Release\...\MostaqlK.exe
Faulting module path:      C:\Users\<user>\AppData\Local\Temp\.net\MostaqlK\9fUI4XvOp5uG\Microsoft.UI.Xaml.dll
```

---

### 2. Deep-Dive Root Cause Analysis

```
[Background Tray Operation (~10+ Hours)]
   │
   ├── 1. Memory Management: Windows pages out inactive DLL code pages from physical RAM
   │
   ├── 2. Temp Maintenance: Windows Storage Sense / Disk Cleaners sweep %TEMP%
   │   └── Bundle folder invalidated: %LocalAppData%\Temp\.net\MostaqlK\<hash>\
   │
[User Restores Window from Tray]
   │
   ├── 3. WinUI 3 re-activates window & visual tree
   ├── 4. Page fault occurs -> Disk read fails on missing DLL/PRI file (System.IO.FileNotFoundException)
   └── 5. Native Microsoft.UI.Xaml.dll hits 0xC0000005 (Access Violation) -> OS Hard Kill
```

#### A. .NET Single-File Extraction Mechanism
In .NET single-file publish mode (`PublishSingleFile=true`), managed assemblies execute directly from memory, but native C/C++ binaries (`Microsoft.UI.Xaml.dll`, `Microsoft.WindowsAppRuntime.dll`, `resources.pri`, DirectX/D3D dependencies) cannot be loaded directly from memory by the Win32 OS loader. The .NET single-file host extracts them to disk on startup.

By default, the extraction destination is:
```text
%TEMP%\.net\<AppName>\<bundle-hash>\
(e.g., C:\Users\<user>\AppData\Local\Temp\.net\MostaqlK\9fUI4XvOp5uG\)
```

#### B. OS Temp Invalidation & Virtual Memory Page Eviction
1. **Windows Storage Sense & Disk Cleanup:** Windows runs automated background disk maintenance tasks targeting `%TEMP%` when files have not had active read/write file handle operations for several hours.
2. **Virtual Memory Paging:** While MostaqlK was running minimized to the tray, Windows reduced its working set by paging out inactive code and resource pages from physical RAM.
3. **Hard Page Fault on Wakeup:** When the tray icon was clicked, the UI thread resumed execution in `Microsoft.UI.Xaml.dll`. When the CPU attempted to access code/resource pages that were evicted from RAM, the OS attempted to read them back from the disk location. Because the `%TEMP%` bundle files had been purged or locked by OS maintenance, the file read failed (`FileNotFoundException`), leading immediately to an invalid memory pointer dereference (`0xC0000005` Access Violation).

#### C. Why Sentry & CLR Handlers Did Not Capture It
* **CLR vs. Native SEH Pipeline:** .NET exception handlers (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, Sentry .NET SDK) only intercept managed exceptions that traverse the CLR execution engine.
* **Unmanaged Kernel Termination:** Native C++ faults occurring inside unmanaged DLLs (`Microsoft.UI.Xaml.dll`) trigger Windows Structured Exception Handling (SEH). If native SEH handlers are not registered, the Windows kernel aborts the process immediately, killing all threads without running managed CLR finalizers, Sentry dispatch queues, or managed `catch` blocks.

---

### 3. The Complete Solution Architecture

To solve both the bundle eviction and the crash visibility issues, a 4-layer resilient architecture was implemented:

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              MostaqlK Architecture                          │
├──────────────────────────────────────────────────────────────────────────────┤
│  Layer 1: Persistent Bundle Cache                                            │
│  - DOTNET_BUNDLE_EXTRACT_BASE_DIR = %LocalAppData%\MostaqlK\bundle-cache\    │
│  - Immune to Windows Storage Sense and Temp Cleaners                         │
├──────────────────────────────────────────────────────────────────────────────┤
│  Layer 2: Native Win32 SEH Filter & MiniDump Engine                          │
│  - SetUnhandledExceptionFilter intercepts native 0xC0000005 / SEH crashes    │
│  - MiniDumpWriteDump writes crash.dmp synchronously                          │
│  - Synchronous native breadcrumb written directly to crash.log               │
├──────────────────────────────────────────────────────────────────────────────┤
│  Layer 3: Post-Mortem Sentry Reporting                                       │
│  - Next startup detects crash.dmp                                            │
│  - Submits Sentry fatal incident with dump metadata & crash.log attachment   │
│  - Rotates dump to crash-yyyyMMdd-HHmmss.dmp                                 │
├──────────────────────────────────────────────────────────────────────────────┤
│  Layer 4: Hardened Window & Tray Restoration                                 │
│  - RestoreRequested wrapped in defensive try/catch logging                   │
│  - Protects against transient XAML visual tree hydration errors             │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

### 4. Implementation Details

#### Layer 1: Persistent Bundle Extraction Cache
**Files:** `Core/Platform/AppPaths.cs`, `Platforms/Windows/Program.cs`

Instead of relying on volatile `%TEMP%`, single-file extraction is redirected to the application's persistent local directory:

```csharp
// AppPaths.cs
public static string BundleCacheDirectory
{
    get
    {
        var dir = Path.Combine(AppDirectory, "bundle-cache");
        EnsureDirectoryExists(dir);
        return dir;
    }
}
```

In `Program.cs`, before any native or WinRT subsystems initialize:
```csharp
private static void EnsurePersistentBundleExtraction()
{
    try
    {
        var bundleCacheDir = MostaqlK.Core.Platform.AppPaths.BundleCacheDirectory;
        // Set for process
        Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", bundleCacheDir, EnvironmentVariableTarget.Process);

        // Set in User environment for persistent cross-restart child processes
        var currentVal = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", EnvironmentVariableTarget.User);
        if (!string.Equals(currentVal, bundleCacheDir, StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", bundleCacheDir, EnvironmentVariableTarget.User);
        }
    }
    catch { }
}
```

* **Guarantee:** Windows Storage Sense **never** sweeps `%LocalAppData%\MostaqlK\`, ensuring extracted native DLLs and XAML assets remain permanently available regardless of uptime.

---

#### Layer 2: Native Win32 SEH & MiniDump Generation
**File:** `Services/Diagnostics/CrashReporter.cs`

`CrashReporter` registers a top-level Win32 unhandled exception filter using P/Invoke:

```csharp
#if WINDOWS || NET10_0_WINDOWS10_0_19041_0_OR_GREATER
[DllImport("kernel32.dll", SetLastError = true)]
private static extern IntPtr SetUnhandledExceptionFilter(UnhandledExceptionFilterDelegate lpTopLevelExceptionFilter);

[DllImport("dbghelp.dll", SetLastError = true)]
private static extern bool MiniDumpWriteDump(
    IntPtr hProcess,
    uint processId,
    SafeFileHandle hFile,
    uint dumpType,
    ref MINIDUMP_EXCEPTION_INFORMATION exceptionParam,
    IntPtr userStreamParam,
    IntPtr callbackParam);
#endif
```

When a native crash occurs:
1. `NativeUnhandledExceptionCallback` extracts the exception code (e.g., `0xC0000005 STATUS_ACCESS_VIOLATION`), faulting memory address, thread ID, and memory statistics.
2. Synchronously writes a structured entry to `crash.log`.
3. Calls `MiniDumpWriteDump` to write `crash.dmp` to `%LocalAppData%\MostaqlK\log\crash.dmp`.
4. Returns `EXCEPTION_CONTINUE_SEARCH` so Windows Error Reporting and OS crash dumps remain intact.

---

#### Layer 3: Startup Post-Mortem Telemetry to Sentry
**Files:** `Services/Diagnostics/CrashReporter.cs`, `Platforms/Windows/Program.cs`

Because native crashes prevent in-flight network dispatch during termination, crash telemetry is reported on next launch:

```csharp
public static void CheckAndReportPreviousCrashes()
{
    var dumpPath = AppPaths.CrashDumpFilePath;
    if (!File.Exists(dumpPath)) return;

    var fileInfo = new FileInfo(dumpPath);
    var dumpSizeKb = Math.Max(1, fileInfo.Length / 1024);
    var crashTime = fileInfo.LastWriteTimeUtc;

    // 1. Log locally to crash.log
    Report("PostMortem.NativeCrashDetected", ...);

    // 2. Dispatch fatal event with crash.log to Sentry
    if (Sentry.SentrySdk.IsEnabled)
    {
        Sentry.SentrySdk.CaptureMessage(
            $"Previous session terminated with native SEH crash (Minidump: {dumpSizeKb} KB)",
            scope =>
            {
                scope.Level = Sentry.SentryLevel.Fatal;
                scope.SetTag("crash_type", "native_seh");
                scope.SetTag("post_mortem", "true");
                scope.SetExtra("dump_size_kb", dumpSizeKb);
                if (File.Exists(AppPaths.CrashLogFilePath))
                {
                    scope.AddAttachment(AppPaths.CrashLogFilePath);
                }
            });
    }

    // 3. Rotate dump file to prevent duplicate reporting
    var archivePath = Path.Combine(AppPaths.LogsDirectory, $"crash-{crashTime:yyyyMMdd-HHmmss}.dmp");
    File.Move(dumpPath, archivePath);
}
```

---

#### Layer 4: Hardened Window & Tray Restoration
**File:** `Platforms/Windows/PlatformServiceRegistration.cs`

Tray window restore events (`RestoreRequested`) are wrapped in defensive try-catch handlers to catch and report transient COM/WinUI visual tree exceptions without bringing down the application:

```csharp
trayIconService.RestoreRequested += () =>
    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
    {
        try
        {
            appWindow.Show();
            window.Activate();
            appLifecycleService.IsInBackground = false;
        }
        catch (Exception ex)
        {
            CrashReporter.Report("PlatformServiceRegistration.RestoreRequested", ex, isFatal: false);
        }
    });
```

---

### 5. Developer & Release Guidelines

To avoid re-introducing single-file eviction or silent native crash regressions in future updates, adhere to the following rules:

1. **Do Not Rely on `%TEMP%` for Native Extracted Binaries:**
   Always ensure `DOTNET_BUNDLE_EXTRACT_BASE_DIR` points to `AppPaths.BundleCacheDirectory`. Never remove `EnsurePersistentBundleExtraction()` from `Program.Main`.

2. **Maintain Required MSBuild Properties in `MostaqlK.csproj`:**
   For Windows single-file packaging, the following properties are mandatory:
   ```xml
   <PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows' and '$(PublishSingleFile)' == 'true'">
       <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
       <EnableMsixTooling>true</EnableMsixTooling>
       <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
       <WindowsAppSdkUndockedRegFreeWinRTInitialize>true</WindowsAppSdkUndockedRegFreeWinRTInitialize>
   </PropertyGroup>
   ```

3. **Dual Deployment Support:**
   * **Portable Mode (`MostaqlK.exe` single-file):** Supported via persistent bundle extraction cache.
   * **Installer / Directory Mode (`PublishSingleFile=false` MSI/Loose):** Native DLLs live in the installation directory, requiring no extraction. Both deployment models are fully supported.

4. **Debugging Native Crashes:**
   * Look in `%LocalAppData%\MostaqlK\log\crash.log` for `[FATAL/NATIVE_SEH_CRASH]` blocks.
   * Check `%LocalAppData%\MostaqlK\log\crash-*.dmp` using WinDbg or Visual Studio (`Open File -> Dump File -> Debug with Native Only`) to inspect thread stacks and registers at the exact moment of the fault.

# Windows Single-File Bundle Eviction, Native SEH Crashes & Sentry Telemetry

## Technical Guide & Retrospective

---

### Executive Summary

When running long-lived background desktop applications on Windows built with **.NET MAUI / WinUI 3 (Windows App SDK)** packaged as a single-file portable executable (`<PublishSingleFile>true</PublishSingleFile>`), the application is vulnerable to silent native crashes after extended idle time (~10+ hours) if native assets are extracted into default temporary storage (`%TEMP%`).

> **CORRECTION (2026-09-13) — read before trusting anything below.** This document's fix **did not stop the crash**, and two of its central claims are wrong: (1) the memory-mapped `Microsoft.UI.Xaml.dll` **cannot** have been deleted out from under a running process (§2a), and (2) setting `DOTNET_BUNDLE_EXTRACT_BASE_DIR` from inside `Program.Main` **cannot** redirect the current process's extraction directory (§2b), so every session kept extracting to `%TEMP%`. The real eviction surface, and the fix that now addresses it, are documented in **[§2a](#2a-what-actually-gets-evicted)** and **[§2b](#2b-why-the-original-fix-could-not-work)**.

Furthermore, because these crashes originate as **native unmanaged Win32 Structured Exception Handling (SEH) faults** (`0xC0000005` Access Violation in `Microsoft.UI.Xaml.dll`), standard managed exception hooks (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) and crash-reporting SDKs (such as **Sentry**) do not capture them in real time.

This document details the failure lifecycle and the multi-layer architecture implemented at the time. The **crash-visibility** half (Layers 2–3: native SEH filter, mini-dump, post-mortem telemetry) is sound and still in force. The **eviction** half (Layer 1) is not — see the correction above and §2a/§2b.

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

#### 2a. What actually gets evicted

The sequence above — *sweeper deletes the running `Microsoft.UI.Xaml.dll`, then a page fault on wake reads it back and fails* — **cannot happen**. A loaded image is backed by a memory section the OS holds open for the lifetime of the mapping; `DeleteFile`/`MoveFileEx` against it fails with `ERROR_ACCESS_DENIED`/`ERROR_SHARING_VIOLATION` until the last mapping closes. No cleaner, including Storage Sense, can unlink a DLL a process is executing.

The crash record proves the same thing from the other side: WER resolved and printed both `Faulting module path` **and** the module `version: 3.1.7.0`. A file that had already been deleted-and-closed cannot be named or versioned like that. `Microsoft.UI.Xaml.dll` was present and intact when it faulted.

What the eviction theory *almost* got right, and where the real exposure is: `IncludeAllContentForSelfExtract=true` writes **every** bundle entry to disk as a loose file, not just native images. Files the OS loader maps inherit that protection; files opened through ordinary file APIs do not. That leaves `resources.pri`, `*.mui`, `*.ttf` and the app's `icon_*.scale-200.png` assets — exactly the class this app reads **by absolute path at render time** (`ImageSource.FromFile(Path.Combine(AppContext.BaseDirectory, "icon_*.scale-200.png"))`, see `AppIconGlyphExtensions.cs`). A sweeper can delete those mid-session, and the resulting `FileNotFoundException` surfacing inside the XAML render path is a consistent match for the observed `0xC0000005` in `Microsoft.UI.Xaml.dll` — the DLL is where the missing file was noticed, not what went missing.

> The correction is deliberately **file-agnostic**: the log never proved *which* asset class was evicted, so the fix protects the whole directory rather than guessing at one file type.

#### 2b. Why the original fix could not work

`Layer 1` (below) never protected the running process, which is precisely why the crash reproduced.

.NET single-file extraction is performed by the **apphost** — the native `MostaqlK.exe` stub — *before the managed entry point runs*. By the time `Program.Main` executes, the bundle is already on disk and `AppContext.BaseDirectory` already points at it. So:

* `Environment.SetEnvironmentVariable(..., EnvironmentVariableTarget.Process)` is **inert** for the current process. There is no ordering of `Program.Main` that fixes this — the apphost always wins.
* The `EnvironmentVariableTarget.User` write is not inert, but it only takes effect for **subsequently launched** processes. At best it redirects the *next* launch; the process that wrote it kept running out of `%TEMP%`.

Net effect: before this correction, every session still extracted into `%TEMP%`, and nothing in the running process held those files open. `Platforms/Windows/BundleExtractionGuard.cs` is what now covers a live process — see §3 Layer 1 and §4.

The instrument that settles which directory a given machine actually used is the `Module file path:` line written to `%LocalAppData%\MostaqlK\log\startup-debug.log` by `Platforms/Windows/Program.cs`; the guard logs `BundleExtractionGuard pinned N file(s)` immediately beside it.

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
│  Layer 1: Persistent dir (later launches) + live handle pinning              │
│  - User-scope DOTNET_BUNDLE_EXTRACT_BASE_DIR -> %LocalAppData%\MostaqlK\     │
│    (the apphost already extracted this process; env applies to the next run) │
│  - BundleExtractionGuard: FileShare.Read handles over every extracted file,  │
│    so deleting a live session's assets fails with ERROR_SHARING_VIOLATION    │
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

#### Layer 1: Persistent Extraction Directory + Live Handle Pinning
**Files:** `Core/Platform/AppPaths.cs`, `Platforms/Windows/Program.cs`, `Platforms/Windows/BundleExtractionGuard.cs`

**(a) Redirecting later launches.** `Program.Main` still records the persistent directory in the **User** environment:

```csharp
private static void EnsurePersistentBundleExtraction()
{
    var bundleCacheDir = MostaqlK.Core.Platform.AppPaths.BundleCacheDirectory;

    // User scope only, and only useful for the NEXT process: the apphost that launched
    // this one already extracted the bundle before Main ran (see §2b).
    var currentVal = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", EnvironmentVariableTarget.User);
    if (!string.Equals(currentVal, bundleCacheDir, StringComparison.OrdinalIgnoreCase))
    {
        Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", bundleCacheDir, EnvironmentVariableTarget.User);
    }
}
```

The previous `EnvironmentVariableTarget.Process` write has been **removed** — it could never affect the process that executed it, and leaving it in place read as if the current session were protected when it was not.

**(b) Protecting the running process.** `InitializeWindowsAppRuntime` first resolves the physical path of `Microsoft.WindowsAppRuntime.dll` with `GetModuleFileName`, then calls `BundleExtractionGuard.PinDirectory(Path.GetDirectoryName(dllPath)!)` before subsequent WinRT/XAML initialization. That parent directory is the actual single-file extraction tree; `AppContext.BaseDirectory` is the portable EXE directory and must not be used here. The guard opens a `FileStream` over every file in the real extraction directory:

```csharp
public static int PinDirectory(string directory)
{
    foreach (var path in Directory.EnumerateFiles(directory, "*", WalkOptions))
    {
        if (Pins.Count >= MaxPinnedFiles) break;
        try { Pins.Add(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)); }
        catch { /* Already open with incompatible sharing, or removed mid-enumeration. */ }
    }
    return Pins.Count;
}
```

`FileShare.Read` deliberately **omits** `FileShare.Delete`. Windows therefore opens those files without `FILE_SHARE_DELETE`, and any subsequent `DeleteFile`/`MoveFileEx` against them fails with `ERROR_SHARING_VIOLATION` for as long as the process lives. That is the same guarantee the loader already gives mapped DLLs, reproduced here for the loose data files that have no section backing them (§2a). Handles are never disposed or cleared — releasing one would unprotect its file.

Cost: one handle per extracted file (capped at `MaxPinnedFiles = 8192`), read-only, with no buffering. No packaging change, no new dependency, one-file release intact, and nothing touched in the render path that already works.

* **Guarantee (running process):** extraction-directory files cannot be deleted or moved while MostaqlK is alive, whatever sweeper runs.
* **Guarantee (later launches):** the extraction directory is `%LocalAppData%\MostaqlK\bundle-cache`, outside `%TEMP%` and therefore outside Storage Sense's default sweep scope.
* **No guarantee:** that a sweeper cannot delete the directory *between* runs. That would be a cold-start failure, not an hours-in crash, and surfaces differently (immediate missing-module at launch).

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

1. **Never treat `DOTNET_BUNDLE_EXTRACT_BASE_DIR` as crash protection for a live process:**
   The env var only steers *future* launches, because the apphost extracts before managed code runs (§2b). Do not re-add an `EnvironmentVariableTarget.Process` write, and do not remove the `BundleExtractionGuard.PinDirectory(Path.GetDirectoryName(dllPath)!)` call from `InitializeWindowsAppRuntime` immediately after `GetModuleFileName` resolves `Microsoft.WindowsAppRuntime.dll` — that call is the only thing protecting the running session. `AppContext.BaseDirectory` is the portable EXE directory, not the extraction tree. `EnsurePersistentBundleExtraction()` itself is still worth keeping, for the cold-start/next-launch benefit only.

2. **Confirm, don't assume, where a machine actually extracted:**
   Before theorising about eviction, read `%LocalAppData%\MostaqlK\log\startup-debug.log` for the `Module file path:` line (which directory that session really used) and the `BundleExtractionGuard pinned N file(s)` line (whether the guard ran, and how many files it holds). A `0xC0000005` naming a loaded DLL is never evidence that DLL was deleted (§2a) — check for a missing *loose data* file instead.

3. **Maintain Required MSBuild Properties in `MostaqlK.csproj`:**
   For Windows single-file packaging, the following properties are mandatory:
   ```xml
   <PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows' and '$(PublishSingleFile)' == 'true'">
       <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
       <EnableMsixTooling>true</EnableMsixTooling>
       <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
       <WindowsAppSdkUndockedRegFreeWinRTInitialize>true</WindowsAppSdkUndockedRegFreeWinRTInitialize>
   </PropertyGroup>
   ```

4. **Dual Deployment Support:**
   * **Portable Mode (`MostaqlK.exe` single-file):** Supported via live handle pinning of the extraction directory (Layer 1b) plus the persistent extraction directory for subsequent launches (Layer 1a).
   * **Installer / Directory Mode (`PublishSingleFile=false` MSI/Loose):** Native DLLs live in the installation directory, requiring no extraction. Both deployment models are fully supported.

5. **Debugging Native Crashes:**
   * Look in `%LocalAppData%\MostaqlK\log\crash.log` for `[FATAL/NATIVE_SEH_CRASH]` blocks.
   * Check `%LocalAppData%\MostaqlK\log\crash-*.dmp` using WinDbg or Visual Studio (`Open File -> Dump File -> Debug with Native Only`) to inspect thread stacks and registers at the exact moment of the fault.

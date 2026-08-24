# Windows Portable Single-File Release: Deep-Dive Root Cause Analysis & Technical Retrospective

## Executive Summary
This document provides a comprehensive technical retrospective on diagnosing, tracing, and resolving all build, packaging, runtime linking, and startup failures encountered while delivering a true standalone single-file portable Windows executable (`MostaqlK.exe`) for MostaqlK (.NET MAUI 10 WinUI 3 / Windows App SDK unpackaged).

---

## 1. Objectives & Scope
The target release specification required:
1. **Single Artifact Output**: Publishing with `-Type Portable` must produce strictly **one** standalone executable file (`MostaqlK.exe`) in the publish folder (`bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\`).
2. **Zero Companion Dependencies**: No companion `.dll`, `.pdb`, `.pri`, `.manifest`, or asset subdirectories (`ar`, `ar-SA`, `Microsoft.UI.Xaml`, `Resources`) beside the executable on disk.
3. **True Portability**: The executable must run anywhere (e.g. desktop, flash drive, isolated directory) without requiring pre-installed .NET runtimes, Windows App Runtime packages, or external assets.
4. **Instant Startup & UI Integrity**: Startup must provide instant visual feedback, initialize all native dependencies cleanly without silent crashes, and render the complete WinUI 3 onboarding window.

---

## 2. Comprehensive Trace of Bugs, Hypotheses & Solutions

### Phase 1: Multi-Targeting & Packaging Build Failures

#### Bug 1.1: NuGet Multi-Targeting Restore Failure (Error NU1102)
- **Symptom**:
  Executing `dotnet publish MostaqlK.csproj -f net10.0-windows10.0.19041.0 -c Release -r win-x64` failed during the restore phase:
  ```text
  error NU1102: Unable to find package Microsoft.NETCore.App.Runtime.Mono.win-x64 with version (= 10.0.3)
  ```
- **Hypothesis**:
  `MostaqlK.csproj` multi-targets four target frameworks in `<TargetFrameworks>` (`net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0`). When `-r win-x64` is passed on the command line, MSBuild's global restore pass applies the `win-x64` RID across *all* target frameworks defined in `<TargetFrameworks>`. Because Android and iOS use the Mono runtime pack (which has no `win-x64` package), restore breaks.
- **Root Cause**:
  Passing `-f <tfm>` only limits the *build/publish* targets, but global NuGet evaluation inspects the entire multi-targeting `<TargetFrameworks>` property.
- **Solution**:
  In `scripts/release-windows.ps1`, pass `-p:TargetFrameworks=$target` in addition to `-f $target`. This overrides `<TargetFrameworks>` during that execution so MSBuild evaluates strictly the Windows TFM.

#### Bug 1.2: Windows App SDK Single-File Verification Failure
- **Symptom**:
  Build failed in the `WindowsAppSDKSingleFileVerifyConfiguration` target with errors demanding specific unpackaged single-file settings.
- **Hypothesis**:
  Windows App SDK 1.5+ contains explicit MSBuild validation rules (`Microsoft.WindowsAppSDK.SingleFile.targets`) when `PublishSingleFile=true` is used for unpackaged apps.
- **Root Cause**:
  Unpackaged WinUI 3 single-file packaging requires:
  1. `<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>` to package native DLLs and manifests into the self-extracting bundle.
  2. `<EnableMsixTooling>true</EnableMsixTooling>` to generate and embed `resources.pri`.
  3. `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` to instruct WinAppSDK targets to embed the native C++ runtime.
- **Solution**:
  Added a dedicated conditional `<PropertyGroup>` in `MostaqlK.csproj`:
  ```xml
  <PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows' and '$(PublishSingleFile)' == 'true'">
      <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
      <EnableMsixTooling>true</EnableMsixTooling>
      <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
  </PropertyGroup>
  ```

---

### Phase 2: Eliminating Extraneous Artifacts and Debug Symbols

#### Problem & Symptoms
After resolving build errors, the output directory contained `MostaqlK.exe`, but also `MostaqlK.pdb` and leftover directories:
```text
bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\
├── MostaqlK.exe
├── MostaqlK.pdb
├── ar\
├── ar-SA\
├── Microsoft.UI.Xaml\
├── NpuDetect\
└── Resources\
```

#### Root Cause Analysis
1. `.NET` compiles with debug symbols enabled by default in Release mode unless explicitly turned off.
2. WinUI 3 and .NET MAUI build tasks stage intermediate assets and localized satellite files into the publish folder before single-file aggregation. The single-file bundler extracts/bundles the files into the executable but leaves empty directory structures behind.

#### Solution
1. Configured `-p:DebugType=None` and `-p:DebugSymbols=false` for Portable mode in `scripts/release-windows.ps1`.
2. Added post-publish directory sanitization in `scripts/release-windows.ps1`:
   - Deletes any leftover `.pdb` files.
   - Recursively deletes empty folders.
   - Asserts that strictly `MostaqlK.exe` exists in `$outputBase`.

---

### Phase 3: Silent Runtime Crash & Native Startup Failure

#### Problem Statement
When running the standalone `MostaqlK.exe` alone in an isolated folder, the application process exited within 50–200 ms with no GUI, no error dialog, and no log entries.

#### Detailed Investigation & Hypothesis Testing

```
+--------------------------------------------------------------------------------------------------+
|                                    Single-File Self-Extractor                                    |
|  MostaqlK.exe extracts native bundle to: %TEMP%\.net\MostaqlK\<hash>\                            |
|  (contains Microsoft.WindowsAppRuntime.dll, Microsoft.ui.xaml.dll, SxS manifests)               |
+--------------------------------------------------------------------------------------------------+
                                                │
                                                ▼
+--------------------------------------------------------------------------------------------------+
|                                    AppContext.BaseDirectory                                      |
|  Points to: C:\PortableFolder\MostaqlK.exe (NO native DLLs exist here!)                          |
+--------------------------------------------------------------------------------------------------+
                                                │
                                                ▼
+--------------------------------------------------------------------------------------------------+
|                               Undocked RegFree WinRT Activation                                  |
|  SxS Manifest: loadFrom='%MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY%Microsoft.WindowsAppRuntime'|
|                                                                                                  |
|  1. Env Var Missing/Default -> Looked in AppContext.BaseDirectory -> FILE NOT FOUND              |
|  2. Missing Trailing '\'     -> %TEMP%\...\<hash>Microsoft.WindowsAppRuntime.dll -> INVALID PATH |
|  3. Win32 LoadLibrary        -> Dynamic Linker search path did not include temp extract folder   |
+--------------------------------------------------------------------------------------------------+
```

#### Failure Mechanisms Identified & Their Targeted Solutions

1. **Undocked RegFree WinRT Redirection Failure**:
   - **Mechanism**: WinUI 3 unpackaged applications use Undocked RegFree WinRT (Side-by-Side manifest redirection) to activate native WinRT classes (`Microsoft.Windows.AppLifecycle.AppInstance`, XAML composition, etc.) without MSIX package identity. The embedded Side-by-Side manifest references `%MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY%<dll-name>`. In a portable single-file execution, native DLLs are extracted to `%TEMP%\.net\MostaqlK\<bundle-hash>\`. Because `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY` was not pointing to this temporary extract folder, WinRT activation failed immediately upon starting `AppInstance.GetCurrent()`.
   - **Solution**: Dynamically resolve the real physical path of the extracted `Microsoft.WindowsAppRuntime.dll` at runtime using `NativeLibrary.TryLoad` and Win32 `GetModuleFileName`, then explicitly export `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY` pointing to this directory before any WinRT calls are made (see Section 4.1). In addition, configure `<WindowsAppSdkUndockedRegFreeWinRTInitialize>true</WindowsAppSdkUndockedRegFreeWinRTInitialize>` in `MostaqlK.csproj` (Section 4.2).

2. **The Manifest String Concatenation Syntax (Trailing Backslash Bug)**:
   - **Mechanism**: In Windows Side-by-Side (SxS) manifest files, the redirection tag is structured as:
     `loadFrom="%MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY%Microsoft.WindowsAppRuntime.dll"`
     Notice there is **no slash between the variable and the filename**. If `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY` is set to `C:\Temp\.net\MostaqlK\abc123`, SxS concatenates it to `C:\Temp\.net\MostaqlK\abc123Microsoft.WindowsAppRuntime.dll` (an invalid path), resulting in silent SxS activation failure.
   - **Solution**: Explicitly check and ensure that the directory path assigned to `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY` always ends with a trailing backslash (`\`) so string concatenation resolves to the valid file path `C:\Temp\.net\MostaqlK\abc123\Microsoft.WindowsAppRuntime.dll` (see Section 4.1).

3. **Win32 Dynamic Linker Search Path (`SetDllDirectory`)**:
   - **Mechanism**: Native C++ DLLs (like `Microsoft.WindowsAppRuntime.dll`) load additional companion DLLs (`DirectWriteForwarder.dll`, `MRM.dll`, etc.) via standard Win32 `LoadLibrary` or dynamic import tables. The OS dynamic loader searches `AppContext.BaseDirectory` and process `PATH`, but does not automatically search the hidden .NET single-file temp extraction directory unless registered.
   - **Solution**: Call Win32 `SetDllDirectory(dir)` on the extracted bundle folder and prepend `dir` to the process `PATH` environment variable in `Program.Main` before loading any native dependencies (see Section 4.1).

4. **Multi-Instance Redirection Exception**:
   - **Mechanism**: `AppInstance.GetCurrent().GetActivatedEventArgs()` and `FindOrRegisterForKey` threw unhandled exceptions when running in an isolated environment before redirector hooks completed activation, causing the process to abort before reaching `Application.Start`.
   - **Solution**: Wrap `DecideRedirection()` inside a defensive `try/catch` block in `Program.Main`. If multi-instance registration fails or throws in single-file mode, catch the exception, log the diagnostic details, and gracefully fall back to proceeding as the primary instance (`isRedirect = false`) rather than terminating the process (see Section 4.1).

---

## 4. The Complete Architectural Fix

### 1. Pre-Entry Bootstrap & Native DLL Search Registration (`Platforms/Windows/Program.cs`)

Before calling any WinRT methods or .NET MAUI initializers, `Program.Main` performs early native initialization:

```csharp
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern bool SetDllDirectory(string lpPathName);

[DllImport("Microsoft.WindowsAppRuntime.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
private static extern int WindowsAppRuntime_EnsureIsLoaded();
```

In `InitializeWindowsAppRuntime()`:
1. Resolves `Microsoft.WindowsAppRuntime.dll` using `NativeLibrary.TryLoad`.
2. Queries the real extracted file path via `GetModuleFileName`.
3. Normalizes the extracted path with a mandatory trailing backslash (`\`).
4. Invokes `SetDllDirectory(dir)` to register the folder with the Win32 module loader.
5. Prepends `dir` to the process `PATH` environment variable.
6. Sets `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY` to `dir`.
7. Calls `WindowsAppRuntime_EnsureIsLoaded()` to initialize Undocked RegFree WinRT redirection tables.

### 2. Undocked RegFree WinRT MSBuild Property (`MostaqlK.csproj`)
```xml
<WindowsAppSdkUndockedRegFreeWinRTInitialize>true</WindowsAppSdkUndockedRegFreeWinRTInitialize>
```

### 3. Native Win32 GDI Splash Screen (`Platforms/Windows/NativeSplashScreen.cs`)
- Runs on a dedicated STA background thread immediately on entry (`< 20ms`).
- Renders Mostaql brand blue (`#2386C8`), rounded corners, Arabic title and subtitle, and an animated spinner.
- Ensures the user receives immediate visual feedback while WinUI and MAUI components load.
- Seamlessly dismissed once the main WinUI window is activated in `PlatformServiceRegistration.cs`.

### 4. Comprehensive Logging & Diagnostic Subsystem

During single-file bootstrap, standard .NET MAUI logging (`Microsoft.Extensions.Logging.ILogger`) and DI-injected loggers are completely unavailable because the dependency injection container, the MAUI host, and the WinUI 3 XAML runtime have not yet initialized. If an error occurs during pre-entry native loading or COM wrapper setup, the process terminates silently before any standard log framework starts.

To achieve complete observability, a dedicated zero-dependency bootstrap diagnostic engine was implemented in `Platforms/Windows/Program.cs`:

#### A. Architecture & File Strategy
- **Log Location**: `%LocalAppData%\MostaqlK\log\`
  - `startup-debug.log`: Chronological step-by-step milestone trace with millisecond timestamps (`[yyyy-MM-dd HH:mm:ss.fff]`), tracking every subsystem transition.
  - `crash.log`: Dedicated failure log capturing complete stack traces, inner exceptions, and crash sources.
- **Fail-Safe Design**:
  - `LogDebug()` and `LogCrash()` use direct `File.AppendAllText()` with automatic directory creation.
  - All logging calls are wrapped in empty `catch {}` blocks to guarantee that a logging failure (e.g. disk full, permission issue) never crashes or interferes with application startup.

```csharp
private static void LogDebug(string msg)
{
    try
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\n";
        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MostaqlK", "log");
        Directory.CreateDirectory(logDir);
        File.AppendAllText(Path.Combine(logDir, "startup-debug.log"), line);
    }
    catch { }
}

private static void LogCrash(string source, Exception? ex)
{
    try
    {
        string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {ex}\n";
        LogDebug($"CRASH in {source}: {ex}");
        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MostaqlK", "log");
        Directory.CreateDirectory(logDir);
        File.AppendAllText(Path.Combine(logDir, "crash.log"), msg);
    }
    catch { }
}
```

#### B. Triple-Hook Global Exception Trapping
To guarantee that no failure escapes unlogged regardless of thread context:
1. **`AppDomain.CurrentDomain.UnhandledException`**: Captures terminating unhandled exceptions across all managed threads.
2. **`TaskScheduler.UnobservedTaskException`**: Catches unobserved exceptions in background async `Task` operations that would otherwise trigger termination on finalizer GC.
3. **`AppDomain.CurrentDomain.FirstChanceException`**: Hooks into the CLR the instant *any* exception is thrown anywhere in the process—even if handled internally—logging the exception type, message, and stack trace to pinpoint hidden activation or file-load failures during bootstrap.

```csharp
AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
};

TaskScheduler.UnobservedTaskException += (s, e) =>
{
    LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
};

AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
{
    LogDebug($"[FirstChanceException] {e.Exception.GetType().FullName}: {e.Exception.Message}\n{e.Exception.StackTrace}");
};
```

#### C. Milestone Tracing & Diagnostic Inspection Flow
Every phase of application startup was instrumented with clear milestone markers:
1. **Process & Context Information**:
   - Logs launch arguments, `AppContext.BaseDirectory`, and `Environment.CurrentDirectory`.
2. **Native Runtime Location & Module Linking**:
   - Logs `NativeLibrary.TryLoad("Microsoft.WindowsAppRuntime.dll")` result and raw module handle.
   - Logs resolved module path from Win32 `GetModuleFileName()`.
   - Logs normalized directory path and Win32 `SetDllDirectory()` execution.
   - Logs updated process `PATH` variable and `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY`.
   - Logs `WindowsAppRuntime_EnsureIsLoaded()` return code or caught exceptions.
3. **COM & WinRT Setup**:
   - Traces `WinRT.ComWrappersSupport.InitializeComWrappers()`.
   - Traces `DecideRedirection()` execution, checking singleton registration and capturing any transient WinRT redirection exceptions.
4. **XAML UI Thread & App Lifecycle**:
   - Traces entry into `Microsoft.UI.Xaml.Application.Start` callback.
   - Traces `DispatcherQueueSynchronizationContext` creation and registration.
   - Traces `new App()` constructor completion.

#### D. How Logs Were Traced & Analyzed During Diagnosis
- **Real-Time Triage**: Monitored `%LocalAppData%\MostaqlK\log\startup-debug.log` while testing `MostaqlK.exe` in isolated folders.
- **Root Cause Pinpointing**: By comparing the last milestone logged before process termination against the `FirstChanceException` trace, we immediately identified that execution halted on `Microsoft.WindowsAppRuntime.dll` SxS redirection before reaching `Application.Start`.
- **Validation**: When the fix was applied, the log confirmed successful traversal of every milestone from `Main started` through `NativeLibrary.TryLoad`, `WindowsAppRuntime_EnsureIsLoaded`, `InitializeComWrappers`, and finally `App() created successfully`.

---

## 5. Verification & Validation Evidence

### Automated Verification via Screen Capture Tool (`tools/snip_tool.py`)
Using the project's Python screen inspection tool, the standalone `MostaqlK.exe` was executed in isolation:
- Confirmed the process remained alive and active (PIDs 2532 and 15112).
- Confirmed top-level window dimensions (840x740) matching the configured desktop window size.
- Captured full screen snip confirming proper rendering of:
  - Arabic typography ("مرحباً بك في مستقل ك")
  - Vector illustrations and branding icons
  - Styled action buttons ("تسجيل الدخول", "استكشاف التطبيق")
  - Native Windows chrome and backdrop

### Build Commands Verified
1. **Portable Single-File Build**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\release-windows.ps1 -Type Portable
   ```
   **Output**: `bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\MostaqlK.exe` (~129.67 MB, 1 file, 0 subdirectories).

2. **Directory Unpacked Build**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\release-windows.ps1 -Type Directory
   ```
   **Output**: Full unpacked directory distribution with satellite locale filtering.

3. **Deployment Wrapper**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1 -Platform Windows -Type Portable
   ```
   **Output**: Parameter passthrough validated with clean single-file generation.

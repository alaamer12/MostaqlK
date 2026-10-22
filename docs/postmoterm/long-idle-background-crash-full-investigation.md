# Long‑Idle Background Crash — Full Investigation & Fix Write‑up

> Comprehensive, blow‑by‑blow account of the session that diagnosed and fixed the
> "the tray icon vanishes after a few hours in the background" crash in **MostaqlK**
> (a .NET 10 / .NET MAUI + WinUI 3, single‑file, **unpackaged** Windows desktop app).
>
> This document reconstructs the *entire* working session: the symptom, every log and
> dump that was read, exactly how the crash dumps were inspected with the throwaway
> `dump.py` and `syms.py` scripts (including the wrong turns and how they were corrected),
> what the symbolicated stacks revealed, every web search and source file fetched from
> the internet and why, how the root cause was proven, how all the fixes were implemented
> and verified, and how native fail‑fast crashes were finally forwarded into Sentry.
>
> Primary code change was committed as **`6ba1ebdad41b6406fdf71b3dd3dc59360950e44c`**
> (`fix: crashing`, 2026‑10‑19 16:52, 18 files, +1315 / −5).

---

## 1. The symptom, as reported

The user reported: open the app, close it (which only hides it to the system tray — the
app keeps running in the background), and after ~3 hours the **tray icon disappears**,
which means the process is gone. This had been "fixed" several times before, with a lot
of postmortem documentation written, yet it kept happening. The request was explicit:
a real, full‑system fix, no hand‑waving — "even if you have to fool the OS or work around
Windows scheduling". The app's data lives in `C:\Users\amrmu\AppData\Local\MostaqlK`, and
the instruction was: if the logs don't surface enough detail, *increase the logging*.

Key environment facts that shaped the whole investigation:

- **Unpackaged, self‑contained, single‑file** publish. The runtime (`coreclr`) and all of
  WinUI are bundled into `MostaqlK.exe` and extracted to
  `%LocalAppData%\MostaqlK\bundle-cache\MostaqlK\<hash>\` at launch.
- **No shared .NET runtime** in play for the managed parts — everything is statically
  bundled, which later mattered a great deal for dump analysis.
- Crash evidence lands in three different places: the app's own `log\<date>\`, the
  Windows Error Reporting (WER) archive under `C:\ProgramData\Microsoft\Windows\WER`, and
  WER's LocalDumps folder `%LocalAppData%\CrashDumps`.

---

### 2. First pass over the evidence (app logs + Windows Event Log)

The investigation started by listing `%LocalAppData%\MostaqlK` and its `log\` folder:

- `log\2026-10-19\interaction-log.txt` (31 KB, last write **13:21:15**)
- `log\2026-10-19\startup-debug.log` (6.8 KB, last write **13:21:16**)

Both were read in full. The crucial observation: the last log line was at **13:21**, but
the process actually died later — the interaction log only records *events*, so a quiet
background poll cycle writes nothing. **This gap (last log 13:21, death ~13:49) was the
first proof that we had no instrumentation for the moment of death** — and the direct
justification for adding a heartbeat later.

The Windows **Application** event log (`Get-WinEvent`, `LogName='Application'`, from 12:00)
gave the first hard crash signature:

```text
Faulting application name: MostaqlK.exe, version: 1.0.4.0, time stamp: 0x6a3ed1eb
Faulting module name: Microsoft.UI.Xaml.dll, version: 3.1.7.0, time stamp: 0x810b4cf6
Exception code: 0xc000027b
Fault offset: 0x000000000039cde5
Faulting process id: 0x1b90
Faulting module path:
  C:\Users\amrmu\AppData\Local\MostaqlK\bundle-cache\MostaqlK\ia9rQ7wFJDjs\Microsoft.UI.Xaml.dll
```

Two WER `Id 1001` APPCRASH records accompanied it, carrying the exception code
`8000000e` (**`E_ILLEGAL_METHOD_CALL`**) in the P7 field, at **13:49:35–13:49:41**.

> Immediate reading of the signatures:
>
> - **`0xC000027B`** is `STATUS_STOWED_EXCEPTION` — a native *fail‑fast*. WinUI packages
>   one or more "stowed" exceptions and calls `RoFailFastWithErrorContext`, which rips the
>   process down. **No managed handler** — not `Application.UnhandledException`, not
>   `AppDomain.CurrentDomain.UnhandledException`, not an SEH filter — can veto a fail‑fast.
> - **`8000000e` / `E_ILLEGAL_METHOD_CALL`** is WinUI's "a method was called at a time /
>   on a thread where it isn't allowed."
>
> The crash is inside `Microsoft.UI.Xaml.dll`, i.e. native XAML, not our managed code —
> but something *we* do must be triggering it. And it only happens after a long idle period
> in the background, which points at a periodic/looping operation.

The WER `ReportArchive` for `MostaqlK.exe` was listed too — it held **hundreds** of
historical AppCrash folders going back to August, confirming this was a long‑standing,
recurring failure and not a one‑off. The matching `Report.wer` for the 13:49 crash was
opened; it confirmed `IsFatal=1`, `Exception Code 8000000e`, and that the faulting
original filename was `MostaqlK.dll` inside the bundle‑cache.

---

### 3. Locating the crash dumps and choosing a tool

`%LocalAppData%\CrashDumps` (WER LocalDumps) contained real minidumps, two of them ours:

- `MostaqlK.exe.8028.dmp` — 24.1 MB — **2026‑10‑14 16:52**
- `MostaqlK.exe.7056.dmp` — 22.3 MB — **2026‑10‑19 13:49:40** (the crash under investigation)

So we had a full minidump for the exact crash. The problem was **tooling**:

- `Get-Command cdb, windbg, dotnet-dump, dotnet` → only `dotnet` present.
- `C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe` → **not installed**.
- `dotnet tool list -g` → no global tools.

There was no WinDbg/cdb on the machine, so the dump had to be cracked open another way.

---

### 4. Dump analysis attempt #1 — `dotnet-dump` (and why it failed)

`dotnet-dump` was installed to a temp tool path and run against the dump:

```text
dotnet-dump analyze MostaqlK.exe.7056.dmp -c 'threads' -c 'clrthreads' -c 'exit'
```

It listed all 26 threads but then:

```text
The current thread is unmanaged
Failed to request ThreadStore
```

**Why it failed (and the wrong turns):** `clrthreads`/SOS needs the matching **DAC**
(`mscordaccore.dll`) for the exact runtime build. In a single‑file bundle there is no
`mscordaccore.dll` on disk next to the app — the bundle‑cache only had `mscorlib.dll`.

Attempts to fix it, each of which taught something:

1. Point SOS at the shared framework:
   `-c 'setclrpath C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.10'` →
   **failed with** `Unrecognized command or argument 'Files\dotnet\...'` — the space in
   `Program Files` broke the command‑line parsing.
2. Re‑run using the 8.3 short path `C:\PROGRA~1\dotnet\...` → the path was accepted
   (`Set load path for DAC/DBI to ...`) **but still** `Failed to request ThreadStore`.
   The versions actually matched (both
   `10.0.10-servicing.26326.116+f7d90799...`), so the real blocker wasn't a version
   mismatch — SOS simply couldn't bind the DAC to this statically‑linked single‑file CLR
   from an out‑of‑process DAC path.

**Conclusion of attempt #1:** `dotnet-dump`/SOS was a dead end for this single‑file build.
Pivot to reading the minidump by hand.

---

### 5. Dump analysis attempt #2 — `dump.py` (raw minidump parsing)

With the project's existing Python venv (`tools\.venv`, Python 3.11.9), the `minidump`
package was installed into a temp folder, and a throwaway script **`dump.py`** was written
to the session scratch dir. Its job: parse the minidump, read the exception stream, map
every return address on the faulting thread's stack to `module+offset` (no symbols yet),
and try to walk the `STOWED_EXCEPTION_INFORMATION` so the *inner* WinRT error could be
recovered.

Bugs hit and fixed while writing `dump.py` (the honest, iterative part):

- **First run crashed** with `TypeError: 'str' object cannot be interpreted as an integer`
  — the code assumed `ExceptionCode` was an int, but the `minidump` library returns it as
  an enum/string. Fixed by printing `er.ExceptionCode` directly instead of `hex(...)` of
  it.
- The stowed‑exception pointer walk printed **`cannot read stowed ptr`** — the
  `ExceptionInformation[0]` pointer (`0x2db48814c80`) wasn't in any memory range captured
  by this minidump, so the nested WinRT `HRESULT`/message couldn't be recovered from this
  dump. That avenue was abandoned in favour of the stack.

Even unsymbolicated, `dump.py` produced the decisive shape of the crash. The faulting
thread was **`0x38fc`**, exception code `EXCEPTION_UNKNOWN 3221226107` (= `0xC000027B`),
faulting address `Microsoft.UI.Xaml.dll+0x39cde5`. The heuristic stack scan showed the
classic fail‑fast sandwich:

```text
Microsoft.UI.Xaml.dll+0x39cde5   (fault)
combase.dll+0x1fc70a
Microsoft.UI.Xaml.dll+0xb23688
...
combase.dll!CoInitializeEx / BaseThreadInitThunk  (thread root)
```

That already told us: the faulting thread was **CoInitialize'd** (a COM/OLE thread) and
died deep inside `Microsoft.UI.Xaml.dll` via `combase`. But `module+offset` is not a
root‑cause; we needed function names.

---

### 6. Dump analysis attempt #3 — `syms.py` (symbol resolution via `dbghelp`)

No debugger, but `dbghelp.dll` ships with Windows and `symsrv.dll` was found inside the
Visual Studio 2022 Remote Debugger folder. A second throwaway script **`syms.py`** was
written to:

1. Copy `dbghelp.dll` (from `System32`) and `symsrv.dll` (from VS) into a private temp
   `dbg\` folder and load `dbghelp` via `ctypes`.
2. Initialize a symbol path pointing at the Microsoft public symbol server:
   `srv*<tmp>\symcache*https://msdl.microsoft.com/download/symbols`.
3. `SymLoadModuleExW` each interesting module at its dump base address (WinUI, combase,
   kernelbase, ntdll, CoreMessagingXP, etc.), then resolve every stack address with
   `SymFromAddrW`.

Wrong turns and corrections while getting symbols to actually download — this is where
most of the "did it wrong, then right" happened:

1. **First run: no symbols at all.** The symbol server was never contacted and
   `symcache\` stayed empty. Two bugs:
   - `SymSetOptions(... | 0x80000000 & 0)` — operator precedence made the intended flag
     evaluate to `0`; worse, `SYMOPT_DEFERRED_LOADS (0x4)` meant symbols were only loaded
     lazily and the download never triggered during resolution.
   - `symsrv.dll` refuses to hit the network unless a sibling marker file **`symsrv.yes`**
     exists (the "do you accept the Microsoft symbol server EULA" gate).
   Fixes: create an empty `symsrv.yes` in the `dbg\` folder, and change the options to
   `SymSetOptions(0x2 | 0x10 | 0x40)` = `UNDNAME | LOAD_LINES | NO_PROMPTS`,
   dropping `DEFERRED`.
2. **Second run: modules loaded (base addresses printed) but `SymFromAddrW` still returned
   nothing.** The `SYMBOL_INFOW` struct was mis‑sized — `SizeOfStruct` was computed
   wrongly. Fixed by hard‑setting `si.SizeOfStruct = 88` (the correct header size, with the
   name buffer handled separately). After this, the `symcache\` folder filled up with
   `.pdb` folders (`Microsoft.ui.xaml.pdb`, `combase.pdb`, `ntdll.pdb`, `kernel32.pdb`, …)
   downloaded from `msdl.microsoft.com`, and the stack resolved to real function names.
3. Initially `syms.py` **skipped `MostaqlK.exe` frames** (filtered out) on the theory they
   were noise. That hid the managed/host side of the stack. It was changed to also
   `SymLoadModuleExW` `MostaqlK.exe` and *stop filtering*, which exposed the CLR frames
   (finalizer thread, host) that nailed the root cause.

---

### 7. What the symbolicated dump revealed (the smoking gun)

With symbols, the faulting thread **`0x38fc`** resolved to this (top = fault):

```text
KERNELBASE.dll!RaiseFailFastException+0x152
combase.dll!RoFailFastWithErrorContextInternal2+0x43a
Microsoft.UI.Xaml.dll!FailFastWithStowedExceptions+0x59
Microsoft.UI.Xaml.dll!ctl::ComBase::ReleaseImpl+0x1b2b55      <-- releasing a XAML object
MostaqlK.exe!... (RtlFreeHeap / SetEvent interop frames) ...
MostaqlK.exe!FinalizerThread::FinalizeAllObjects+0x1a0        <-- CLR finalizer thread
MostaqlK.exe!FinalizerThread::WaitForFinalizerEvent+0x1d
MostaqlK.exe!FinalizerThread::FinalizerThreadWorker+0x34f
combase.dll!CoInitializeEx+0x68
MostaqlK.exe!FinalizerThread::FinalizerThreadStart+0x134
kernel32.dll!BaseThreadInitThunk+0x14
ntdll.dll!RtlUserThreadStart+0x21
```

Read bottom‑to‑top this is unambiguous:

> The **CLR finalizer thread** is finalizing a managed object whose native peer is a WinUI
> XAML `DependencyObject`. Releasing that native object
> (`ctl::ComBase::ReleaseImpl`) runs on the **wrong thread** (the finalizer thread, not the
> UI dispatcher thread that owns the XAML object). WinUI detects the illegal cross‑thread
> release and responds with `FailFastWithStowedExceptions` → `0xC000027B`. There is no
> managed frame that could ever catch this.

The other threads corroborated the architecture: thread `0x4700` was the real UI thread
sitting in `FrameworkApplication::RunDesktopWindowMessageLoop` / `GetMessageW`; thread
`0x33fc` was WinUI's `CompositorScheduler::RenderThreadFrame` (the 60 Hz render loop still
running); `CoreMessagingXP.dll` registrar/dispatch threads were present — i.e. the UI was
*alive* and animating while hidden, and the finalizer was concurrently releasing XAML
transforms.

**Second dump cross‑check (`MostaqlK.exe.8028.dmp`, 10‑14).** Running `syms.py` against the
older dump showed the *same family* of failure, but even more specific:

```text
Microsoft.UI.Xaml.dll!DirectUI::UIAffinityReleaseQueue::DoCleanup+0x190
...
Microsoft.UI.Xaml.dll!ctl::ComObject<DirectUI::RotateTransform>::`scalar deleting destructor'
Microsoft.UI.Xaml.dll!DirectUI::DependencyObject::OnFinalRelease+0x363
Microsoft.UI.Xaml.dll!ctl::ComObject<DirectUI::CompositeTransform>::`vftable'
```

That dump names the exact objects being destroyed: **`RotateTransform`** and
**`CompositeTransform`**, through WinUI's `UIAffinityReleaseQueue`. Both dumps die
releasing **transform objects**. That turned a hypothesis into a proven root cause:
*something in the app is producing a flood of native XAML transform objects, and their
finalization is what fail‑fasts WinUI.*

---

### 8. Web research — what was searched, what was visited, what was learned, and why

The dump told us *where* WinUI died; the web research was needed to confirm *that this is a
known WinUI behaviour* and to find the exact MAUI code that produces the transform garbage,
so the fix would be targeted and not a guess.

**Web searches run (and why):**

1. `WinUI 3 crash "ReleaseImpl" FailFastWithStowedExceptions finalizer thread 0xc000027b`
   — to confirm the fail‑fast‑on‑finalizer pattern is a recognised WinUI failure mode in
   unpackaged/self‑contained apps. Result: multiple reports that unpackaged, self‑contained
   WinUI 3 apps crash with `0xc000027b` in `Microsoft.UI.Xaml.dll`, especially after
   publishing — consistent with our build type.
2. `microsoft-ui-xaml github ComBase.cpp "ReleaseImpl" "OnFinalReleaseOffThread" OR
   "ReleaseOnUIThread" ctl::ComBase` — to find the WinUI source that decides how a
   `ComBase` object is released off‑thread, i.e. the code that *chooses* to fail‑fast.
3. `"ComBase::ReleaseImpl" WinUI "wrong thread" finalizer crash DependencyObject released
   on finalizer thread .NET` — to confirm specifically that a `DependencyObject` released
   on the **finalizer thread** is the canonical trigger. Result: issue reports describing
   exactly that — XAML objects must be released on their owning dispatcher thread; if a
   finalizer releases them after the dispatcher is gone/busy, WinUI fail‑fasts.

**Source files fetched directly from GitHub (and why):**

- `https://raw.githubusercontent.com/microsoft/microsoft-ui-xaml/main/src/dxaml/xcp/dxaml/lib/ComBase.cpp`
  and the alternate path `.../main/dxaml/xcp/dxaml/lib/ComBase.cpp` — both returned
  **404** (the public mirror doesn't expose that path/layout), so the WinUI internal
  release logic couldn't be read line‑by‑line. The understanding of
  `UIAffinityReleaseQueue` / off‑thread release therefore rests on the symbolicated dump
  frames plus the issue reports, not the WinUI source.
- `https://raw.githubusercontent.com/dotnet/maui/main/src/Core/src/Platform/Windows/TransformationExtensions.cs`
  — **this was the key fetch.** MAUI's `UpdateTransformation` is the code that applies
  `TranslationX/Y`, `Scale`, `Rotation`, etc. to a WinUI `FrameworkElement`. Reading it
  confirmed that MAUI allocates a **`new ScaleTransform` *and* a `new CompositeTransform`
  on every single transform property change** and assigns them to `RenderTransform`,
  discarding the previous ones. Those discarded native objects are exactly the
  `CompositeTransform`/`RotateTransform` the dumps die finalizing. **This is the direct
  mechanical link between "an animation is running" and "WinUI fail‑fasts on the finalizer
  thread."**
- Later, for the `Missing file: ''` issue (§12),
  `https://raw.githubusercontent.com/dotnet/maui/main/src/Core/src/ImageSources/FileImageSourceService/FileImageSourceService.Windows.cs`
  — to understand how MAUI resolves a file image source on Windows and why an empty
  filename produces a `FileNotFoundException`.

**The synthesis from all of it:** every running animation in the app
(`NewRibbonBadge`'s perpetual "new" sweep, `ShimmerBox`, `EnrichmentShimmerOverlay`, the
`LastScanStatus` refresh spinner, `PipelineRadar`) drives MAUI transform updates. Each
update allocates ~2 native XAML transforms. At ~60 fps per animated view that is on the
order of **~120 native XAML `DependencyObject`s per second per view, for the entire life
of the process — hidden window or not.** The GC eventually finalizes them on the finalizer
thread, which releases them off the UI thread, which WinUI punishes with a fail‑fast. A
long idle period in the background is simply enough time for the finalizer to run during a
moment when the UI dispatcher can't safely take the release — hence "crashes after hours".

---

### 9. The in‑process fix — stop producing the garbage

Two complementary code changes removed the root cause:

**9.1 `TransformReuseMapping` (`Platforms/Windows/TransformReuseMapping.cs`, new ~177 lines).**
Replaces MAUI's Windows transform mapping for `TranslationX/Y`, `Scale/ScaleX/ScaleY`,
`Rotation/RotationX/RotationY`, `AnchorX/Y` with a version that **mutates a single
`CompositeTransform` (and a single `PlaneProjection` for 3‑D rotation) per element**
instead of allocating new objects every frame. It zeroes an existing transform at identity
rather than dropping it (so a loop passing through identity doesn't recreate it next
frame), and only writes properties that actually changed (`SetIfChanged`). Visual output is
byte‑for‑byte identical to MAUI's (same `RenderTransformOrigin` anchor, same
scale/rotation/translation composition). It is installed via
`ViewHandler.ViewMapper.ModifyMapping(...)` right after `builder.Build()`, before the first
handler exists. **This is the real fix: the per‑frame native XAML garbage simply stops
existing, so the finalizer has nothing dangerous to release.**

**9.2 Background motion suspension (`MotionPreferences` + the animation loops).**
A new static flag `MotionPreferences.IsAmbientMotionSuspended` is set from
`AppLifecycleService.IsInBackgroundChanged`. While the window is hidden in the tray,
`IsReducedMotionRequested` now returns `true`, so every unit that already honours reduced
motion goes quiet for free. The individual perpetual loops were also gated directly:
`LastScanStatus` stops its refresh spin, `ShimmerBox` parks its sweep, and
`EnrichmentShimmerOverlay` freezes its band while hidden. Nobody can see these animations
in the tray anyway; stopping them removes the remaining background render/transform work.

---

### 10. The out‑of‑process safety net — the watchdog

Because a fail‑fast can *never* be caught in‑process, a second, independent guarantee was
added so that even if WinUI ever fail‑fasts again, the tray icon comes back within seconds.

- **`Core/Diagnostics/WatchdogPolicy.cs`** (pure, no Win32/MAUI, ~111 lines) — the decision
  rules, deliberately isolated so the crash‑path tests can prove them without killing real
  processes. Constants: `--watchdog`, `--supervised`, `MOSTAQLK_NO_WATCHDOG`,
  `MaxRestartsInWindow = 5`, `RestartWindow = 30 min`, `RelaunchDelay = 4 s`, and the
  per‑PID clean‑exit event name `Local\MostaqlK.App.CleanExit.<pid>`. `Decide(...)` returns
  `Relaunch` / `StopCleanExit` / `StopSystemShutdown` / `StopCrashLoop`.
- **`Platforms/Windows/AppWatchdog.cs`** (~422 lines) — the supervisor. The primary instance
  creates the clean‑exit event and spawns `MostaqlK.exe --watchdog <pid>` (one supervisor
  per session, guarded by the `Local\MostaqlK.App.Watchdog` mutex). The watchdog does
  nothing but wait for the watched PID to die; if it dies **without** the clean‑exit event
  set, it waits `RelaunchDelay` (so WER can finish the dump and the singleton mutex frees),
  writes a pending‑crash record for Sentry, and relaunches the app with
  `--silent-start --supervised` (back to the tray, no second watchdog). It refuses to
  relaunch on intentional exit, during Windows shutdown (`GetSystemMetrics(SM_SHUTTINGDOWN)`),
  or after 5 restarts in 30 min. Everything is logged to `log\<date>\watchdog.log`.
- **`Core/Diagnostics/ExitIntent.cs`** — a platform‑neutral "the user really asked to quit"
  signal. `MarkIntentional()` is called from tray **Quit** (`TrayIconService.OnQuit`), the
  close‑to‑tray **Exit** choice, and the window `OnClosed` handler, plus
  `AppDomain.ProcessExit`; the Windows entry point plugs in the handler that sets the
  clean‑exit event. This is what stops the watchdog mistaking a deliberate exit for a crash.
- **Wiring:** `Program.Main` runs `AppWatchdog.TryRunAsWatchdog(args)` *first* (before any
  WinUI, bundle extraction, splash, or singleton logic) and returns immediately if this
  process is the supervisor; the primary instance calls
  `AppWatchdog.InitializeForPrimaryInstance(...)` after the runtime is up.

The watchdog is disabled when a debugger is attached or `MOSTAQLK_NO_WATCHDOG=1` is set.

---

### 11. Getting native fail‑fasts into Sentry

The user's pointed question — "Sentry is built for this, why didn't it catch the crashes?"
— has a precise answer, and it drove a dedicated fix.

**Why Sentry was blind:** (1) a `RoFailFastWithErrorContext` fail‑fast terminates the
process *before any managed code runs*, and the Sentry .NET SDK only captures **managed**
exceptions, so its handlers never fire. (2) The pre‑existing post‑mortem path
(`CrashReporter.CheckAndReportPreviousCrashes`) ran in `Program.Main` **before**
`UseSentry` had initialised the SDK, and it only looked for `log\crash.dmp`, which a
fail‑fast never writes. So Sentry literally had no path to see these crashes.

**The fix — `Platforms/Windows/NativeCrashForwarder.cs` (~302 lines):**

- The **watchdog** (which *does* witness the death and its exit code) writes a
  `pending-crashes\crash-<utc>-<pid>.json` record (pid, exit code + name, uptime, dump path).
- On the **next app launch**, once `SentrySdk.IsEnabled` (polled up to 60×500 ms, then a
  15 s settle so compressing ~20 MB dumps doesn't fight UI startup),
  `NativeCrashForwarder.Forward()` sends **one Fatal `SentryEvent` per crash**. Each event
  carries tags (`crash_type=native_fail_fast`, `post_mortem=true`, `exit_code`,
  `witnessed_by_watchdog`), a fingerprint, and attachments: the **gzipped WER minidump**
  from `%LocalAppData%\CrashDumps` plus that day's `watchdog.log`, `heartbeat.log`,
  `startup-debug.log`, `interaction-log.txt`, and `crash.log`.
- It also sweeps **WER dumps no watchdog saw** (upgrades, watchdog disabled, crash loops):
  last 14 days, max 3 per launch, each dump sent exactly once (deduplicated via
  `reported-crash-dumps.txt`, keyed by name|size|mtime).

**Sentry config hardening (`MauiProgram.cs`):** `Release = mostaqlk@<version>`,
`Environment = debug|production`, `IsGlobalModeEnabled = true` (desktop app, one scope),
`AttachStacktrace`, `MaxBreadcrumbs = 200`, a **disk `CacheDirectoryPath`** under the cache
folder (so queued envelopes and the open session survive an instant kill),
`AutoSessionTracking = true` (a killed session is counted as a crash in Release Health),
and `MaxAttachmentSize = 40 MB` for the dumps. Lifecycle breadcrumbs ("Window hidden to
tray" / "Window restored") were added.

---

### 12. More logging, and the `Missing file: ''` cleanup

- **`Services/Diagnostics/ProcessHeartbeat.cs` (new, ~75 lines):** every 2 minutes, and on
  every background/foreground transition, appends a health line to
  `log\<date>\heartbeat.log` — uptime, bg state, working‑set/private/managed memory, GC
  gen0/1/2 counts, **finalizer backlog** (`FinalizationPendingCount`), pinned objects,
  handles, threads. This exists specifically because the 13:49 crash looked like "last log
  13:21": the heartbeat now brackets any silent death to within one interval and shows
  whether native‑object churn is returning. (A formatting refinement swapped
  `string.Create(CultureInfo.InvariantCulture, ...)` for `FormattableString.Invariant(...)`
  and dropped the now‑unused `System.Globalization` using.)
- **Richer first‑chance log:** `Program.cs`'s `FirstChanceException` hook now records the
  *throw site* for the first few `Missing file` occurrences, not just the empty filename.
- **The `Missing file: ''` root cause (user asked to fix it even though harmless):** the
  startup log carried many `[FirstChanceException] Missing file: '' (Unable to find the
  specified file.)` lines. Reading the symbolicated throw site showed it came from
  `Microsoft.Maui.FileImageSourceService.GetLocal`. Tracing through `AppIcon`
  (`AppIconGlyphExtensions.ToImageSource`), the app was requesting colour variants of icons
  that were **never generated** — e.g. `icon_close_white` — by building an absolute path
  under the bundle‑cache. MAUI's `GetLocal` throws `FileNotFoundException` for the missing
  file (the recurring log line) and then falls back to an `ms-appx:///` URI that loads
  nothing in an unpackaged app — so the icon was already invisible. The fix: `ToImageSource`
  now returns `null` (renders nothing) after a **cached** `File.Exists` check, so the
  exception is gone and the on‑screen result is unchanged. (~20 icon/colour combinations
  still have no PNG; they simply render nothing now instead of throwing.)

---

### 13. How it was all verified

**Unit/decision tests — `tools/CrashPathTests` (console harness):**
`WatchdogPolicy.cs` was added to the test project and **13 new watchdog checks** were added
(args parsing, primary spawns watchdog, supervised doesn't spawn a second, debugger/env‑var
suppression, fail‑fast→relaunch, intentional/shutdown→stop, crash‑loop→stop, old restarts
outside window ignored, relaunch args). Final result: **39/39 crash‑path checks PASS**.
`dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -c Debug` → **0 warnings, 0
errors** (repeatedly).

**Live end‑to‑end test on the user's portable build:**

1. Both processes were confirmed running via `Win32_Process`:
   - app **pid 3772** (parent 924),
   - watchdog **pid 19652** (parent 3772), command line `... MostaqlK.exe --watchdog 3772`.
2. The first `watchdog.log` / `heartbeat.log` lines confirmed the supervisor attached and
   the heartbeat was writing (e.g. `#2 to-background ... finalizePending=1308 ...`).
3. **Crash simulated:** `Stop-Process -Id 3772 -Force`. The watchdog logged the death
   (`code=0xFFFFFFFF, cleanExit=False, uptime=0.00:01:54`), wrote the pending‑crash record,
   and **relaunched the app ~4 s later as pid 11236** with `--silent-start --supervised`
   (back in the tray, no second watchdog). On that relaunch, the forwarder marked the
   crash plus the three existing dumps (10‑14 and two from 10‑19) as reported in
   `reported-crash-dumps.txt`.

**The "it disappeared again!" follow‑up (and its resolution).** After a rebuild, the user
reported the app was gone and feared another crash. Investigation proved it was **not** a
crash: `watchdog.log` recorded only the test kill; `heartbeat.log` showed pid 11236 healthy
(beats at 16:39:43 and 16:41:43, flat ~200 MB private, finalizer backlog *dropping*
486→220, threads 60→32) and then simply stopped; the only Event‑Log crash was the *old*
pre‑fix build (pid 7352, `0xc000027b`) starting while the new one launched; and the publish
`MostaqlK.exe` was rewritten at 16:44:38 — i.e. a rebuild. The culprit was
`scripts\release-windows.ps1` lines 22–28, which force‑kills **every** `MostaqlK.exe`
(app + watchdog together) to free the file lock before publishing:
`$runningProcesses | Stop-Process -Force`. A simultaneous force‑kill of both leaves the
watchdog no chance to log or relaunch — which is correct behaviour (you can't overwrite a
running exe), not a watchdog failure.

---

### 14. Follow‑on work in the same session (context, not part of the crash fix)

After the crash fix (`6ba1ebda`), the session continued with separate requests:

- **Version tagging.** Adopted a `MAJOR.MINOR.PATCH.TAG{n}` scheme (e.g. `1.0.4.dev1`),
  wiring `AppPaths` to read the tag from `InformationalVersion` and documenting the
  "every rebuild bumps the tag" rule in `AGENTS.md` and the `version-bumping` skill.
- **Per‑version data — bug, redesign, partial revert.** Nesting *all* app data under a
  per‑version folder made each new build start empty (DB/Settings not found). Per the
  user's choice ("Stable data, versioned logs"), the design was changed so Data, Settings,
  Cache and bundle‑cache stay in the shared `%LocalAppData%\MostaqlK` root and only
  `log\` + crash files are versioned, with `LogDirectoryPurger.PruneSiblingVersions` for
  retention and a first‑launch migration copy. The two most recent version‑related tasks
  were then **reverted** at the user's request, returning `AppPaths`/`LogDirectoryPurger`
  to the shared‑root layout and the `.csproj` to `1.0.4` / `ApplicationVersion 5` (the
  `AppPaths.VersionDirectory`, migration, and `PruneSiblingVersions` code was removed).

These are recorded here for completeness; the crash fix itself is independent of them.

---

### 15. Root‑cause summary (one paragraph)

MostaqlK's shimmer/spinner/ribbon animations ran continuously — even while the window was
hidden in the tray — and MAUI's stock Windows transform mapping allocates two native WinUI
XAML transform objects (`CompositeTransform` + `ScaleTransform`) on every frame. Over hours
this produced a massive stream of short‑lived native `DependencyObject`s. When the CLR
finalizer thread released those objects off the UI dispatcher thread, WinUI detected the
illegal cross‑thread release and fail‑fasted the process with `STATUS_STOWED_EXCEPTION`
(`0xC000027B`, inner `E_ILLEGAL_METHOD_CALL` / `8000000E`) inside
`ctl::ComBase::ReleaseImpl` → `FailFastWithStowedExceptions` — a crash no managed handler
can catch, which is why the tray icon silently vanished and Sentry never saw it. The fix
removes the garbage at the source (`TransformReuseMapping` + background motion suspension),
backstops any residual fail‑fast with an out‑of‑process watchdog, forwards native crashes
to Sentry post‑mortem with the minidump and logs, and adds a heartbeat so the moment of any
future death is always on disk.

---

### 16. Artifacts & reference

**New / changed source (commit `6ba1ebdad41b6406fdf71b3dd3dc59360950e44c`):**

| File | Role |
| ------ | ------ |
| `Platforms/Windows/TransformReuseMapping.cs` | Root‑cause fix: reuse one transform per element |
| `Platforms/Windows/AppWatchdog.cs` | Out‑of‑process supervisor / relaunch |
| `Core/Diagnostics/WatchdogPolicy.cs` | Pure, testable watchdog decision rules |
| `Core/Diagnostics/ExitIntent.cs` | Intentional‑exit signal (don't relaunch) |
| `Platforms/Windows/NativeCrashForwarder.cs` | Forward native fail‑fasts to Sentry post‑mortem |
| `Services/Diagnostics/ProcessHeartbeat.cs` | Periodic health line (`heartbeat.log`) |
| `UI/PlatformComponents/MotionPreferences.cs` | `IsAmbientMotionSuspended`, reduced‑motion gate |
| `UI/DesignSystem/ShimmerBox.cs`, `EnrichmentShimmerOverlay.cs`, `UI/PlatformComponents/LastScanStatus/LastScanStatus.cs` | Pause ambient loops while hidden |
| `UI/PlatformComponents/AppIcon/AppIconGlyphExtensions.cs` | `Missing file: ''` fix (existence check) |
| `Platforms/Windows/Program.cs`, `PlatformServiceRegistration.cs`, `MauiProgram.cs` | Wiring: watchdog, hardening, Sentry options, forwarder |
| `UI/TrayIcon/TrayIconService.cs` | Mark Quit as intentional exit |
| `tools/CrashPathTests/*` | +13 watchdog checks (39/39 pass) |
| `UNITS.md` | Entries for the new units |

**Throwaway analysis scripts (session scratch, not committed):**
`dump.py` (raw minidump exception + stack scan) and `syms.py`
(`dbghelp`/`symsrv` symbolication against the Microsoft public symbol server).

**Crash signatures seen:** `0xC000027B` (`STATUS_STOWED_EXCEPTION`, fail‑fast),
inner `0x8000000E` (`E_ILLEGAL_METHOD_CALL`); faulting module `Microsoft.UI.Xaml.dll`;
faulting frame `ctl::ComBase::ReleaseImpl` → `FailFastWithStowedExceptions`; crash thread =
CLR finalizer thread.

**Dumps analysed:** `%LocalAppData%\CrashDumps\MostaqlK.exe.7056.dmp` (2026‑10‑19 13:49,
22.3 MB) and `MostaqlK.exe.8028.dmp` (2026‑10‑14, 24.1 MB).

# Published-Layout Dispatcher Fail-Fast (`0xC000027B`) — Full Investigation, 2026-10-28

Companion document: `animation-tick-failfast-2026-10-28.md` — the bug class that produced the
`TickerGuard` fix (dev60). This document is the crash hunt itself: every tool, every wrong turn, the
parser bugs that hid the answer for six weeks, the forensics recipe, and the honest status of what is
**not** fixed.

## 0. Bottom line

| | |
|---|---|
| **Symptom** | a published `Directory`-shape instance dies with no managed handler firing, no `crash.log`, and only a WER minidump behind it |
| **Exception** | `0xC000027B` `STATUS_STOWED_EXCEPTION` (a WinUI/combase fail-fast) |
| **Faulting instruction** | `Microsoft.UI.Xaml.dll+0x39cde5` — **identical in every one of the fifteen dumps parsed during the session** |
| **Symbolicated frame** | `microsoft.ui.xaml.dll!CXcpDispatcher::MessageTimerCallback+0xe1` — WinUI's *own* dispatcher message-pump timer (named chain resolved from `4688`) |
| **Inner stowed error** | `0x80004002` `E_NOINTERFACE` in the eight Oct-27 dumps decoded (three of those files are still in `CrashDumps`; five were rotated out by WER mid-session), `0x80004005` `E_FAIL` in the four Oct-28 dumps, and three dumps whose captured window was too small to decode |
| **Fixed?** | **No.** The mechanism is named and reproducible-in-principle; two long dual-channel soaks did not reproduce it, three shorter runs did |
| **What was fixed on the way** | the tick-escape class (`TickerGuard`, dev60) and six diagnostics blind spots (dev61) |
| **Status of the user-facing shape** | `scripts/release-windows.ps1`'s **default** mode is `Directory`, i.e. exactly the shape that crashes — this is not a smoke-test artifact |

---

## 1. Starting conditions

The session began with one red gate: `tools/coexistence-smoke.ps1` reported a WER dump whose pid
matched neither instance it launched, on a build whose published layouts had previously been proven
good. The pre-hardened script could not answer "which process was that?", and the September
investigation (`long-idle-background-crash-full-investigation.md`) had already spent a session on the
same exception code and hit a wall it recorded as *"cannot read stowed ptr … not in any memory range
captured by this minidump"*.

Two things were therefore on the table: make the gate self-explanatory, and actually read the dumps
instead of re-deriving offsets.

Environment established first (all verified, not assumed):

| Check | Result |
|---|---|
| `cdb.exe` / WinDbg | **absent** (`Program Files (x86)\Windows Kits\10\Debuggers` does not exist; no `windbg` in `WindowsApps`) |
| Python | 3.11.9 |
| `dbghelp.dll` | `%WINDIR%\System32` ✓ |
| `symsrv.dll` | three copies found; used `C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\symsrv.dll` |
| Symbol server reachable | `curl` to `msdl.microsoft.com/download/symbols/...` returned `404` in 0.8 s on a deliberately bogus GUID → reachable ✓ |
| `HKCU\...\Windows Error Reporting\LocalDumps` | **absent** (so the dumps were not WER-locally-configured) |
| `HKLM\...\WER\LocalDumps` | only Avast subkeys; no `MostaqlK.exe` |
| The app's own dump writer | `Services/Diagnostics/CrashReporter.cs:233` writes `crash.dmp` into the version dir with `MiniDumpWithIndirectlyReferencedMemory (0x40) | MiniDumpWithThreadInfo (0x1000)` and returns `EXCEPTION_CONTINUE_SEARCH` |
| Conclusion | the 8–20 MB dumps in `%LOCALAPPDATA%\CrashDumps` are **WER's**, named `MostaqlK.exe.<pid>.dmp`, and the app's `crash.dmp` was never produced for these deaths |

`CrashDumps` at the start of the analysis held ten files; `reported-crash-dumps.txt` in the stable root
records the *history* of them since 2026-10-14 (`8028` 10-14, `7056` 10-19, then dense batches on 10-22
12:49–12:50 and 10-23 16:19–16:34, single entries through 10-27). That file is the reason the answer to
"is this new?" is **no** — the class has been dying for two weeks, mostly during automated runs.

---

## 2. Dump analysis #1 — raw parsing, and the two offset bugs

`scratch/dump.py` (throwaway) parses the header, stream directory, module list, exception stream and
thread list with `struct` only, then heuristically scans the faulting thread's captured stack for values
that fall inside a loaded module. First real output:

```text
EXCEPTION thread=0x2b3c code=0xC000027B STATUS_STOWED_EXCEPTION
  ExceptionInformation[0] = 0x0000000000000002
  ExceptionInformation[1] = 0x000002027c338b00
```

which is *wrong* in a way that took a second pass to notice: `params=1779158501` was also being printed,
and a parameter count cannot be 1.78 billion. `MINIDUMP_EXCEPTION_STREAM` is
`{ ThreadId, Alignment, EXCEPTION_RECORD, THREAD_CONTEXT }` and on x64 `EXCEPTION_RECORD` is
`Code@0 Flags@4 ExceptionRecord@8 ExceptionAddress@16 NumberParameters@24 UseExceptions@28
ExceptionInformation@32` — so relative to the stream the record begins at `+8` and the information
array begins at `+40`, not `+32`. The "ExceptionInformation[0] = 2" was actually
**NumberParameters = 2**, and the heap pointer labelled `[1]` was the real
`ExceptionInformation[0]` — the stowed-exception pointer.

Lesson worth keeping: a hand-written parser that produces a plausible-looking number is more dangerous
than one that crashes. The nonsense `params` value is what forced the re-derivation.

Stack scan (module+offset only, no symbols) for `16864`:

```text
KERNELBASE.dll+0x12c012   combase.dll+0x1fc70a
Microsoft.UI.Xaml.dll+0x39cde5   Microsoft.UI.Xaml.dll+0xb236a0
```

Cross-checked across every dump in the directory: the fault offset is `+0x39cde5` in **all of them**
(`1056 15744 18288 18784 20416 22000 4972 9504 12700 16864 8992 4688 22020 20904 9484` over the course
of the session) — one instruction, one bug class, not five crashes.

---

## 3. Dump analysis #2 — reading the stowed exception, and the bug that hid it since September

`scratch/stowed.py` follows `ExceptionInformation[0]` → `EXCEPTION_PARAMETER` →
`STOWED_EXCEPTION_INFORMATION`. It took four corrections:

1. **Which stream holds the bytes.** `MemoryInfoListStream` (15) is VAD *metadata*; its
   `MINIDUMP_MEMORY_INFO` location descriptor sits at entry+48, and reading it produced ranges totalling
   an impossible 1.5 TB from a 12 MB file. Real content lives in `MemoryListStream` (5) /
   `Memory64ListStream` (9). Restricting to those, the same file reports ~11 MB captured over ~25 000
   ranges — which is what a 12 MB dump should say.
2. **`MINIDUMP_LOCATION_DESCRIPTOR` is `{ ULONG32 DataSize; ULONG32 Rva }` — size first.** The parser
   had them swapped. Proof it was the ordering and not the file: reading entries as
   `{start, A, B}`, field `A` had **median 8**, `B` was **monotonically non-decreasing** in entry order
   (803 123 → 12 165 638, i.e. the packed tail of the file), and `sum(A) = 11 362 521` which is exactly
   `fileSize − 803 123`. So `A` is the size and `B` is the offset.

   **This single line explains September's dead end.** With the fields swapped, every address looked
   "not captured", and the reasonable conclusion was that a full dump was required. It never was:
   the answer was inside the 11 MB mini dump the whole time.
3. **Overlapping windows.** WER captures many small, overlapping ranges. A full-length read at
   `ExceptionInformation[0]` fails because the captured window there is 24–72 bytes. A
   `read_partial` that takes the *smallest* containing range and returns whatever it holds is what made
   the struct readable.
4. **The struct shape, read from the data rather than from documentation.** The 56-byte object is
   `Header{USHORT Size=56; USHORT Type=0}` @0, `Signature` @4 = `0x53453032` = ASCII **"20ES"** (the
   stowed-exception magic), and **`ErrorValue` @8**. What the dumps then said:

```text
MostaqlK.exe.1056.dmp   thr=0x3b04  Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004002 (E_NOINTERFACE)
MostaqlK.exe.15744.dmp  thr=0x5888  Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004002
MostaqlK.exe.18288.dmp  thr=0x154   Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004002
MostaqlK.exe.18784.dmp  thr=0x684   Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004002
MostaqlK.exe.20416.dmp  thr=0x3094  Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004002
MostaqlK.exe.22000.dmp  thr=0x5a7c  Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004002
MostaqlK.exe.4972.dmp   thr=0x3a48  Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004002
MostaqlK.exe.9504.dmp   thr=0x3e48  Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004002
MostaqlK.exe.8992.dmp   thr=0x4074  Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004005 (E_FAIL)
MostaqlK.exe.4688.dmp   thr=0x4470  Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004005
MostaqlK.exe.22020.dmp  thr=0x282c  Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004005
MostaqlK.exe.16864.dmp  thr=0x2b3c  Microsoft.UI.Xaml.dll+0x39cde5  E=0x80004005
MostaqlK.exe.12700.dmp / 20904.dmp / 9484.dmp        EXCEPTION_PARAMETER window too small to decode
```

  The `1056/15744/18288/20416/22000` files were decoded during the session and have since been rotated
  out of `CrashDumps` by WER's own retention; the eight Oct-27 lines above are the recorded readings,
  and the `E_NOINTERFACE`/`E_FAIL` split is reproducible today on the files still present
  (`18784 4972 9504` vs `8992 4688 22020 16864`).

**Two different inner errors on the identical instruction is the load-bearing fact**: the fatal thing is
the *escape* reaching WinUI's stow-and-fail path, not any one exception type. It also means a handler or
filter that selects by exception type cannot be trusted to see the crash — which is precisely the mistake
made twice in this codebase (§6 of the companion document).

---

## 4. Dump analysis #3 — symbolication without a debugger

`scratch/syms.py` stages `dbghelp.dll` + `symsrv.dll` into `scratch/dbg/`, creates an empty
`symsrv.yes` marker (without it `symsrv` silently refuses to hit the network — September's note, still
true), sets `SymSetOptions(SYMOPT_UNDNAME|SYMOPT_LOAD_LINES|SYMOPT_NO_PROMPTS)` with **no** deferred
loads, and loads each module at its dump base from the binary on disk (layout for WinUI/app assemblies,
`System32` for OS ones), then resolves every address found by the stack scan.

Four failures, each of which looked like a different problem than it was:

| # | Symptom | Actual cause |
|---|---|---|
| 1 | Ran >6 minutes, printing nothing; looked hung | the module allow-list matched `MostaqlK.exe` and the managed set, so dbghelp pulled ~400 MB of PDBs (NGEN `.ni.pdb` images included). Silent pipe + network = indistinguishable from a hang. Fixed by narrowing to the modules the fault can live in and flushing progress to a log file. |
| 2 | `AttributeError: module 'ctypes' has no attribute 'ulong'` | `ctypes.ulong` does not exist on Windows builds; use `c_ulong`. |
| 3 | Every `SymFromAddrW` returned FALSE with `GetLastError() = 6` (`ERROR_INVALID_PARAMETER`), which reads exactly like "symbols did not download" | the handle. With no `argtypes`, ctypes passes `GetCurrentProcess()`'s pseudo-handle as a **32-bit int**, so dbghelp receives a truncated handle. Declaring `argtypes=[HANDLE, c_ulonglong, POINTER(c_ulonglong), c_void_p]` fixed it; lookups then succeeded (the returned displacement changed from `0` to the real `+0x152`, which is how the difference was spotted). |
| 4 | Lookups returned TRUE but the name came back empty | `MaxNameLen` was left zero. The struct's true field offsets are not worth betting on, so the robust form is: opaque buffer, `SizeOfStruct = 88`, set every plausible 4-byte slot in 52..80 to the buffer length, and read the name at the offset it actually landed on — measured empirically as **84**, where `"RaiseFailFastException"` appeared. |

### Getting the WinUI PDB when `symsrv` will not

`Microsoft.ui.xaml.pdb` defeated `symsrv` twice: 117 MB and 144 MB partials left as `download*.error` in
the cache. Plain `curl` gets it. Read the CodeView record out of the *layout's own* DLL, build the
GUID+age the way the server wants it (first three fields little-endian reversed), and drop the file into
the cache layout so dbghelp finds it locally:

```text
find b"RSDS" in Microsoft.UI.Xaml.dll      -> guid bytes, age, original pdb path
GUIDAGE = 842BD730F2C13BF8850C829A507007F31
curl -sSL --retry 8 --retry-all-errors -C - -o Microsoft.ui.xaml.pdb \
  https://msdl.microsoft.com/download/symbols/Microsoft.ui.xaml.pdb/<GUIDAGE>/Microsoft.ui.xaml.pdb
-> 292,016,128 bytes, curl exit 0
place at <symcache>/Microsoft.ui.xaml.pdb/<GUIDAGE>/Microsoft.ui.xaml.pdb
```

### What the symbols then said

`8992` and `4688` (Oct 28) share the tick-delivery chain, and `1056`, `16864` share the same raw offsets;
the fully named chain below is **`4688`'s** — the only stack symbolicated after the WinUI PDB was
available. (`22020` was decoded for its inner error but its stack was never symbolicated, so no claim is
made about its frames beyond the identical `+0x39cde5` fault.)

```text
kernelbase.dll!RaiseFailFastException+0x152
combase.dll!RoFailFastWithErrorContextInternal2+0x43a
microsoft.ui.xaml.dll!FailFastWithStowedExceptions+0x59
microsoft.ui.xaml.dll!FailFastWithStowedExceptions+0x4c
microsoft.ui.xaml.dll!CXcpDispatcher::OnReentrancyProtectedWindowMessage+0x9a
microsoft.ui.xaml.dll!CXcpDispatcher::MessageTimerCallback+0xe1        <-- the failing frame
coremessagingxp.dll!DispatcherQueueTimer::TimerCallback+0x83
coremessagingxp.dll!Timeout::OnTimerSignaled+0x3f
coremessagingxp.dll!EventSource<ITypedEventHandler<DispatcherQueueTimer*,IInspectable*>>::InvokeAll+0xad
coremessagingxp.dll!TimeoutManager::Callback_OnDispatch+0x1a9
```

### All-threads scan (what it ruled out)

`--all-threads` over `4688` (56 threads) shows the CLR finalizer thread **waiting**, not working:

```text
thread 0x3644
  kernelbase.dll!WaitForSingleObjectEx+0x8e
  coreclr.dll!FinalizerThread::FinalizeAllObjects+0x83
  coreclr.dll!FinalizerThread::WaitForFinalizerEvent+0x24
```

so this death is not the finalizer releasing an object in the act — the illegal work had already been
*queued* and WinUI's own dispatcher timer was the thing that surfaced it. That refines September rather
than contradicting it: same underlying cross-thread-release pressure, but the *visible* victim frame is
`CXcpDispatcher`, and no managed handler of ours is on the path.

A related correction, stated plainly: September's write-up labels the fault
`ctl::ComBase::ReleaseImpl+0x1b2b55`. A displacement of 1.7 MB inside one function is not a function
offset — dbghelp attributes an address to the nearest preceding **public** symbol, and WinUI's internal
functions are not public. The real name for `+0x39cde5` is `FailFastWithStowedExceptions`, confirmed now
that the actual PDB is loaded. The *class* September diagnosed (native XAML objects released off their
affinity thread, drained on a dispatcher timer) still matches the evidence; its naming did not.

---

## 5. Corroborating telemetry from the app itself

The runs that died left their own fingerprints in `heartbeat.log`:

```text
[07:00:18] #1 start    uptime=0:00:00  gen2=0  finalizePending=57    handles=1598 threads=26 ws=114MB
[07:00:21] #2 to-bg    uptime=0:00:03  gen2=3  finalizePending=969    handles=2122 threads=39 ws=198MB
[07:40:20] #1 start    uptime=0:00:00  gen2=0  finalizePending=51     handles=1592 threads=27
[07:40:22] #2 to-bg    uptime=0:00:01  gen2=4  finalizePending=2417   handles=2090 threads=41
[08:00:58] #4 to-bg    uptime=0:00:24  gen2=19 finalizePending=3036   handles=2626 threads=53
```

`finalizePending` in the low thousands within the first second or two — thousands of objects whose
finalizers release native peers, which is the fuel September identified, now arriving during startup and
the first enrichment burst instead of after 22 idle hours.

And in `interaction-log.txt` for the process that died at 07:00:22 (`16864`), the last app-side marks
before death are entirely ordinary:

```text
07:00:22.078 MARK Pipeline.PlatformGate    {"source":"mostaql","enabled":true}
07:00:22.377 MARK Pipeline.PlatformGate    {"source":"nafezly","enabled":true}
07:00:22.378 MARK App.Startup.PipelineStarted
07:00:22.402 MARK EnrichmentWorker.AttemptStart {"Source":{"Value":"nafezly"},"ProjectId":56941}
07:00:22.421 MARK EnrichmentWorker.AttemptStart {"Source":{"Value":"mostaql"},"ProjectId":1288763}
```

No fault, no warning, then nothing. Four `FirstChanceCOM` records at 07:00:19–20 (`0x80070001`,
`CanvasFontSet` via MAUI `FontManager`) were the only exceptions the then-filtered probe could see, and
they are harmless startup noise — documented in the companion file so nobody re-investigates them.

---

## 6. Web research, and what it did *not* provide

Two searches, because the local evidence names a WinUI internal function and the question worth asking
is whether this is a known framework failure mode.

* `WinUI 3 0xC000027B stowed exception unpackaged MAUI crash dispatcher timer finalizer ReleaseImpl` —
  returned the September-consistent cluster: a StackOverflow report *"Uncatchable exception when
  populating a WinUI-3 ListView (0xc000027b)"* whose snippet says the crash probability rises sharply
  when items contain an `Image`, a Microsoft Q&A thread *"WinUI 3 published unpackaged self-contained
  app crashes"* stating that `0xc000027b` is a stowed-exception wrapper whose common trigger is
  something specific inside it, `microsoft-ui-xaml#10897` (fatal Win32 exception when closing a MAUI
  modal page on WinUI), and `microsoft-ui-xaml#9793` (unmodified WinUI3 template crashing in Release).
* A follow-up search added nothing new.

**Limitations, stated honestly:** the StackOverflow page returned `403 Forbidden` to the fetcher and the
Microsoft Q&A page returned a rate-limit error, so **their bodies were never read** — only search
snippets. The direction they suggest (image-heavy item templates in a virtualized list, published
unpackaged configuration) is consistent with this app — a feed of project cards with cover images, in
design-parity mode with seeded content, running `--silent-start` — but it is **not** evidence and nothing
in this document rests on it. Conclusions here rest on the dumps, the symbols and the app's own logs.

The `Image`/item-template hint is nonetheless the most promising untested lead, recorded as such below.

---

## 7. The publish-shape question, answered before it could mislead

Is the crashing shape one users actually get? Checked against `scripts/release-windows.ps1`:

| Mode | Properties | WAP runtime |
|---|---|---|
| `Portable` (opt-in) | `PublishSingleFile=true`, `SelfContained=true` → also sets `WindowsAppSDKSelfContained=true` (csproj line 67-70) | self-contained in the bundle |
| **`Directory` (the script's default)** | `PublishSingleFile=false`, `SelfContained=true` | framework-dependent |
| `tools/coexistence-smoke.ps1` | `PublishSingleFile=false`, `SelfContained=true`, `SatelliteResourceLanguages=ar` | framework-dependent — **same as `Directory`** |

So the smoke measures the default release shape, and the crash is user-facing. An experiment to compare
against the `Portable` shape was set up and **not** completed; it is listed as a follow-up because the
answer changes what mitigation is appropriate (a self-contained-WAP build might not hit this path at all,
which would make "ship Portable" a workaround rather than a fix).

One earlier red herring is worth recording because it was believed for a day: the deaths that happened at
~4 s in *every* published layout in October were the deleted `en-US\Microsoft.ui.xaml.dll.mui` (the
locale-pruning target was removed in dev51, and the smoke now asserts the file exists). That was a real
bug with a real fix. It is **not** this bug: the current deaths are intermittent (2 s, 31 s, 40 s, and
absent from two long soaks) and the MUI assertion passes in all of them.

---

## 8. Instruments added, because each gap cost time

| Instrument | Gap it closes |
|---|---|
| `Program.LogBirthCertificate` — `pid`, parent pid via `NtQueryInformationProcess`/`PROCESS_BASIC_INFORMATION.InheritedFromUniqueProcessId`, channel, version, `toastActivation=`, redirect key — logged before any WinUI code | "which process is this dump?" Two instances used to interleave one log with only timestamps. It also exposes a known unknown: dumps for pids `9484` and `20904` exist with **no** matching birth-certificate line in either channel root, i.e. processes that died before the first log write or reached no logging at all |
| `pid:` on every `startup-debug.log` line, `\| pid= \|` on every `interaction-log.txt` line | same, retrospectively |
| `InteractionLogger.Request(...)` under `MOSTAQLK_DIAG_NET=1` at the three exit points (`MostaqlFetcher`, `NafezlyFetcher`, `AssetDownloadService`), each naming its own source through its own descriptor | turns "a disabled platform is network-silent" into a measured claim: control **mostaql 3 / nafezly 4**, treatment (`settings_disabled_platforms="nafezly"`) **mostaql 3 / nafezly 0** |
| First-chance probe widened to every exception type + live stack, capped at 400 | its old type filter could not see `0x80004005`, so in every run that died it reported "nothing happened" |
| `tools/enable-full-dumps.ps1` (HKCU `DumpType=2`, `-Restore`) | the mini dump gives the HRESULT but not the stowed record's attached **strings**, which name the failing API. Used once; restored afterwards, registry key verified gone |
| `tools/coexistence-smoke.ps1`: `-SoakSeconds`, `-SkipPublish`, liveness = pid + exe path + start time, dump-attribution line, MUI assertion | a 4 s window caught early deaths and let later ones through; `Get-Process -Id` alone can be satisfied by a recycled pid and report coexistence it never observed |

---

## 9. Soak evidence (what reproduces and what does not)

All runs are `--silent-start` on published layouts, `MOSTAQLK_DIAG_COM=1`, `MOSTAQLK_NO_WATCHDOG=1`,
logs attributable by pid.

| # | Build | Configuration | Outcome |
|---|---|---|---|
| 1 | dev59 stable | 40 s soak, both channels | **died** — `16864`, dumped pid is the launched instance, later decoded as `E_FAIL` |
| 2 | dev59 stable | 30 s single | **died** — `8992`, ~31 s, `E_FAIL` (foreign `9484` dump in the same window) |
| 3 | dev60 (guarded) stable | 75 s single | **died** — `4688`, ~40 s, `E_FAIL`; `TickerGuard` logged **zero** catches |
| 4 | dev60 (guarded) canary, treatment run | 90 s | **died** — `22020`, ~6 s, `E_FAIL`; `TickerGuard` zero catches |
| 5 | dev60 (guarded) stable | 90 s single | **survived** (`18656`), 24 first-chance records, all handled noise |
| 6 | dev60 (guarded) both channels | **150 s** dual soak | **COEXISTENCE SMOKE PASSED** — alive, mutexes held, prefs isolated, zero dumps |
| 7 | dev60 (guarded) both channels | **240 s** dual soak, full dumps armed | **COEXISTENCE SMOKE PASSED** — zero dumps, so no full dump was captured |

Interpretation, without dressing: the fault is intermittent inside the first minute under startup +
first-enrichment load, it is not prevented by guarding our own ticks, and two 150–240 s dual-channel
runs happened not to hit it. Four deaths out of seven attempts. A gate that requires zero deaths in a
single 150 s soak will therefore pass on luck, which is why the smoke now reports the *dump's pid*
instead of only "a dump exists".

---

## 10. Root-cause summary (one paragraph)

A published `Directory`-shape unpackaged WinUI 3 instance dies with `0xC000027B` when WinUI's own
dispatcher message-pump timer (`CXcpDispatcher::MessageTimerCallback`, reached via a
`DispatcherQueueTimer` tick delivered through a WinRT delegate adapter) encounters a COM failure and
routes it to `FailFastWithStowedExceptions` → `RoFailFastWithErrorContextInternal2` → `RaiseFailFastException`.
The stowed record carries `E_NOINTERFACE` in the earlier dumps and `E_FAIL` in the later ones, i.e. the
error identity differs while the death is identical, proving the fatal property is *an error surfacing on
that path*, not a particular exception. The app contributes the fuel that September already named —
thousands of native-peer objects awaiting finalization within seconds of launch, released off their
affinity thread and drained on the dispatcher — and contributes nothing catchable: no managed handler of
ours runs on this path, which is why there is no `crash.log`, why `Application.UnhandledException` does
not fire, and why the `TickerGuard` work (a genuine fix for the sibling path) recorded zero activity in
the runs that still died.

## 11. Unresolved, and the next concrete step

1. **Read the stowed strings.** Mini dumps give the HRESULT; the attached message strings name the API.
   Procedure that works today: `tools/enable-full-dumps.ps1` → soak until a death → parse with the
   pointer walk (`ExceptionInformation[0]` → `+0x10` → `'20ES'` struct → follow its pointer slots,
   accepting only targets that *begin* with printable UTF-16, or a full dump prints the whole heap as
   "strings"). Keep the dump: `DumpCount` must be raised above 3 or the file copied out before the next
   death rotates it away.
2. **Reduce the churn, not the handling.** `finalizePending` 2417 at 1 s of uptime is the target metric;
   `heartbeat.log` is the instrument. No managed handler can help on this path, so the only lever that
   exists in our code is producing fewer cross-thread releases (and the app's units already dispose
   composition objects on the UI thread; the remaining churn is plausibly MAUI item-template and image
   plumbing, per the unconfirmed lead in §6).
3. **Test the `Portable` shape** against the same soak, and decide what the *default* release mode should
   be if only one shape is affected.
4. **Attribute the ownerless dumps** — `9484`, `20904`, `12700` have no birth-certificate line; either
   they die before the first write (worth logging earlier) or they are processes this app launches that
   never reach `Program.Main`'s logging.
5. **Retention awareness**: WER rotated five decoded Oct-27 dumps out of `CrashDumps` during this session.
   Evidence that matters must be copied out of `%LOCALAPPDATA%\CrashDumps` immediately.

## 12. Recipes worth keeping (the parts that were re-derived twice)

```text
# streams that matter
3 ThreadList   4 ModuleList   5 MemoryList(content, 32-bit RVA)   6 Exception
7 SystemInfo   9 Memory64List(content)   15 MemoryInfoList(VAD metadata, NOT content)

# MINIDUMP_EXCEPTION_STREAM:  ThreadId@0 Alignment@4 EXCEPTION_RECORD@8
# EXCEPTION_RECORD (x64):     Code@0 Flags@4 ExceptionRecord@8 ExceptionAddress@16
#                             NumberParameters@24 UseExceptions@28 ExceptionInformation@32
# MINIDUMP_LOCATION_DESCRIPTOR: { ULONG32 DataSize; ULONG32 Rva }   <-- size FIRST
# MINIDUMP_MEMORY_DESCRIPTOR:   { UINT64 StartOfMemoryRange; LOCATION }   stride 16
# MINIDUMP_MEMORY64_LIST entry: { UINT64 Start; UINT64 PartSize; UINT64 Rva }
# MINIDUMP_MEMORY_INFO:         Base@0 AllocBase@8 RegionSize@24 Location.Rva@48 Location.Size@52

# stowed chain
ExceptionInformation[0] -> EXCEPTION_PARAMETER
EXCEPTION_PARAMETER+0x10 -> pointer -> STOWED_EXCEPTION_INFORMATION
STOWED_EXCEPTION_INFORMATION: Header{USHORT Size;USHORT Type}@0, Signature '20ES'@4, ErrorValue@8

# symbolication without a debugger
dbghelp.dll from System32 + symsrv.dll from VS + empty sibling file "symsrv.yes"
SymSetOptions(UNDNAME 0x2 | LOAD_LINES 0x10 | NO_PROMPTS 0x40)   # no DEFERRED
SymInitializeW(HANDLE, "srv*<cache>*https://msdl.microsoft.com/download/symbols", FALSE)
SymLoadModuleExW(HANDLE, None, <dll on disk>, None, base, size, None, 0)
ctypes: declare argtypes with wt.HANDLE for EVERY call, or the pseudo-handle truncates
SYMBOL_INFOW: SizeOfStruct=88, MaxNameLen nonzero (slots 52..80), name read at offset 84 (measured)
```

## 13. Artifacts

| Path | State |
|---|---|
| `docs/postmoterm/animation-tick-failfast-2026-10-28.md` | new — the `TickerGuard` class, the ten-site audit, the design trade-offs |
| `UI/PlatformComponents/TickerGuard.cs` + 10 routed call sites + rule 6 + `UNITS.md` row + `TestTickerCallbacksAreGuarded` | committed dev60 (`2f82e44`) |
| `Platforms/Windows/Program.cs` birth certificate, `pid` on log lines, `InteractionLogger.Request`, widened first-chance probe, `tools/enable-full-dumps.ps1`, hardened `tools/coexistence-smoke.ps1` | committed dev61 (`62a6171`) |
| `scratch/dump.py`, `scratch/stowed.py`, `scratch/syms.py`, `scratch/duckrun.ps1`, `scratch/netproof.ps1`, `scratch/dbg/` (dbghelp+symsrv+~700 MB symbol cache) | throwaway, deleted with the session; §12 preserves the format knowledge, §4 the ctypes traps, §3 the parser bugs |
| `%LOCALAPPDATA%\CrashDumps\*.dmp` | left in place — they are the user's crash evidence, and WER owns their retention |
| WER `LocalDumps\MostaqlK.exe` (`DumpType=2`) | removed via `-Restore`; key verified absent |

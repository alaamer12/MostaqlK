# Post-Mortem: Published-Layout Dispatcher Fail-Fast (`0xC000027B`), 2026-10-28

## Incident overview

* **Application**: `MostaqlK.exe` — WinUI 3 / .NET 10 MAUI, **unpackaged**, published layout
  (`Directory` shape: `PublishSingleFile=false`, `SelfContained=true`, which is also
  `scripts/release-windows.ps1`'s default mode, so this is the shape users actually run).
* **Symptom**: a launched instance dies with no managed error and no `crash.log`, between ~2 s and
  ~40 s after start, while running hidden (`--silent-start`). Intermittent, not a fixed-time crash.
* **Exception**: `0xC000027B` (`STATUS_STOWED_EXCEPTION`) — a fail-fast raised by WinUI itself.
* **Dumps analyzed**: 10 WER minidumps in `%LOCALAPPDATA%\CrashDumps`, all `MostaqlK.exe.<pid>.dmp`,
  parsed by hand (no WinDbg, no `cdb`, no `pip install`).

---

## 1. What the dumps actually said

Every one of the ten dumps is the **same instruction**:

| Dump | pid | Fault | Inner stowed error |
|---|---|---|---|
| `1056 / 15744 / 18288 / 18784 / 20416 / 22000 / 4972 / 9504` | Oct 27 | `Microsoft.UI.Xaml.dll+0x39cde5` | `0x80004002` `E_NOINTERFACE` |
| `8992 / 4688` | Oct 28 | `Microsoft.UI.Xaml.dll+0x39cde5` | `0x80004005` `E_FAIL` |

With `dbghelp` + the real PDB, `+0x39cde5` resolves and the faulting thread's chain is unambiguous:

```text
kernelbase.dll!RaiseFailFastException+0x152
combase.dll!RoFailFastWithErrorContextInternal2+0x43a
microsoft.ui.xaml.dll!FailFastWithStowedExceptions+0x59
microsoft.ui.xaml.dll!FailFastWithStowedExceptions+0x4c
microsoft.ui.xaml.dll!CXcpDispatcher::OnReentrancyProtectedWindowMessage+0x9a
microsoft.ui.xaml.dll!CXcpDispatcher::MessageTimerCallback+0xe1        <-- the failing frame
coremessagingxp.dll!DispatcherQueueTimer::TimerCallback+0x83
coremessagingxp.dll!TimeoutManager::Callback_OnDispatch+0x1a9
```

**Read it plainly:** the process dies inside **WinUI's own dispatcher message-pump timer**
(`CXcpDispatcher::MessageTimerCallback`), which picked up a stowed COM failure and fail-fasted. Two
different inner errors produce the identical death, which is the point: *the escape is the fatal
event, not one specific throw.*

Two supporting observations from the same dumps:

* The faulting thread is a **DispatcherQueue thread** (`CoreMessagingXP` frames), not the CLR
  finalizer thread — the CLR finalizer was sitting in `WaitForSingleObjectEx` at the moment of death
  in `4688`, so the illegal release that September proved was real had already been *queued* and was
  being *drained* here.
* `heartbeat.log` for these runs shows `finalizePending` at **2417 after 1 s** and **3036 at 24 s**
  of uptime — the same cross-thread-release pressure the September investigation named, still
  present, now arriving inside the first minute instead of after 22 hours.

---

## 2. Why September could not read this, and how the block was removed

The September investigation concluded *"cannot read stowed ptr … not in any memory range captured by
this minidump"* and gave up on the inner error. That conclusion was an artifact of a parser bug, not
a property of the dump:

```c
typedef struct _MINIDUMP_LOCATION_DESCRIPTOR { ULONG32 DataSize; ULONG32 Rva; }   // size FIRST
```

The throwaway parser had them the other way round, so every "captured memory range" it computed was
garbage and the stowed pointer looked absent. Fixing the field order (and reading content from
`MemoryListStream` 5 only — `MemoryInfoListStream` 15 is VAD *metadata* whose entries overlap and lie)
made the answer appear immediately:

```text
ExceptionInformation[0] = 0x220eeaf09e0            <- EXCEPTION_PARAMETER
  +0x10 -> 0x220f0618878                            <- STOWED_EXCEPTION_INFORMATION
     Header.Size=56  Signature=0x53453032 ("20ES")  ErrorValue=0x80004002 (E_NOINTERFACE)
```

**Lesson, worth keeping:** a minidump is 12 MB of structures whose layout is only documented in
`dbghelp.h`. When a tool says "the data is not in the dump", check the struct definition before
believing it — the same 292 MB full-dump hunt the whole plan was built around turned out to be
unnecessary.

### Getting the WinUI symbols when `symsrv` gives up

`Microsoft.ui.xaml.pdb` is ~292 MB and the symsrv plugin failed twice (150 MB partials left as
`download*.error` in the cache). It downloads fine with plain `curl` — read the CodeView `RSDS` record
out of the layout's own `Microsoft.UI.Xaml.dll`, build the server path from the GUID (first three
fields little-endian reversed) plus age, and drop the file into the cache where `dbghelp` will find it
without the network:

```text
GUIDAGE = 842BD730F2C13BF8850C829A507007F31
https://msdl.microsoft.com/download/symbols/Microsoft.ui.xaml.pdb/<GUIDAGE>/Microsoft.ui.xaml.pdb
-> <cache>/Microsoft.ui.xaml.pdb/<GUIDAGE>/Microsoft.ui.xaml.pdb
```

Two ctypes traps cost real time and are worth remembering: passing the process pseudo-handle without
declaring `argtypes` truncates it to 32 bits (every lookup then fails with `ERROR_INVALID_PARAMETER`,
which looks exactly like "symbols not downloaded"), and `MaxNameLen` must be non-zero or
`SymFromAddrW` returns TRUE with an empty name.

---

## 3. The separate, definitively-fixed bug found on the way

While chasing this, a second uncatchable-escape class was proven from the same stack shape: an
exception thrown by a managed `IDispatcherTimer.Tick` handler is **not** routed to
`Microsoft.UI.Xaml.Application.UnhandledException` either — WinUI invokes ticks through its own
delegate adapter, and whatever leaves it is stowed and fail-fasted.

`UI/PlatformComponents/TickerGuard.cs` is now the only way a tick (or a MAUI `Animation.Commit`
callback) runs: it swallows, logs the first failure per ticker with the **live** stack, and parks a
ticker that fails 30 consecutive times. All ten loops route through it (ShimmerBox,
EnrichmentShimmerOverlay, NewRibbonBadge, ChromeValueLabel, LastScanStatus, the dashboard text timer
and slide `finished:` callback, the radar frame ticker, the morph cleanup + refresh timers). Binding
as rule 6 of `.repertoire/.steering/v3/animation-safety.md`, with
`ParserTests.TestTickerCallbacksAreGuarded` failing the build if a new unguarded `.Tick +=` appears.

This is honest scope: it removes a whole class of "silent tray death" that was reachable from any
future animation unit, and the dumps confirmed the mechanism — but it is **not** the fix for
`CXcpDispatcher::MessageTimerCallback`, which fails inside WinUI before any managed tick is involved
(`TickerGuard` logged zero hits in the runs that still died).

---

## 4. Diagnostics that were missing and are now permanent

| Instrument | What it answers |
|---|---|
| `Program.LogBirthCertificate` (`pid`, parent pid, channel, version, `toastActivation=`, redirect key) | "which process is this dump?" — a WER dump whose pid matches no launched instance used to be un-attributable |
| `pid:` on every `startup-debug.log` line, `\| pid= \|` on every `interaction-log.txt` line | two instances of two channels previously interleaved one log with only timestamps to separate them |
| `InteractionLogger.Request(...)` under `MOSTAQLK_DIAG_NET=1` | the only honest witness for "a disabled platform makes zero requests" — recorded at the three places bytes leave the process |
| First-chance probe widened to **every** exception type under `MOSTAQLK_DIAG_COM=1`, with the live stack | the old filter (`InvalidCastException/COMException`) could not see the fatal `0x80004005`, because that HRESULT *is* what a plain `System.Exception` marshals to — the instrument was hiding the bug |
| `tools/enable-full-dumps.ps1` (opt-in HKCU WER `DumpType=2`, `-Restore` removes it) | the full heap, hence the stowed struct's attached strings |
| `tools/coexistence-smoke.ps1 -SoakSeconds N -SkipPublish`, pid+path+start-time liveness | a soak long enough to see late deaths, and no false GREEN from a recycled pid |

## 5. Status and the next step

Soak statistics from this session (published dev60 layouts, guarded build):

| Round | Shape | Result |
|---|---|---|
| 40 s dual-channel | dev59 | stable `16864` died, WER dump attributed |
| 30 s single (dev59 + diagnostics) | dev59 | `8992` died at ~31 s |
| 75 s single (dev60, guarded) | dev60 | `4688` died at ~40 s; treatment-soak `22020` died at ~6 s |
| 90 s single (dev60, guarded) | dev60 | survived (`18656`), zero `TickerGuard` hits |
| 150 s dual-channel (dev60, guarded) | dev60 | **COEXISTENCE SMOKE PASSED** — both channels alive, zero dumps |
| 240 s dual-channel (dev60, guarded, full dumps armed) | dev60 | **COEXISTENCE SMOKE PASSED** — both channels alive, zero dumps |

Neither dual-channel soak (150 s + 240 s, both channels side by side) reproduced the fail-fast, so
the death is not the deterministic ~4 s wall the MUI bug was. It is genuinely intermittent, it has
been observed at 2 s, 31 s and 40 s, and the guarded build is not proven innocent — `TickerGuard`
recorded zero hits in every run, including the ones that died, which is what puts the cause inside
WinUI's own dispatcher rather than in any callback we own.

**Still open**: the `CXcpDispatcher::MessageTimerCallback` fail-fast is not fixed. It is WinUI's own
dispatcher frame, it is intermittent, and it is the September cross-thread-release class arriving
early rather than after 22 hours. The next concrete step is already instrumented: keep
`tools/enable-full-dumps.ps1` on during a long soak, and when it next dies, read the
`STOWED_EXCEPTION_INFORMATION` **strings** out of the full dump (`scratch/stowed.py` follows the
pointer chain already) — the message text names the failing API, which is the one thing a mini dump
cannot give. After that, the lever is reducing `finalizePending` churn, not adding handlers: no
managed handler can intercept this path.

Scripts used here live in `scratch/` and are deliberately not committed
(`scratch/dump.py`, `scratch/stowed.py`, `scratch/syms.py`, `scratch/duckrun.ps1`,
`scratch/netproof.ps1`); the permanent tooling is `tools/enable-full-dumps.ps1` and the hardened
`tools/coexistence-smoke.ps1`.

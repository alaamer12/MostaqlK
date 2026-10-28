# Post-Mortem: The Animation-Tick Fail-Fast — `0xC000027B` From a DispatcherTimer Tick

Companion document: `published-layout-dispatcher-failfast-2026-10-28.md` (same session, same dump set,
a **different** and still-unresolved path). Read that one for the forensic tooling; this one is about
the bug class `TickerGuard` was created to eliminate.

## 0. Summary in five lines

* An exception thrown by an `IDispatcherTimer.Tick` handler in a WinUI 3 / MAUI app is **not** routed
  to `Microsoft.UI.Xaml.Application.UnhandledException`. WinUI invokes the tick through its own
  delegate adapter; whatever leaves that adapter is **stowed** by combase and turned into
  `RoFailFastWithErrorContextInternal2` → `RaiseFailFastException` → instant process death.
* The escape **is** the fatal event. Two dumps reached the identical faulting instruction carrying two
  different inner errors (`0x80004002`, `0x80004005`), so no specific exception type needs to be
  predicted and no managed handler can help.
* The app had **ten** unguarded per-frame callbacks, any of which could take that path on any bad
  frame — an animation unit is the most likely author of one, because it touches native visuals.
* Fix: `UI/PlatformComponents/TickerGuard.cs` is now the only way a tick or a MAUI `Animation.Commit`
  callback runs. Swallow → log the first failure per ticker with the **live** stack → park a ticker
  that fails 30 times running.
* Enforced, not trusted: `ParserTests.TestTickerCallbacksAreGuarded` fails the build if an unguarded
  `.Tick +=` exists, and rule 6 of `.repertoire/.steering/v3/animation-safety.md` states it as a
  binding animation rule.

---

## 1. How this was found (the chain of evidence, in order)

1. **`tools/coexistence-smoke.ps1` started reporting WER dumps.** After the MUI fix, a 40 s soak of the
   published `stable` layout died, and the hardened liveness check proved the dumped pid **was** the
   instance the smoke launched (`MostaqlK.exe.16864.dmp`), which the pre-hardening script could not
   tell. That converted "some foreign process crashes sometimes" into "our published layout dies".
2. **The app's own logs said nothing.** The crashing process's log directory contained
   `startup-debug.log`, `interaction-log.txt` and `heartbeat.log` and **no `crash.log`**. The app has
   `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`,
   `SetUnhandledExceptionFilter`, and a `WinUI.UnhandledException` hook that sets `Handled = true`.
   None of them fired. A crash that bypasses four independent handlers is by definition uncatchable
   from managed code, which is the signature of a fail-fast, not of an exception.
3. **The dumps were parsed by hand** (`scratch/dump.py`, `scratch/stowed.py`) and symbolicated with
   `dbghelp`/`symsrv` (`scratch/syms.py`) — no WinDbg, no `cdb`, no `pip install`. The recipe, the two
   parser bugs that had been hiding the answer since September, and the curl-based PDB retrieval are
   written up in the companion document, §2–§4.
4. **The faulting thread's symbolicated chain named the mechanism.** For `8992` and `4688` (Oct 28) the
   same shape appears. The chain as fully named is `4688`'s; `8992` is identical except that its
   `Microsoft.UI.Xaml.dll` frames were still raw `+0x39cde5` / `+0xb236a0` at the time (same
   instruction, resolved once the PDB was fetched for the companion investigation). `kernelbase`,
   `combase` and `CoreMessagingXP` have public symbols, which is why the delivery path below was
   namable before the WinUI PDB was ever available:

   ```text
   kernelbase.dll!RaiseFailFastException+0x152
   combase.dll!RoFailFastWithErrorContextInternal2+0x43a
   microsoft.ui.xaml.dll!FailFastWithStowedExceptions+0x59
   microsoft.ui.xaml.dll!CXcpDispatcher::MessageTimerCallback+0xe1
   coremessagingxp.dll!Microsoft::UI::Dispatching::DispatcherQueueTimer::TimerCallback+0x83
   coremessagingxp.dll!Microsoft::CoreUI::Dispatch::Timeout::OnTimerSignaled+0x3f
   coremessagingxp.dll!Microsoft::WRL::Details::DelegateArgTraits<
       ITypedEventHandler_impl<AggregateType<DispatcherQueueTimer*, IDispatcherQueueTimer*>, IInspectable*>
       >::DelegateInvokeHelper<Implements<RuntimeClassFlags<2>, ITypedEventHandler<...>, FtmBase>, ...>::Invoke+0x87
   coremessagingxp.dll!Microsoft::CoreUI::Dispatch::TimeoutManager::Callback_OnDispatch+0x1a9
   ```

   The frame that matters is the one in the middle: a **`DispatcherQueueTimer` tick being delivered
   through a WinRT delegate adapter**, and everything above it is the stow-and-kill machinery. The
   app's own tickers reach that exact code path.
5. **The audit followed.** If a tick can kill the process, then the question becomes: how many ticks
   does this app own, and does any of them let anything escape? Answer: ten callbacks, **zero** of them
   guarded at the subscription point.

---

## 2. The mechanism, precisely

```
managed Tick body throws
   → the exception crosses the CCW / delegate adapter that WinUI holds for the Tick subscription
   → combase captures it as a "stowed exception" (STOWED_EXCEPTION_INFORMATION, signature '20ES')
   → WinUI's error boundary calls FailFastWithStowedExceptions
   → combase!RoFailFastWithErrorContextInternal2 → kernelbase!RaiseFailFastException
   → 0xC000027B, process terminated. No stack unwind, no handler, no WER-interactive step.
```

Three consequences that are easy to get wrong:

| Belief | Reality |
|---|---|
| "`Application.UnhandledException` with `Handled = true` protects XAML" | It protects XAML **event** dispatch (the September long-idle fix relied on it, and it works there). A tick delivered through the delegate adapter is a different boundary; the handler is never consulted. |
| "`SetUnhandledExceptionFilter` will at least log it" | The fail-fast path can bypass it; no `crash.log` was produced by any of these deaths. The only artifacts are WER's dump plus whatever the app logged moments earlier. |
| "a `try/catch` inside each tick body is enough" | Only if the body is *fully* inside it. `ShimmerBox` and `EnrichmentShimmerOverlay` guarded the per-item `box.Tick(nowMs)` but **not** the subscription lambda around it — the snapshot copy, the `lock`, the `Environment.TickCount64` arithmetic and the list iteration were all outside any handler. |

### Why the inner error varies

The stowed record's `ErrorValue` is whatever HRESULT the escaping exception marshals to:

| Escaping exception | `ErrorValue` |
|---|---|
| `InvalidCastException` (a failed WinRT cast — e.g. `Handler` gone, wrong native peer) | `0x80004002` `E_NOINTERFACE` |
| plain `System.Exception`, or anything with no dedicated HRESULT | `0x80004005` `E_FAIL` |
| `InvalidOperationException` (illegal call on a WinRT object, e.g. after the XAML root is gone) | `0x8007050F` / marshalled `E_FAIL` |

Observed in this dump set: `E_NOINTERFACE` in eight dumps (Oct 27) and `E_FAIL` in four (Oct 28). The
table is in the companion document, §3. **A handler that filters by exception type would miss half of
these deaths — which is exactly the mistake the app's own diagnostic made** (§6.3).

---

## 3. The audit: every per-frame callback in the app

Enumerated with `grep -rn --include=*.cs "\.Tick +="`, plus the two MAUI animation-commit sites.

| # | Unit | Site | Drives | Guarded before? |
|---|---|---|---|---|
| 1 | `ShimmerBox` | `UI/DesignSystem/ShimmerBox.cs:178` | one app-wide 33 ms ticker sweeping every skeleton box | only the per-box `Tick` body, not the lambda |
| 2 | `EnrichmentShimmerOverlay` | `UI/DesignSystem/EnrichmentShimmerOverlay.cs:210` | one app-wide 33 ms light-band over cards being enriched | same partial shape as #1 |
| 3 | `NewRibbonBadge` | `UI/DesignSystem/NewRibbonBadge.Windows.cs:373` | 33 ms "جديد" ribbon shimmer (method-group subscription) | no |
| 4 | `ChromeValueLabel` | `UI/PlatformComponents/ChromeText/ChromeValueLabel.Windows.cs:410` | 33 ms brushed-metal sheen on the today counter | no |
| 5 | `LastScanStatus` | `UI/PlatformComponents/LastScanStatus/LastScanStatus.cs:142` | 1 s re-wording of "آخر فحص: منذ …" | no |
| 6 | `PipelineDashboardPanel` (text) | `UI/PlatformComponents/PipelineDashboard/PipelineDashboardPanel.xaml.cs:86` | 250 ms readout refresh | no |
| 7 | `PipelineDashboardPanel` (slide) | same file, `:244` `Animation.Commit(..., finished:)` | panel collapse/expand landing: visibility swap, `EndSlide()`, radar diameter | no |
| 8 | `PipelineRadar` | `UI/PlatformComponents/PipelineRadar/PipelineRadar.xaml.cs:688` | `Animation(_ => OnFrame())` at `rate: 16` — the ~60 fps state tween + canvas invalidation | `OnFrame` had a narrow `try` only around the `AppLifecycleService` lookup |
| 9 | `SourceMorphTransition` (cleanup) | `UI/PlatformComponents/SourceMorphTransition/SourceMorphTransition.Windows.cs:178` | one-shot reveal finalize (`Finish()`, clip restore, composition disposal) | no |
| 10 | `SourceMorphTransition` (refresh) | same file, `:239` | one-shot quiesced re-capture of the reveal cache | no |

Why these are the dangerous ones (all four of the app's historical crash classes apply): they run on a
timer, they touch native visuals (`BoxView.Color`, `TextBlock.Foreground` gradient stops,
`ImageSource`, `Visual.Clip`, composition sprites), they are re-entrant with disposal
(`Handler is null`, ancestors unloaded mid-flight), they keep running while the window is hidden in the
tray, and #1/#2/#8/#9/#10 are **shared static** tickers — a single bad frame does not break one view, it
breaks every instance of that unit, and a throw breaks the process.

---

## 4. Design of the fix, and the options that were rejected

**Chosen: one guard seam, called at the subscription site.**

```csharp
// UI/PlatformComponents/TickerGuard.cs
public static void Run(IDispatcherTimer? timer, string ticker, Action body)
```

Rejected alternatives, with the reason each lost:

| Option | Why not |
|---|---|
| Per-site `try/catch` in all ten callbacks | Ten copies of the same policy, nine of which will drift. The bug class is "somebody forgot"; the fix must make forgetting fail loudly, which a copy-paste idiom cannot. |
| Wrap `IDispatcherTimer` in a custom implementation injected through DI | The timers come from `Application.Current.Dispatcher.CreateTimer()` / `Dispatcher.CreateTimer()`, a MAUI surface, not ours; a wrapper would be a new seam for one method. |
| Subscribe to `Application.UnhandledException` and rely on it | Proven by §2 not to be on this path. This is the mistake the codebase already made once (the September fix), and it is the reason the deaths looked "unexplainable". |
| Stop animating while hidden / reduce frame counts | Orthogonal and already done (`MotionPreferences`, ambient suspension). It lowers exposure; it does not remove the mechanism, and a single frame during a visible session is enough. |
| Let it crash, add a watchdog restart | The watchdog exists (`Platforms/Windows/AppWatchdog.cs`) and is a backstop, not a fix. Silent tray death with a restart loses the user's scroll position and state, and every such death is a WER hunt. |
| Swallow silently forever | Unacceptable: it converts a crash into an invisible bug. Hence the log-first-failure-with-live-stack, and hence the streak limit that parks a permanently broken ticker instead of throwing 30×/s for the life of the process. |

**The policy, verbatim from the unit:**

* The body runs inside a `try`.
* On the **first** failure for a given `ticker` name: `CrashReporter.Report("TickerGuard.<ticker>", ex,
  {ticker, streak}, isFatal: false)` **plus** `InteractionLogger.Fault(...)` with the exception's own
  stack *and* `new StackTrace(true)` of the throwing thread, because the exception's own stack is empty
  whenever native code is the one that threw.
* Reporting is itself wrapped in `try` — a broken diagnostics sink must not become a second way to lose
  the process.
* Success resets the streak. `streak >= 30` parks the timer (`timer.Stop()`), or, for callbacks that own
  no timer (MAUI `Animation.Commit`), `TickerGuard.ShouldStop(ticker)` is used in the animation's own
  `repeat:` condition. 30 ≈ 1 s at 33 ms and 0.5 s at the radar's 16 ms: long enough that a transient
  frame cannot freeze an animation, short enough that a permanent fault stops storming.
* The `ticker` string is the **unit's own name**, never a source id or a platform name, and the guard
  contains no product vocabulary at all — it is a Platform Concept, so a new animated unit uses it with
  zero coupling.

Call-site conversions: `#3`, `#9`, `#10` had their handlers reduced to parameterless
(`OnSharedTickerTick`/`OnCleanupTick`/`OnRefreshTick` never read `sender`/`e`), so the subscription reads

```csharp
_sharedTicker.Tick += (_, _) => TickerGuard.Run(_sharedTicker, "NewRibbonBadge", OnSharedTickerTick);
```

and `#1`/`#2` wrap the whole lambda body. `#7`/`#8` pass `timer: null` and use `ShouldStop` (#8) or rely
on the swallow (#7, a one-shot landing).

---

## 5. Why this is preventive, stated honestly

**No crash observed in this session was caused by one of the app's own tick bodies.** Across every run
after the guard shipped, `TickerGuard` logged **zero** catches — including the runs that still died.
The dumps that killed those runs are named `CXcpDispatcher::MessageTimerCallback`, i.e. WinUI's own
dispatcher pump (§2's chain, but one frame above the app), which is a different path and is documented
as unresolved in the companion document.

What this fix legitimately claims:

1. The mechanism is proven from symbolicated dumps, not theorised: ticks are delivered through a WinRT
   delegate adapter and an escaping exception there fail-fasts.
2. Ten of the app's callbacks were reachable from that mechanism with no handler at all.
3. After the change, they are not reachable, and a new unguarded tick fails a test rather than shipping.

What it does **not** claim: that the published-layout deaths stopped. They did not (two survived long
soaks, three died before the guard's effect could matter — see the companion document §5 for the full
soak table). Saying otherwise would have been the easier and wrong story.

---

## 6. Diagnostics problems found while doing this (three of them were ours)

### 6.1 The app-side handler had no stack to give
`Platforms/Windows/App.xaml.cs` reported `{ e.Message }` and the exception. For a fault arriving from
native code that is a dead end; the handler now also records
`new System.Diagnostics.StackTrace(true)` — the live UI-thread frames — which is what turns "a bare
`InvalidCastException` with no stack" into a named call chain.

### 6.2 The published layout logs were unattributable
Two channels, one log file naming convention, no pid. `Program.LogBirthCertificate` (parent pid via
`NtQueryInformationProcess`), `pid:` on every startup line and `| pid= |` on every interaction line
fixed it; the first crash it identified (`16864`) is the crash that started this investigation.

### 6.3 The first-chance probe filtered on the wrong predicate
`CrashReporter`'s `MOSTAQLK_DIAG_COM=1` probe logged only
`InvalidCastException / COMException / EntryPointNotFoundException`. The fatal HRESULT in the Oct 28
dumps is `0x80004005`, which is what a *plain* `System.Exception` marshals to — so in every run that
died, the probe correctly reported "nothing happened". It now records **every** first-chance exception
(type, `HResult`, message, thread id, own stack, live stack), hard-capped at 400 records, still
opt-in. Noise it revealed on a healthy startup, worth knowing so nobody re-investigates it:

* four `0x80070001` from `Microsoft.Graphics.Canvas.Text.CanvasFontSet..ctor` via
  `Microsoft.Maui.FontManager.FindFontFamilyName` → `UpdateFont` on `ButtonHandler`/`LabelHandler` —
  MAUI probing embedded-font URIs that Win2D rejects, caught by MAUI, harmless, expected in a published
  layout;
* `InvalidOperationException` from `ABI.Windows.ApplicationModel.IPackageStatics.get_Current` /
  `IApplicationDataStatics.get_Current` — unpackaged mode has no `Package.Current`, and
  `Microsoft.Data.Sqlite`'s static constructor probes it through reflection once per AppDomain; caught,
  harmless;
* `OperationCanceledException` / `TaskCanceledException` from Dapper + `Microsoft.Data.Sqlite` during
  pipeline teardown.

---

## 7. Enforcement

`tools/ParserTests/Program.cs` → `TestTickerCallbacksAreGuarded`:

* asserts `UI/PlatformComponents/TickerGuard.cs` exists;
* scans `Core`, `Features`, `Infrastructure`, `Services`, `Sources`, `Platforms`, `UI` for
  `.Tick +=` and fails on any occurrence whose line does not also call `TickerGuard.Run`;
* asserts `guarded >= 8` — the anti-vacuity floor. The suite's own history records a lint that passed
  because it matched nothing (a regex keyed on constant *values* while call sites name the *identifier*,
  so zero sites scanned and zero violations found). A source-scan without a minimum-match floor is not
  a gate.

Docs: rule 6 of `.repertoire/.steering/v3/animation-safety.md` carries the mechanism, the dump chain and
the inner-error pair inline; `docs/UNITS.md` gained the `TickerGuard` row under Platform Components next
to `MotionPreferences` (the two are the animation-safety pair: one decides *whether* a loop runs, the
other decides what happens when a running loop fails).

---

## 8. Verification

| Gate | Result |
|---|---|
| Windows `canary` Debug | Build succeeded, **0 warnings / 0 errors** |
| Windows `stable` Debug | Build succeeded, **0 warnings / 0 errors** |
| `net10.0-android` Debug (forced `-t:Rebuild`) | **0 errors**; exactly the two pre-existing `CS1574` cref warnings (`SourceSwitchTiles`, `Visual`) — verified pre-existing, not introduced here |
| ParserTests | **453 passed / 0 failed** (was 450; +3 from the guard lint) |
| CrashPathTests | all checks pass |
| Lint falsified on purpose | a temporary `UI/PlatformComponents/TickerGuardLintProbe.cs` with one unguarded `timer.Tick += (_, _) => …` produced `FAIL ticker guard: every Tick subscription routes through TickerGuard -- UI\PlatformComponents\TickerGuardLintProbe.cs:9` and `TOTAL: 452 passed, 1 failed`; deleting it returned the suite to **453/0** |
| Coexistence smoke on published dev60 layouts | **GREEN twice**: 150 s and 240 s dual-channel soaks — both channels alive, both mutexes held, both roots present, preferences channel-isolated, zero crash dumps |
| Guard activity in those runs | zero `TickerGuard.*` records — see §5 |

## 9. Follow-ups (deliberately not done here)

1. The `CXcpDispatcher::MessageTimerCallback` path is unresolved; the next concrete step is reading the
   stowed record's attached strings out of a **full** dump
   (`tools/enable-full-dumps.ps1`, then `python scratch/stowed.py <dump>` — the pointer walk already
   exists) and reducing `finalizePending` churn, which was already at **2417 objects one second after
   launch** and **3036 at 24 s** in the heartbeats of these runs.
2. `Animation.Commit` sites are guarded by hand, not by lint. A `.Commit(`-based lint is possible but
   brittle (the callback is an argument, not a subscription); rule 6 carries it instead.
3. `TickerGuard` has no headless unit test: `IDispatcherTimer` is a MAUI dispatcher type and
   `ParserTests` deliberately compiles only MAUI-free sources. The behaviour is covered by the lint,
   the live runs, and reading the unit; adding a fake `IDispatcherTimer` would mean pulling MAUI into
   the test project, which its design exists to avoid.

## 10. Artifacts

| Path | State |
|---|---|
| `UI/PlatformComponents/TickerGuard.cs` | new, committed (dev60) |
| 10 call sites in `UI/DesignSystem/*`, `UI/PlatformComponents/*` | routed, committed (dev60) |
| `Platforms/Windows/App.xaml.cs` | live stack added, committed (dev60) |
| `tools/ParserTests/Program.cs` | `TestTickerCallbacksAreGuarded`, committed (dev60) |
| `.repertoire/.steering/v3/animation-safety.md` | rule 6, committed (dev60) |
| `docs/UNITS.md` | `TickerGuard` row, committed (dev60) |
| `Services/Diagnostics/CrashReporter.cs`, `InteractionLogger.cs`, `Platforms/Windows/Program.cs`, fetcher request marks | committed (dev61) |
| `scratch/dump.py`, `scratch/stowed.py`, `scratch/syms.py`, `scratch/duckrun.ps1`, `scratch/netproof.ps1` | throwaway, deleted with the session (recipes preserved in the companion document) |

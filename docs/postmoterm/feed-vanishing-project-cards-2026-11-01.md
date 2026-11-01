# Postmortem: Projects feed — project cards vanish after opening a project and returning — 2026-11-01

Status: draft (published for review; §17 quality numbers are filled at the 30-day check)
Scope: Windows desktop (MAUI / WinAppSDK), the projects feed page (`MainWindowPage` → `ProjectFeedViewModel`)

---

## 1. What the problem was

Opening a project from the desktop feed and pressing back left the projects list showing **one card over a tall blank field**; in a later and more severe form it showed **no cards at all**. The list's own footer still reported the correct totals ("76 مشروعًا من نفذلي", "25 غير مقروء"), and the underlying database was untouched — so the projects were present and correct while the screen claimed otherwise. Scrolling the feed made the missing cards pop back into view, which is how the fault was eventually characterised.

Two further consequences:

- The first card's **identity changed** under the reader across the same navigation (observed as "Nasser Elsayd" → "Mohamed Ahmed"), i.e. the list re-ranked itself while someone was reading it.
- The same window of instability is the prime suspect for a **native fail-fast** (`0xC000027B`) that killed the app seven times during the investigation. That crash is **not fixed** and is recorded as open in §15 and §16.

User-visible impact: the primary screen of the application rendered empty. No data loss, no corruption, no security impact.

## 2. When and where — traceability

| Field | Value |
|---|---|
| First bad commit | UNKNOWN — not bisected. **Bounded by evidence**: the owner reported the fault at the start of the investigation while running builds at or before `02e94d65c392c176969ee050dfb3b6b44ea4aef2` ("feat: dev69 — the first run asks which platforms to watch and what for"), and it reproduced on `dff4f43c6ac3507f40e3841032ae1c3baa96b45b` (dev73/dev74 instrumented builds). To fill: `git bisect` with the repro in §5. |
| Last good commit | UNKNOWN — no build is known to have been free of the fault. |
| Fix commit | `d10dfebf0a0561009c3c054597564b4b3f4d27be` — "fix: dev80 — the feed stops losing its cards: stable ordering, and the items control rebuilds its containers" (branch `canary`; PR: none — direct branch commit, no hosted review) |
| Hardening commit | `becfbed41d75fd36da070eabab3f2714b1d0929c` — "docs: iwts ledger…" (investigation ledger); diagnostics gating + invariant landed as dev81 (see §11, §16) |
| Versions affected | 1.0.4.dev69 and earlier (unbounded above) through 1.0.4.dev80, both release channels |
| Version fixed | 1.0.4.dev80 (fix) · 1.0.4.dev81 (diagnostics gating) |
| Release channel | `canary` is the integration branch; the reproduction also ran on the `stable` channel build produced by `scripts/run.ps1` |
| Build ID | `ApplicationVersion` 84 (dev80) · 86 (dev81); `ApplicationDisplayVersion` 1.0.4.dev80 / dev81 |
| Artifact hash | UNKNOWN — no checksum is recorded for locally produced binaries; release artifacts are hashed by `.github/workflows` at publish time |
| Built from | local working tree, commit `dff4f43c6ac3507f40e3841032ae1c3baa96b45b…` + uncommitted instrumentation for the instrumented runs; `d10dfebf0a0561009c3c054597564b4b3f4d27be…` for the verified fix build. No CI run produced these — they are developer builds, so shipped-bytes-equals-reviewed-source does not apply |
| Build type | Debug (diagnostics on) and Release (diagnostics off) — both compiled and run, see §14 |
| Toolchain | .NET SDK 10.0.302; MAUI workload; `TargetFramework=net10.0-windows10.0.19041.0`; `SupportedOSPlatformVersion=10.0.17763.0`; exact `Microsoft.Maui.Controls` patch UNKNOWN (resolved from the installed workload, not pinned in `MostaqlK.csproj`) |
| Dependencies | `.NET` + Dapper/SQLite + WinAppSDK via workload; no lockfile hash recorded in the repository |
| Feature flags / config that matter | `MOSTAQLK_TRACE_FEED` (added dev80, semantics changed dev81: Debug on-by-default, Release opt-in) · `MOSTAQLK_DIAG_COM=1` (pre-existing first-chance logger) · `MOSTAQLK_NO_WATCHDOG=1` · `MOSTAQLK_TEST_RADAR_FAULT=1` (pre-existing, was set in some sessions) · `preferences.dat` key `pipeline_panel_is_expanded` |
| OS / build | Windows 10.0.19045 |
| Hardware | Developer workstation, x64, single monitor, client area 1296×840 (the harness normalises to a 1280×800 viewport) |
| Container digest | n/a |
| Cloud region / zone | n/a |
| Security policies in force | Windows Defender Application Control not asserted; **UIPI integrity boundary mattered**: instances launched from an elevated IDE are invisible to a non-elevated automation client (`Get-Process` empty, UIA tree empty), which cost two investigation cycles |
| Network conditions | Live internet required by the polling pipeline; not implicated |

**Reproduce matrix**

| Environment | Version | Reproduces? | Notes |
|---|---|---|---|
| Windows Debug canary, dev74 instrumented | 1.0.4.dev74 | yes | owner's flow captured; data path proven innocent |
| Windows Debug stable (via `scripts/run.ps1`), dev76/dev79 | 1.0.4.dev76, dev79 | yes | 39 `FeedDiag.Collapse` fires in one session |
| Windows Debug canary, dev80 (isolated output) | 1.0.4.dev80 | **no** | 0 of 12 observations bad, six open/back cycles |
| Windows Debug canary, committed artifact | 1.0.4.dev80/81 | **no** | 6 of 6 cycles clean after the cleanup rebuild |
| Windows Release canary | 1.0.4.dev81 | untested for the feed fault | used to prove diagnostics silence (0 diagnostic lines) |
| Android / iOS / maccatalyst | any | untested | the fault lives in the Windows items host; the ordering policy is shared code |

## 3. When it started and how long it lasted

Start is **UNKNOWN** at commit granularity (§2). What can be dated is the affected window of user time:

| Quantity | Value | Source |
|---|---|---|
| Time to detect (TTD) | 0 — the owner saw it directly | user report |
| Time to acknowledge (TTA) | 0 | same session |
| Time to first *instrumented* evidence | ≈ 40 min after the report | first `FeedDiag` line `2026-10-31T22:54:35Z` |
| Time to correct diagnosis | ≈ 6 h 20 min after the report | decisive capture `2026-11-01T04:35:33Z` (stacked containers) → fresh-launch repro `05:17Z` |
| Time to fix committed | ≈ 7 h 37 min after the report | `d10dfebf0a0561009c3c054597564b4b3f4d27be…` authored `2026-11-01T05:51:59Z` |
| Time to remediate (production-safety) | ≈ 8 h 50 min after the report | dev81 gating verified `2026-11-01T08:4xZ` |
| User-visible exposure | the whole period; the feed is the app's landing page, so effectively every launch | owner statement + reproduction |

Duration of the *undetected* phase is unknown and is the honest number to bisect (§16, action A1).

## 4. The interval in which the pattern occurs

The fault is **not** time-driven. It is state-driven, and the state is reached by a specific interaction:

- **Trigger**: the feed page being re-displayed after the details page was pushed on top of it (open a project → press back). Also reachable via a source morph, because that re-points the same list.
- **Amplifier**: any background refine batch that changes row rank (enrichment completing) while the reader is scrolled away from the top — this moved cards out from under the viewport and changed the first card's identity.
- **Non-trigger**: elapsed time alone. An idle feed at the top of the list stayed healthy; that is why early sampling at `t300` looked clean and the fault was initially mischaracterised as intermittent.
- **Frequency once the trigger is followed**: deterministic. The minimal repro produced **11 bad observations out of 12** on dev79/dev80-pre-fix, and **0 of 12** after the fix. It only *appeared* intermittent because the observer had to be scrolled down or have had a rank-changing batch land for the blank field to be visible.

## 5. Reproducibility

**Deterministic minimal repro** (verified on dev79 pre-fix and dev80 post-fix):

1. Launch the app (either channel).
2. Wait for the projects feed to paint (≥ 2 cards visible).
3. Open the first project card.
4. Press the details page's back button.
5. Observe the feed: one card, blank below — and every subsequent open/back keeps it that way.

**Harness** (UI Automation against the running process; counts only cards that have a real rectangle inside the feed's own bounds, because the collection's item count is the trap that passed while the screen was empty):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scratch/uia_feed_check.ps1 -Mode loop -ProcessId <pid> -Loops 6
```

Pre-fix output (dev79, fresh launch):

```text
loop 1 before:     items=9 sized=2 distinct=2 inside=2 emptyState=False -> OK
loop 1 after-back: items=9 sized=9 distinct=1 inside=9 emptyState=False -> STACKED
...
BAD STATES: 11   invalid iterations (card never opened): 0
```

Post-fix output (dev80, same build path, six cycles):

```text
loop 1 after-back: items=9 sized=2 distinct=2 inside=2 emptyState=False -> OK
...
BAD STATES: 0   invalid iterations (card never opened): 0
```

**Reproduced by**: the owner (original report) and by the agent independently, without the owner present.

## 6. How deep — layer and blast radius

**Layer**: two, and conflating them is what delayed the diagnosis.

1. **Presentation** — the Windows items host behind MAUI's `CollectionView`. Its subtree is **`ItemsRepeater`-based**: the visual-tree walk recorded 17 `ItemsPresenter` nodes and **zero `ListViewBase`** nodes. Containers existed, were loaded, were `Visible`, had correct size, and all reported the same position.
2. **Data→view policy** — the feed adopted the store's ranking on *every* batch, including background refine batches, so rows moved under the reader.

**Not implicated, each proven by measurement**: the SQLite repository and its queries; `SourceFeedStore`'s generation guard and coalescing; the snapshot diff-apply's row bookkeeping; source switching; navigation stack identity; the view-model's collection contents.

**Blast radius**: the projects feed page on Windows desktop. No persisted state was affected — `mostaqlk.db`, `preferences.dat`, cookies and the pipeline were untouched, and every captured session showed the view-model holding the full row set (45/40/76 rows) while the screen showed one card. Recovery required no data repair: scrolling or re-launching restored the view.

**Adjacent surface that was *wrongly* blamed during the investigation** (§8, §13): the mount animation unit `ContentSettle`, whose compositor pass had been the standing suspect in this codebase for crash classes past.

## 7. The pattern

**Two independent conditions had to coincide** — this is a causal chain, not one sentence:

- **P1 — a live list re-ranks itself underneath its reader.** The recent-page query orders rows by *enrichment completion* (`(enriched_at IS NULL) ASC, enriched_at DESC, discovered_at DESC`), and enrichment finishes continuously while the app runs. Every completion therefore re-ranks the whole page, and the renderer adopted that order on every batch.
- **P2 — the items host's container set goes stale when the page is re-displayed.** After the details page is popped away and the feed page re-appears, the host reports every realized container at one position. The view-model is correct; the control's index→container mapping is not.

P1 explains the identity swap and makes the blank region *reachable* (the reader is looking at an offset whose content moved away). P2 explains the blank field itself. Neither alone produces the reported symptom, and fixing only one was measurably insufficient (§13).

The generalisable pattern: **a feed whose sort key is mutable at runtime must not re-sort a list someone is reading**, and **a virtualized/reused item host must be told to rebuild when its host page is re-activated** — inferring the latter from "the data is right, so the view must be right" is the trap.

## 8. How it happened

The mechanism, in the order it was discovered:

1. **The data path was cleared first, by instrumentation rather than argument.** Checkpoints at every store publish and every renderer apply (`FeedDiag.Request/QueryResult/Publish/Apply/ApplyEnd`) showed, in the owner's own session: `listCount=44 → Publish items=44 → Apply paintable=44 → ApplyEnd rowsAfter=44`, one view-model instance, one subscriber, no dropped batch, no search query, no error/empty state. That eliminated three of the four initial hypotheses and is the reason the hunt did not end in a speculative patch.
2. **The first repair was aimed at the wrong layer.** Because the store legitimately re-publishes the *same* generation twice (the stale-while-revalidate dim and the card-tap optimistic-read overlay) and the renderer's guard dropped only *strictly older* generations, the renderer was re-running a full structural patch repeatedly — measured at **31 patch passes with one generation patched 7 times**, using `RemoveAt`/`Insert`/`Move` per row. Removing the `Move` storm was a real improvement to the code, but it did **not** fix the reported symptom; the fault still reproduced 11/12.
3. **Geometry, not existence, was the missing measurement.** Sampling "how many containers exist" reported a healthy `realized: 12, visible: 12` while the screen was empty. Adding per-container *position* exposed the truth: `realized: 9, distinctY: 1` — nine distinct container objects (`distinctContainers: 9`) all at the same place, with `panelChildren: 2` showing that the panel the probe had resolved was not the one laying the cards out.
4. **A screenshot ended the inference.** Capturing the running window showed one fully rendered card over a blank field, matching the owner's report exactly, while the app's own log at the same instant reported the list healthy. Pixels settled what three layers of instrumentation had only narrowed.
5. **The owner's own observation supplied the second cause.** "The first project was Nasser Elsayd, then it was Mohamed Ahmed" identified the re-ranking, which no counter had been watching — the trace recorded counts and positions but not *which row moved to which index*. Adding that field (`FeedTrace.Patch` with `moved`, `topBefore`, `topAfter`) confirmed it immediately.
6. **Why the eventual repair is the shape it is.** The only operation that reliably restored the view was making the items control rebuild its containers. Doing it by nulling and re-attaching `ItemsSource` worked twice and then lost the feed permanently (assigning in code replaces the XAML binding). Handing the control a **fresh collection holding the same card instances** rebuilds containers without disturbing bindings, and card identity survives so nothing re-animates.

**Second story for the decisions that were wrong.** The `Move`-storm theory was *reasonable* at the time: it was backed by a real measurement (31 patches, one generation 7×), by a known class of WinUI collection-change bugs, and by the codebase's own history of compositor-animation crashes, which pointed suspicion at anything touching item containers. It was abandoned only because the repro kept failing after the change — the discipline that saved the investigation was having a deterministic, measurable repro rather than a plausible story. Equally, the earlier probes were not wasted: each one *removed* a suspect, and the geometry probe is what made the screenshot interpretable.

## 9. Cause category, with evidence

| Category | Verdict | Evidence |
|---|---|---|
| Code — data-layer defect | **ruled out** | every capture shows the full row set through store → renderer (`rowsAfter` = 44/45/76) |
| Code — presentation policy (re-ranking under the reader) | **confirmed contributing cause (P1)** | `FeedTrace.Patch` before: background batches adopted new ranks and the first card's id changed; after: `reorder=False, moved=0, topBefore==topAfter` |
| Third-party framework interaction — items host keeps a stale container set on page re-display | **confirmed contributing cause (P2)** | `realized: 9, distinctContainers: 9, distinctY: 1` at `scrollOffset: 0` with a healthy view-model; screenshot shows one card over blank; repair that rebuilds containers clears it (11/12 bad → 0/12) |
| Concurrency / race | **ruled out as the cause** (a same-generation re-publish amplification existed and was fixed, but the fault persisted without it) | repro is deterministic and single-threaded on the UI path |
| Configuration / environment | **ruled out**; environment did obstruct *investigation* (UIPI integrity boundary, `MainWindowHandle` unavailable for WinUI3 windows) | elevated instances invisible to non-elevated automation |
| Data loss | **none** | database and settings untouched; view recovered on scroll/relaunch |
| Security | **none from the fault**; **one real risk created by the investigation** — see §16 A3 | hunt diagnostics emit project ids and navigation history to a plaintext log and were on-by-default in all configurations until dev81 |

**Root cause confidence: strongly supported**, not proven. Strongly supported because both halves were independently manipulated: fixing P2 alone and P1 alone left the repro failing, fixing both cleared it 12/12, and the mechanism was observed directly (positions + screenshot). Not *proven* because the framework-side reason the host keeps a stale container set was never traced into MAUI/WinUI internals — the repair works by rebuilding around it rather than by removing it. Falsifier for the current model: a build where the container set is *not* rebuilt and the fault does not reproduce, or a trace showing the container map differing from the item list while the screen is correct.

## 10. Scripts, tools and commands used to FIND the bug

| Tool / command | What it answered | What it showed |
|---|---|---|
| `grep -E "FeedDiag" %LOCALAPPDATA%\MostaqlK*\1.0.4.dev*\log\*\interaction-log.txt` | did the data collapse? | no — full row counts at every stage |
| `powershell -NoProfile -ExecutionPolicy Bypass -File scratch/uia_feed_check.ps1 -Mode loop -ProcessId <pid> -Loops 6` | is the fault reproducible and countable? | 11/12 bad pre-fix, 0/12 post-fix |
| `… -Mode repro -Source mostaql` / `-Mode scroll -ScrollSteps 30` | does it survive morph + scroll stress? | healthy post-fix (`realized: 9, distinctY: 9, inViewport: 2`) |
| `tools/capture_page.py --pid <pid> --output scratch/broken-now.png` (PrintWindow capture) | what does the user actually see? | one rendered card over a blank field |
| `ContentSettle.Probe` → `FeedDiag.NativeList` (`TransformToVisual`, `IsLoaded`, parent type, root-relative point) | are the containers placed? | `distinctContainers: 9` at `distinctY: 1`, `panelChildren: 2` |
| `ContentSettle.WatchContainers` → `FeedTrace.Container` | is the container↔item binding sane? | containers bound, loaded, recycled normally |
| `FeedTrace.Hosts` visual-tree walk | which control lays these cards out? | 17 `ItemsPresenter`, **0** `ListViewBase` → repairs aimed at a list host were touching the wrong control |
| `python scratch/dump_probe.py %LOCALAPPDATA%\CrashDumps\MostaqlK.exe.<pid>.dmp` | what killed the app? | `0xC000027B` at `Microsoft.UI.Xaml.dll+0x39CDE5`, identical offset in 7 dumps; earlier `E_INVALIDARG` (`0x80070057`) |
| `MOSTAQLK_DIAG_COM=1` (pre-existing first-chance logger) | was a managed exception the trigger? | no managed exception precedes the fail-fast |
| `python scratch/win_probe.ps1` equivalent (`powershell -File scratch/win_probe.ps1`) | is the instance visible/attachable? | WinUI3 windows have no `MainWindowHandle`; elevated instances are invisible to a non-elevated client |
| `dotnet build -p:Channel=canary -o bin/FeedHunt` | how to test without disturbing the owner's instance | channel isolation (separate data root/mutex) let both run side by side |

**Dead ends, recorded deliberately**: `LinearItemsLayout` (disabling virtualization) — still stacked, so the virtualizer was never at fault; `InvalidateMeasure`/`InvalidateArrange`/synchronous `UpdateLayout` on the native host — no effect; a sub-pixel `ScrollViewer.ChangeView` nudge — no effect; a `HeightRequest` nudge — no effect; pop-instead-of-absolute back navigation — still stacked, reverted. A hand-rolled x64 stack scan of the minidump (`scratch/stack_scan.py`) produced image bases rather than return addresses (wrong thread-record stride and `CONTEXT` offset) and was abandoned rather than trusted.

## 11. How it was solved

Two changes, both in the feed's own boundary, no new abstraction:

1. **Order stability (`ProjectFeedViewModel`)** — a `_reorderPending` intent flag, armed on cold open, source morph, explicit refresh and search. A background batch now keeps the reader's rank: existing rows hold position, vanished rows drop out, newly arrived rows go on top. Full re-sort only on user intent.
2. **Container rebuild (`ProjectFeedViewModel.RebuildItemsContainer`, called from `MainWindowPage.OnAppearing`)** — assign a *new* `ObservableCollection` holding the *same* card view-model instances. `Projects` became a settable observable property for exactly this reason. The items control rebuilds its container set; the page's bindings are untouched; card identity survives so the mount animation does not replay.

Rollback: revert `d10dfebf0a0561009c3c054597564b4b3f4d27be…`. Both changes are additive to behaviour the page owns; no schema, no persisted key, no route contract changes, so rollback needs no data step. The dev81 diagnostics gating is independently revertible.

**Does it remove the cause or hide the symptom?** Honest split: change 1 **removes** a cause (the re-ranking policy was wrong on its own terms). Change 2 **repairs around** a framework-side fault whose internal mechanism was not identified — the container set is rebuilt rather than the staleness being eliminated. That is stated as a known limitation, not a finished root-cause fix, and is why §9 says "strongly supported" rather than "proven".

## 12. Scripts, tools and commands used to FIX and release

```bash
# build the fix in isolation (channel isolation keeps it off the owner's instance)
dotnet build -f net10.0-windows10.0.19041.0 -c Debug -p:Channel=canary -o bin/FeedHunt

# the repository's one command, scoped to the working diff
powershell -NoProfile -ExecutionPolicy Bypass -File tools/quality-gate.ps1 -FormatChanged

# release-configuration build used to prove the diagnostics gate compiles and behaves
dotnet build -f net10.0-windows10.0.19041.0 -c Release -p:Channel=canary -o bin/RelCheck

# commit on the integration branch (never direct to main)
git add .iwts/state.md CHANGELOG.md Features/ Services/ UI/ MauiProgram.cs MostaqlK.csproj \
        MostaqlK.UITests/FeedBackNavigationRegressionTests.cs
git commit -m "fix: dev80 — the feed stops losing its cards…"
```

Result: build 0 warnings / 0 errors (Debug and Release); quality gate `ParserTests PASS · CrashPathTests PASS · format PASS`; commits `d10dfebf0a0561009c3c054597564b4b3f4d27be…` and `becfbed41d75fd36da070eabab3f2714b1d0929c…` on `canary`, **not pushed**.

## 13. Heuristic or verified?

**Verified**, with the caveat that the framework-side mechanism was not traced to its internals (§11).

What makes it verification rather than a plausible fix: a repeatable, quantified measurement taken before and after on the same build path — 11 of 12 observations bad → 0 of 12 good, re-run after dead-code removal, re-run on the committed artifact (6/6), plus two adjacent stress paths (source morph, 30-step scroll) that stayed healthy. Six alternative repairs were *falsified* by the same measurement rather than by argument, which is what distinguishes this from the earlier heuristic attempts.

## 14. How it was verified

| Check | Method | Result |
|---|---|---|
| Reported fault no longer occurs | UIA loop, six open/back cycles, counting cards with real rectangles inside the feed bounds | 0 bad / 12 observations (was 11 bad / 12) |
| Holds after cleanup | same harness on a rebuild with the dead repairs removed | 0 bad |
| Holds on the committed artifact | same harness against `bin\Debug` built from the committed tree | 6/6 cycles OK |
| Source morph path | `-Mode repro` | healthy (`realized: 9, distinctY: 9, inViewport: 2, stuck: false`) |
| Scroll stress | `-Mode scroll -ScrollSteps 30` + 40 s of background batches | healthy |
| Re-ranking gone on background batches | `FeedTrace.Patch` | `reorder=False, moved=0, topBefore==topAfter` |
| Renderer churn reduced | `FeedDiag.Patch` counts | 41 content-only vs 4 reset in one session (was 31 patches with one generation applied 7×) |
| Regression test exists | `MostaqlK.UITests/FeedBackNavigationRegressionTests.cs` | present and compiling; **not runnable in this environment** while the open crash kills the Appium session (§15) |
| Quality gates | `tools/quality-gate.ps1 -FormatChanged` | ParserTests 614/0 (+3), CrashPathTests pass, format pass |
| **Diagnostics do not reach production** | Release build run 40 s vs Debug build run 40 s, same version tag `1.0.4.dev81+becfbed41d75fd36da070eabab3f2714b1d0929c…` | Release: 43 log lines, **0** `FeedDiag`, **0** `FeedTrace`. Debug: 131 lines, 45 `FeedDiag`, 10 `FeedTrace` |
| The production guard is real, not decorative | break-proof: inject one ungated `InteractionLogger.Mark("FeedDiag…` emitter | scan failed at that exact line; reverting returned the suite to 614/0 |

**Evidence level: V4** — the fix is demonstrated by a purpose-built repro harness run before and after, on the committed artifact, with adjacent stress paths, and by an enforced build-time invariant for the remediation. It is not at the highest level, because the change does not remove the *class* of fault: the repair rebuilds the container set around the framework-side staleness rather than eliminating it, so a MAUI/WinAppSDK update that changes item-container reuse could resurface the same symptom. Nor can the level rise while the regression tests cannot execute in CI here (the open crash destroys their session), there is no soak/long-run evidence, and the framework-side mechanism is unrepaired — a future MAUI/WinAppSDK update could change the container-reuse behaviour the rebuild works around.

## 15. Rare or untestable bugs

**The feed fault itself turned out not to be rare** — it was deterministic once the trigger (open → back) was known, and only looked intermittent because visibility depends on scroll position and on whether a rank-changing batch had landed. That reframing is the single most useful fact in this document: it converted a soak problem into a two-minute repro.

**The crash is the genuinely hard one, and it is still open.**

- Signature: `0xC000027B` (`STATUS_STACK_BUFFER_OVERFLOW`-family fail-fast used by WinRT's `RoFailFastWithErrorContext`) at `Microsoft.UI.Xaml.dll+0x39CDE5` — the *same offset* in all seven dumps, i.e. one call site.
- Seven occurrences: pids 22068, 27964, 3048, 9928 (owner session), 14100, 28588, 13628 (investigation). Three related dumps carry a managed `E_INVALIDARG` (`0x80070057`).
- It fired with the app **idle** (pid 13628: feed settled, healthy, no interaction, no managed first-chance exception) and, separately, during heavy scrolling on the owner's machine.
- **MTBF is not estimable from this sample** — occurrences cluster around feed activity but the denominator (instance-hours observed) is roughly 3–4 hours of mixed instrumented running, so any rate would be noise. Per the rule of three, zero further failures in that window would put the 95 % upper bound near one failure per ~4 instance-hours, which is not a useful safety claim.
- **What was tried and did not resolve it**: no debugger is installed (`cdb.exe`/WinDbg absent), and a hand-written minidump stack walk produced image bases instead of return addresses, so the faulting call chain was never symbolised. Full dumps (`tools/enable-full-dumps.ps1`) would give heap and stacks but are a machine-level registry change and were not made unasked.
- **A documented procedure was available and was not used.** This repository already writes up how to resolve a minidump *without* a debugger: `docs/postmoterm/published-layout-dispatcher-failfast-2026-10-28.md` section 12 (hand-parsing the dump, following the stowed-exception pointer, resolving symbols with `dbghelp`/`symsrv`). The investigation substituted its own throwaway parser instead of following that procedure, and failed on problems the existing write-up already solves. That is a process finding, folded into action A4.
- **A/B that was inconclusive**: an attempt to test whether the mount-animation pass is implicated failed because the launches did not start (a process-detection artifact, later understood), not because the hypothesis was tested. It remains untested.
- **Relationship to the fixed fault**: the corrupted container set is the leading suspect for the `E_INVALIDARG` family, so dev80/81 may reduce the crash rate. **This is unproven and is explicitly not claimed as fixed.** Re-open criteria: any new dump at `Microsoft.UI.Xaml.dll+0x39CDE5` after dev81, or any `E_INVALIDARG` dump.
- **"This crash cannot be fully verified as fixed by the change above" is the honest sentence here**, and the reason it is recorded as an open action with an owner rather than closed.

## 16. Prevention

| # | Type | Action | Owner | Due | Ticket | Definition of done |
|---|---|---|---|---|---|---|
| A1 | prevent | Bisect the first-bad commit and confirm the affected range in §2 (currently UNKNOWN) | alaamer12 (repo owner) | 2026-11-15 | UNKNOWN — no tracker configured in-repo | `git bisect` log recorded in this file, §2 first-bad cell filled with a full SHA |
| A2 | prevent | Make `MostaqlK.UITests/FeedBackNavigationRegressionTests.cs` runnable in CI: it currently cannot execute while the open crash kills the Appium session; wire it behind a green quality-gate step | alaamer12 | 2026-11-22 | UNKNOWN | the three tests execute in CI and fail when the repro is re-introduced (prove by re-adding a `Move` patch) |
| A3 | mitigate | **Remove the hunt diagnostics once the owner confirms the fix** — `Services/Diagnostics/FeedTrace.cs`, all `FeedDiag.*` marks in `SourceFeedStore`/`ProjectFeedViewModel`/the feed and details layouts, `ContentSettle.Probe`/`WatchContainers`/`CountHosts`, and `scratch/` probes. Keep the invariant, repurposed to guard any future diagnostic emitter | alaamer12 | 2026-11-08 | UNKNOWN | no `FeedDiag`/`FeedTrace` emitter remains in app code; the ParserTests scan still passes (anti-vacuity floor lowered to match) |
| A4 | detect | Investigate the open fail-fast: enable full dumps (`tools/enable-full-dumps.ps1`, restore afterwards), reproduce, symbolise; or install a debugger so `Microsoft.UI.Xaml.dll+0x39CDE5` resolves to a call site | alaamer12 | 2026-11-22 | UNKNOWN | a root cause named for the fail-fast, or a documented decision to accept it with a re-open rule |
| A5 | prevent | Encode the ordering rule as a product invariant: *a live feed must not change the rank of rows already on screen without user intent.* Add a ParserTests scan forbidding an unguarded `Projects.Move(`/re-rank on the background path | alaamer12 | 2026-11-22 | UNKNOWN | scan present, break-proved, and the steering doc that owns feed behaviour states the rule |
| A6 | learn | Record in `.repertoire/.steering/v3/animation-safety.md` (or the feed doc) that the feed's items host is `ItemsRepeater`-based with **no `ListViewBase`**, so future repairs must not target a list host — this cost six falsified attempts | alaamer12 | 2026-11-08 | UNKNOWN | steering updated with the tree-walk evidence |
| A7 | learn | Adopt "measure existence *and* position" for any UI-vanishing report, and capture a screenshot of the live window early; both were cheaper than the reasoning they replaced | alaamer12 | 2026-11-08 | UNKNOWN | this document referenced from the next UI-vanishing investigation |

At least one **mitigate** (A3) and one **prevent** (A2/A5) are present; seven actions, within the eight cap. No action is closed: all are open with owners, and closure will require evidence, not a status change.

**Detection analysis — why was this not caught earlier?** Three reasons, all systemic rather than individual. (1) No test asserted on *rendered* items: the natural assertion is a collection count, and the collection count was correct in every failing capture — the existing suite could only ever have passed. (2) The feed's ordering key is mutable at runtime (enrichment completion), so "the list re-ranks itself" was the default behaviour rather than a recognised hazard, and nothing in the steering docs said otherwise. (3) The observable symptom was in a different layer from its cause, so the first plausible suspect (the animation/compositor surface, which has a documented crash history in this codebase) absorbed the early attention.

**Known risks accepted**: none disclosed in advance for this fault. The closest prior art is the repository's own animation-safety catalogue, which documents compositor/transform crash classes and thereby made the wrong layer look well-founded — a case where a good document increased confidence in a wrong hypothesis.

**Lessons learned**
- *Went well*: instrumentation over argument — every layer was cleared or convicted by a measurement; the data path was exonerated before anything was changed; the repro was made deterministic and countable; dead ends were recorded rather than hidden; the diagnostics leak was caught and gated in the same session.
- *Went poorly*: six repairs were attempted against a control that the visual-tree walk could have shown was the wrong one in the first five minutes; the first geometry probe measured existence and not position, which produced a "healthy" reading of a broken screen and delayed the diagnosis by hours; the minidump stack walk was attempted without the tooling to support it.
- *Where we got lucky*: the owner's two observations ("scrolling brings them back", "the first project changed identity") each pointed straight at a cause that no counter was yet recording.
- *Surprises*: `CollectionView` on Windows is `ItemsRepeater`-based here, not `ListViewBase`; UIA `Invoke` on a list item does not raise MAUI's tap gesture (a coordinate click does), which nearly produced a false "fixed" reading; and an elevated instance is invisible to a non-elevated automation client in a way that looks like a dead app.

**Related incidents**: `docs/postmoterm/published-layout-dispatcher-failfast-2026-10-28.md` (same fail-fast family, layout/dispatcher path); `docs/postmoterm/long-idle-background-crash-full-investigation.md` (compositor churn); `docs/postmoterm/radar-iterationcount-stow-failfast-2026-10-31.md` (a caught exception that was invisible — the precedent for "instrument before theorising").

**Open questions**
1. What is the call chain behind `Microsoft.UI.Xaml.dll+0x39CDE5`? — owner: alaamer12, needs A4 (full dumps or a debugger), by 2026-11-22.
2. Does dev80/81 reduce the crash rate, or only the visible fault? — owner: alaamer12, answerable after A2 gives CI coverage.
3. Should the recent-page sort key stop being enrichment-completion (the property that makes a live feed churn), or should the UI freeze ranks for a session? — owner: alaamer12, product decision, by 2026-11-22.

## 17. Supporting sections

**Metadata**

| Field | Value |
|---|---|
| Incident ID | UNKNOWN — no incident tracker in this repository; proposed `FEED-VANISH-2026-11-01` |
| Status | draft → review |
| Severity | SEV-3 (justification: primary screen renders empty for the sole known user; no data loss, no security impact, trivially recoverable by relaunch/scroll; would be SEV-2 for a multi-user deployment) |
| Authors / reviewers | Author: Qoder agent session, 2026-11-01. Reviewer: alaamer12 (pending) |
| Detection | user report |

**Impact**

| Dimension | Value |
|---|---|
| Users affected | 1 of 1 known user (the owner); 100 % of launches reached the feed page |
| Duration of user-visible impact | ≥ since dev69, closed at dev80 — commit-granular range UNKNOWN (A1) |
| Data integrity / loss | none; database, settings and cookies verified untouched |
| Security impact | none from the fault. **One from the investigation**: hunt diagnostics wrote project ids, navigation history and scroll offsets to a plaintext log, on by default in all configurations, from dev74 to dev80 — remediated in dev81 (§11, A3) |
| SLA / SLO breach | n/a |
| Cost | ≈ 8.5 hours of investigation time |

**UTC timeline, with sources**

| UTC | Event | Source |
|---|---|---|
| `2026-10-31T22:15Z` (approx) | Owner reports: open project → back → cards vanish except one | conversation |
| `2026-10-31T22:54:35Z` | First instrumented session (dev74, pid 15188) | `interaction-log.txt` |
| `2026-10-31T23:00:31Z` | Owner reproduces the exact flow (pid 16508): data proven intact | `interaction-log.txt` |
| `2026-10-31T23:11Z` | Two fail-fast dumps (22068, 27964) | `%LOCALAPPDATA%\CrashDumps` |
| `2026-11-01T00:00Z` | Owner hands over full autonomy (`/iwts`) | conversation |
| `2026-11-01T02:2xZ` | dev75 `Move`-storm repair; repro still 11/12 bad | `FeedDiag.Patch`, harness |
| `2026-11-01T04:21:50Z` | Owner's dev76 run: 39 `FeedDiag.Collapse` fires | `interaction-log.txt` |
| `2026-11-01T04:35:33Z` | Decisive geometry: containers at `y = -scrollOffset`, `distinctY: 1` | `FeedDiag.NativeList` |
| `2026-11-01T04:49Z` | Screenshot: one rendered card over a blank field | `scratch/broken-now.png` |
| `2026-11-01T05:17Z` | Fresh-launch repro made deterministic: 11/12 bad | harness `-Mode loop` |
| `2026-11-01T05:33Z` | Fail-fast during UI-test run (pid 13628), app idle and healthy beforehand | dump + `interaction-log.txt` |
| `2026-11-01T05:5xZ` | Both causes fixed: 0/12 bad | harness |
| `2026-11-01T05:51:59Z` | Fix committed `d10dfebf0a0561009c3c054597564b4b3f4d27be…` on `canary` | git |
| `2026-11-01T05:55:35Z` | Ledger committed `becfbed41d75fd36da070eabab3f2714b1d0929c…` | git |
| `2026-11-01T06:53Z` | Committed artifact re-verified 6/6 | harness |
| `2026-11-01T08:2x–08:4xZ` | Diagnostics gated dev-on/release-off; Release 0 lines vs Debug 55; invariant break-proved; gate green | §14 |

**Key durations**: TTD 0 · TTA 0 · TTI ≈ 6 h 20 min (report → correct mechanism) · TTM ≈ 7 h 37 min (report → fix committed) · TTR ≈ 8 h 50 min (report → production-safety remediated).

**Sign-off**

| Role | Name | Date |
|---|---|---|
| Author | Qoder agent session | 2026-11-01 |
| Reviewer | alaamer12 (pending) | — |

**Revision history**

| Version | Date | Change | Author |
|---|---|---|---|
| 1.0 | 2026-11-01 | initial draft, published for review while evidence for §15 (crash) is still arriving | Qoder agent session |

## 18. Files and artifacts

Folder mode not used — this document is self-contained and cites repository files by path plus full commit SHA. The investigation scripts live in `scratch/` (git-excluded by design) and are quoted with their exact invocation in §10; if they must outlive the cleanup action A3, they should be moved into a tracked tools directory at that point rather than copied into a package.

| Artifact | Location | Commit |
|---|---|---|
| Fix | `Features/Projects/ViewModels/ProjectFeedViewModel.cs`, `Features/Projects/Views/MainWindowPage.xaml.cs` | `d10dfebf0a0561009c3c054597564b4b3f4d27be` |
| Diagnostics gate | `Services/Diagnostics/FeedTrace.cs`, `MauiProgram.cs` | dev81 |
| Invariant | `tools/ParserTests/Program.cs` → `TestFeedDiagnosticsAreGatedOutOfRelease` | dev81 |
| Regression tests | `MostaqlK.UITests/FeedBackNavigationRegressionTests.cs` | `d10dfebf0a0561009c3c054597564b4b3f4d27be` |
| Investigation ledger | `.iwts/state.md` | `becfbed41d75fd36da070eabab3f2714b1d0929c` |
| Changelog record | `CHANGELOG.md` — dev74 → dev81 sections | as above |
| Crash dumps (not committed; contain memory images) | `%LOCALAPPDATA%\CrashDumps\MostaqlK.exe.{22068,27964,3048,9928,14100,28588,13628}.dmp` | n/a |

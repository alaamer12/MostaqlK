# iwts state — objective: make the MostaqlK desktop feed stop losing its project cards

status: complete (feed bug fixed, verified, committed) — one open item, see "still open"
commits on `canary` (not pushed): d10dfeb fix (dev80) · becfbed ledger · 0273982 diagnostics gating (dev81) ·
ae97d44 postmortem + AGENTS config surface
owner: asleep at handoff 2026-11-01 ~02:00; woke briefly ~06:25 to confirm the bug was live and to
point at the re-ranking cause; full autonomy granted ("fix all problems you find").

## what was delivered

- [x] Root-caused the reported bug in two parts:
      (1) the feed re-ranked itself under the reader (background refine batches adopted the store's
      enrichment-completion order), and
      (2) the items control kept stale item containers after the page came back, so every realized
      container reported one position — one card visible, the rest "gone", while the view-model
      correctly held all 45 rows.
- [x] Fix 1: order stability. Background batches preserve the reader's rank, drop vanished rows, put
      new arrivals on top; a full re-sort happens only on cold open, source morph, refresh, search
      (`ProjectFeedViewModel._reorderPending`).
- [x] Fix 2: `ProjectFeedViewModel.RebuildItemsContainer()` — a fresh collection of the same card
      instances on page appear, which makes the items control rebuild its containers without touching
      the page's bindings or replaying the mount animation.
- [x] Verified with a UIA harness that counts only cards having a rectangle inside the feed's own
      bounds: **11 of 12 observations bad before → 0 of 12 after**, re-run after dead-code cleanup,
      and 6/6 on the committed `bin\Debug` artifact; a source morph and a 30-step scroll stress both
      stayed healthy (`realized: 9, distinctY: 9, inViewport: 2, stuck: false`). Screenshots confirm
      multiple distinct cards where the owner saw one.
- [x] Gates: `quality-gate.ps1 -FormatChanged` — ParserTests PASS, CrashPathTests PASS, format
      (12 changed .cs) PASS; Windows canary Debug 0 warnings / 0 errors.
- [x] Regression cover added: `MostaqlK.UITests/FeedBackNavigationRegressionTests.cs` (asserts on
      realized on-screen cards, not collection counts).

## decisions recorded

- **Preserve the reader's rank; re-sort only on user intent.** Chosen over scroll-to-top (throws the
  reader away from the card they are reading, continuously, because enrichment re-ranks every few
  seconds) and over suppressing refresh entirely (the feed must still show new projects).
- **Rebuild containers via a new collection instance**, not by nulling `ItemsSource`. Nulling fixed
  the stacking and then permanently lost the feed a few cycles later; the swap keeps bindings intact.
- **Absolute-route back navigation kept.** Pop-instead-of-absolute was tested and did not fix the
  bug, so the contract's deliberate absolute-route choice stands.
- **`FeedTrace` ships on by default** (silenced by `MOSTAQLK_TRACE_FEED=0`) so the owner can confirm
  with zero setup. It is hunt scaffolding and must come out next build.

## rejected after measurement (do not retry)

Null + re-attach `ItemsSource` · `InvalidateMeasure`/`InvalidateArrange`/synchronous `UpdateLayout` on
the native host · sub-pixel `ScrollViewer.ChangeView` nudge · `HeightRequest` nudge ·
`LinearItemsLayout` (virtualization off — still stacked, so the virtualizer was never at fault) ·
pop-based back navigation. Key reason several of these were futile: the feed's subtree is
`ItemsRepeater`-based (17 `ItemsPresenter`s, **zero** `ListViewBase`), so repairs aimed at a native
list host were touching the wrong control.

## still open

1. **The native fail-fast is NOT fixed.** `0xC000027B` at `Microsoft.UI.Xaml.dll+0x39CDE5` — six owner
   dumps plus one during the UI-test run (pid 13628, 07:33), identical offset every time, no managed
   first-chance exception, and the instance was idle with a settled, healthy feed when it died. It
   predates this work. The corrupted container set is the prime suspect, so this build may reduce it,
   but that is unproven.
   Blocked on: a debugger/symbolizer (no `cdb.exe`/WinDbg on this machine) or full dumps
   (`tools/enable-full-dumps.ps1`, a machine-level registry change I did not make unasked).
   The repo's Appium suite cannot be used as regression cover while this crash kills its session —
   all 27 UI tests failed on `Currently selected window has been closed`, which is this crash, not a
   code regression (untouched About/Settings tests failed identically).
2. **A/B of the settle pass against the crash was inconclusive** — my launch loop reported no running
   process in all four runs, which was a detection artifact (the same build launches fine via
   `Start-Process`, confirmed at 07:53). Redo it with `Start-Process` if pursuing the crash.

## corrected finding

- **The pipeline dashboard is NOT clipped.** I told the owner it looked like an RTL track-ordering
  bug; it is not. `PipelineDashboardPanel.RailWidth = 44` is the collapsed panel, and the thin strip
  with the dots and `0%` at the window's right edge is that collapsed state working as designed.
  No action taken, and none should be.

## cleanup owed next build

`Services/Diagnostics/FeedTrace.cs`, the `FeedDiag.*` marks in `SourceFeedStore` /
`ProjectFeedViewModel` / the feed and details layouts, `ContentSettle.Probe` / `WatchContainers` /
`CountHosts`, and `scratch/` (`uia_feed_check.ps1`, `live_feed_probe.py`, `dump_probe.py`,
`win_probe.ps1`, `probe_api.ps1`, `stack_scan.py`) are diagnostics, not product surface. Remove once
the owner confirms the feed behaves; keep the `FeedBackNavigationRegressionTests`.

# iwts state — objective: make the MostaqlK desktop feed stop losing its project cards

status: running
owner-handoff: 2026-11-01 ~02:00 (+02:00), owner asleep; full autonomy granted ("ensure the bug is full fixed",
"Appium may not be reliable and ps1 files maybe more reliable — your decision", "fix all problems you find").

## remaining

- [x] Diagnose: data path cleared (store/VM keep all rows in every captured session)
- [x] Diagnose: renderer cleared of "never arranged" theory (dev78 InvalidateMeasure did NOT fix it)
- [x] Root cause found from dev78 geometry: after a structural patch the WinUI items panel keeps the
      ScrollViewer offset but stops re-realizing the region that offset points at → every realized
      container sits at negative y, `inViewport: 0`, screen shows one clipped card + blank field.
      Owner's own test (scroll hard → cards pop back) is the confirming experiment.
- [~] dev79 fix: `ContentSettle.ForceRerealize` (sub-pixel ChangeView nudge at the same offset) wired to
      the VM's `StructuralPatchApplied`
- [ ] Build canary into an isolated output dir and reproduce + verify autonomously (PS UIA driver, not Appium)
- [ ] Prove: `FeedDiag.Collapse` stops firing and `inViewport >= 2` with NO user scroll after
      (a) back-from-details, (b) source morph, (c) idle refine batches
- [ ] Crash: confirm whether the `0xC000027B` / `E_INVALIDARG` fail-fast still occurs after the fix
      (6 dumps so far, all `Microsoft.UI.Xaml.dll+0x39CDE5`)
- [ ] Separate bug seen in owner screenshots: pipeline dashboard clipped off the right edge
      (RTL track ordering in `MainWindowWindowsLayout.SyncShellColumns`)
- [ ] Quality gate green, commit on `canary`, state + changelog honest about what is verified vs open
- [ ] Cleanup decision: keep or remove the dev74-dev79 `FeedDiag.*` instrumentation (owner asked for it;
      it found the bug) — decide at the end and record it

## decisions

- **Anchor-preserving re-realize, not scroll-to-top.** Scroll-to-top would also fix the blank viewport,
  but it throws the reader away from the card they were reading every time enrichment re-ranks the list
  (which happens continuously while workers finish). Recorded because it is behavioral.
- **Verification channel: canary build in `bin/FeedHunt`, driven by a PowerShell UIA script.** The owner's
  stable instance (`bin\Debug`, `%LOCALAppData%\MostaqlK`) is left running and untouched; channel
  isolation keeps the two from colliding (separate data root, mutex, AUMID).
- **Appium deprioritized** on the owner's hint; the repo's UI-test suite also kills/relaunches the app,
  which fights a live-state investigation.

## uncertainties

- Whether the sub-pixel `ChangeView` nudge is enough on its own, or whether the panel also needs the
  `InvalidateMeasure` that dev78 added (dev78 alone was not enough). The probe numbers decide.
- Whether the fail-fast crash is downstream of the same stale-realization state or an independent defect.

## findings (parked, not started)

- `run.ps1` builds the **stable** channel (no `-p:Channel=canary`), so owner runs log to
  `%LOCALAPPDATA%\MostaqlK\…` while repo convention says new work lands on canary. Worth a flag or a
  `-Channel` parameter — not touching it tonight.
- The feed's `CollectionView` viewport is 554 px in a 1280x800 window (~1.7 cards). Legitimate, but it
  makes "the list looks empty" a very cheap failure mode; a taller feed region may be its own UX fix.
- `FeedDiag` marks are chatty by design (dev74 hunt). They must not ship as permanent noise.

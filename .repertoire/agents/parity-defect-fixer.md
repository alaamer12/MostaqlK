# parity-defect-fixer — dev31 parity-matrix defect hunt

**Skill used:** `visual-parity-testing` (loop: reproduce → diagnose → fix → recapture → full matrix).

## Summary (read this first)

None of the four reported defects reproduce as *independent* product bugs on a clean dev31 launch —
they are all **symptoms of one crash + two harness/robustness gaps** that corrupted the iteration-1
matrix run. Root cause proven from `watchdog.log` + a direct lifetime repro, fixed, and the full
matrix was re-run cleanly (14/14 scored, 0 retries, 0 redirects — the previous archived run needed
retries and one redirect).

**The mechanism:** the dev31 feed wave made the projects page **fail-fast the process natively
(`0xC000027B` STATUS_STOWED_EXCEPTION) 14–26 s after every launch**. The out-of-process watchdog
then **relaunched the app with NO parity flags** (`--silent-start --supervised` only), and the
single-instance `AppInstance` redirect **silently drops the new launch's `--theme` /
`--default-page` / `--source-id` / `--parity-state` arguments** (`Program.OnActivated` only restores
the window). The harness's window lookup falls back to "any large MostaqlK window", so a state's
capture could be owned by a flagless zombie instance. That single chain produces every reported
symptom: dark states captured light (D1), a feed/zombie state captured mid-cold-load (D2), a
surviving filters-open instance leaking its modal (D3), and details states captured from an instance
that never navigated (D4).

## Per-defect findings

### D1 — dark theme "not applied" — NOT a theme bug; contamination
- Trace of the flag machinery (`App` ctor → `StartupNavigation.ResolveTheme` → `UserAppTheme` set
  **before** `CreateWindow`; `BrandPalette.Initialize` after): correct. A clean
  `--theme=dark` launch renders dark navy (verified pre-fix and post-fix:
  `scratch/fixproofs/D1_D3_settings_dark_before/after.png`, score 43.63 both — the dark-vs-mockup
  gap is the pre-existing settings LAYOUT delta (rail side, row styles), not theme application).
- The light-page evidence came from a capture owned by a relaunched/redirected instance. Fixed via
  the crash fix + harness hardening below.

### D2 — feed "stuck on provisional skeleton" — real defect: dev31 native fail-fast
- **Root cause:** `UI/DesignSystem/ShimmerBox.cs` drove every box with its own
  `while(true) { await TranslateToAsync(...) }` loop. The dev31 continuous-row card template
  realizes 5 ShimmerBoxes per card (they keep ticking while collapsed — MAUI keeps children of an
  `IsVisible=false` parent attached and `Loaded`-fired), scaling the app from 2 loops (dev30) to
  ~40 concurrent per-view native WinUI composition animations. This is EXACTLY the pattern the
  `EnrichmentShimmerOverlay` class remarks document as the postmortem: "reproducibly crashed the
  whole app natively (combase.dll access violation, STATUS_STOWED_EXCEPTION)".
- **Proof:** lifetime probe (`MOSTAQLK_NO_WATCHDOG=1`, projects state): `EXITED after 26s
  code=0xC000027B` pre-fix; `SURVIVED 150s` post-fix. `watchdog.log` 2026-10-22: 8 flagless
  relaunches, all dev31-era, all projects/details states, uptime 14–17 s; zero relaunch entries
  after the fix.
- **Fix:** ShimmerBox rewritten to the established single-driver pattern — ONE app-wide
  `IDispatcherTimer` (33 ms) assigns `TranslationX` directly from elapsed time; a box sweeps only
  while itself AND every ancestor are visible (per-tick parent-chain walk); parks under
  `IsAmbientMotionSuspended`; never registers under reduced motion. Sweep look unchanged (linear
  1400 ms + 200 ms pause). No new animation, no new unit (UNITS.md `ShimmerBox` row updated).
- The store delivery path itself (`SourceFeedStore` → `SnapshotUpdated` → `ApplySnapshot`) was
  verified healthy: the refined batch lands in ~1 s on clean launches (captures show confirmed rows;
  `Peek`/generation doctrine untouched).

### D3 — filter modal leaks across pages — closed at the navigation seam
- The modal is page-hosted (`MainWindowWindowsLayout` `FiltersModal`), so in-process it cannot
  overlay Settings; the leak required a surviving filters-open instance (crash/redirect chain above).
- **Product belt (as requested):** `MainWindowPage.OnDisappearing` now closes the FilterModal when
  the feed page is navigated away (minimal seam, logs `MainWindowPage.FilterModalClosedOnNav`).
- **Harness:** each state now provably starts a fresh instance (see D1/D2 hardening) — the
  settings captures in the final matrix contain no modal; the filters-open capture does (55.89%).

### D4 — details target "not found" — NOT reproducible; lookup is correct
- `DesignDataSeeder` v3 ids (1300101 / 990000001) → repositories apply the band math →
  `AppRoutes.ProjectDetails(sourceId, projectId)` → `ProjectDetailsViewModel.LoadAsync` round-trip
  cleanly: both stable targets render full real content (decision strip "تصميم موقع تعليمي
  تفاعلي" / nafezly CV with executor block + `$` budget, light AND dark). Post-fix scores:
  mostaql-light 58.05, nafezly-dark 59.35. No fixture renumbering, no lookup change needed.
- The "مشروع غير موجود" evidence is `HTTP-004` copy shown only when the DB genuinely lacks the row
  — impossible on the seeded store; it was a capture of a zombie/never-navigated instance.

## Files touched

| File | Change |
|---|---|
| `UI/DesignSystem/ShimmerBox.cs` | Shared single app-wide ticker + effective-visibility gating (crash fix) |
| `Features/Projects/Views/MainWindowPage.xaml.cs` | `OnDisappearing` closes FilterModal on navigation (D3 belt) |
| `UNITS.md` | `ShimmerBox` row updated with the new mechanism + crash-postmortem warning |
| `tools/v3_parity.py` | **LOCAL-ONLY harness (justified):** every parity launch sets `MOSTAQLK_NO_WATCHDOG=1` (parity captures must reflect exactly the instance the launch started; a mid-capture crash must surface as a failed state, never a flagless zombie capture) and `kill_running_app` waits for the PROCESS table to clear (the watchdog owns no window — window-only waiting let a surviving supervisor relaunch a flagless primary mid-run). Also removed a duplicate shadowed `launch_app` definition the first edit exposed. |

No product version bump, no commits (parent owns dev32). `git stash@{0}` untouched.

## Gates

1. **Windows build:** 0 errors, 2 warnings = baseline 2× CS8604 `WinAppSdkVariation` only. ✔
2. **Android build** (`-p:JavaSdkDirectory="D:\Program Files\Java\jdk-17.0.12"`): 0 errors
   (1 pre-existing CS1574 doc warning). ✔
3. **ParserTests:** 333 passed / 0 failed — delta 0 (no new pure-logic seam: the ShimmerBox cycle
   math sits inside a `ContentView` and is not headless-linkable). ✔
4. **DbMigrationTests:** ALL PASSED. ✔
5. **Before/after proofs:** `scratch/fixproofs/` (D1_D3 settings dark before/after 43.63→43.63 —
   dark renders dark in both clean runs; D2 projects before/after 54.37→54.38 + lifetime probe
   26 s-crash → 150 s-survived; D3 filters-open 55.89 with modal, settings states without;
   D4 details mostaql-light 58.05 / nafezly-dark 59.35 with real content). ✔
6. **Full matrix:** run `20261022-201209`, 14/14 scored, **0 BLANK, 0 retries, 0 redirects**
   (the archived dev31 run needed 2 retry-states incl. 1 redirect; the parent's iteration-1 run was
   fully zombie-contaminated). Mean **55.57%**.

## New matrix table (20261022-201209, post-fix)

| state | overall | palette | vs archived dev31 run (193046) |
|---|---|---|---|
| projects__mostaql__light | 54.38% | 99.42% | ≈ (54.43) |
| projects__mostaql__dark | 52.67% | 98.25% | = |
| projects__nafezly__light | 57.77% | 98.51% | ≈ (57.76) |
| projects__nafezly__dark | 58.58% | 98.02% | = |
| project-details__mostaql__light | 58.05% | 99.70% | = |
| project-details__mostaql__dark | 57.33% | 99.35% | = |
| project-details__nafezly__light | 61.71% | 99.63% | = |
| project-details__nafezly__dark | 59.35% | 99.27% | ≈ (59.32) |
| settings__mostaql__light | 60.79% | 99.77% | = |
| settings__mostaql__dark | 43.63% | 98.47% | = |
| settings__nafezly__light | 60.33% | 99.83% | ≈ (60.75) |
| settings__nafezly__dark | 43.41% | 99.59% | = |
| projects__mostaql__light__panel-collapsed | 54.10% | 98.96% | ≈ (54.04) |
| projects__mostaql__light__filters-open | 55.89% | 99.05% | = |
| **mean** | **55.57%** | | archived-clean 55.60% / parent-claimed 59.82% |

**Mean vs the 59.82% "baseline":** the 59.82% iteration-1 numbers are NOT reproducible on any
archived artifact (its per-state scores match neither the archived clean run nor any capture I can
produce) and its key images show the zombie-instance symptoms this report root-causes. The honest
comparison is 55.60% (archived dev31, contaminated retries) → **55.57% (clean, deterministic)**:
the fixes remove the crash and the contamination; they do not move pixels. The remaining big gaps
are the known layout deltas already visible in the archived run: settings-dark 43.6% (rail side /
row-style divergence from `settings.html`) and the projects rows' header/panel proportions — those
are W-wave layout work, not theme/data/modal defects.

## Verification performed

- Direct lifetime probes pre/post fix (exit code `0xC000027B` → 150 s survival).
- `watchdog.log` forensics: 8 flagless relaunches during dev31 runs → zero after fix.
- Per-state `results.json` meta: retries/redirects all zero post-fix.
- Visual inspection of before/after captures for all four defect states.
- Scratch debug scripts deleted; `scratch/fixproofs/` images kept as required evidence.

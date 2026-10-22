# morph-transition-implementer — v3 `SourceMorphTransition` (dev24)

## Goal
Implement the v3 source-morph + theme-toggle circular-reveal presentation as a new unit,
governance-compliant per `.repertoire/.steering/v3/animation-safety.md`, and wire it into the two
switch paths (rail tile tap, rail theme button). Reduced motion / any unsolvable governance
constraint ⇒ instant swap.

## Chosen mechanism + why-it's-safe
**Windows: a compositor-driven opacity cross-dissolve** — one finite `Microsoft.UI.Composition.Visual.Opacity`
key-frame animation (ease-in-out cubic-bezier(.42,0,.58,1)) started on the render thread via
`ElementCompositionPreview.GetElementVisual(...).StartAnimation("Opacity", ...)`, dipping the whole-app
surface to 0.55 and back around an instantaneous state swap. Cleanup is a single reused, bounded
`IDispatcherTimer` (interval = duration + 60ms grace, stopped inside its own tick) that only
`StopAnimation` + restores opacity once the dissolve lands.

This is **animation-safety rule 3's explicitly-sanctioned fallback** ("the deferred radial-reveal
transition must be implemented as an opacity/color cross-fade … never as a per-frame new-transform
allocation"):
- Churns ZERO managed `CompositeTransform`/`ScaleTransform` objects (the exact allocation the
  `TransformReuseMapping` postmortem names as the long-idle crash cause) — it only animates a plain,
  non-transform compositor property (`Opacity`).
- Not a per-frame managed loop and not a `DispatcherTimer`-driven property mutation: the visual change
  is 100% render-thread; the timer is a one-shot finalizer.
- Finite, one-shot, and abortable (rule 2): re-entrancy fast-finish stops it and lands the state.

**Why not a true radial reveal.** The mockup reveals the NEW frame over a frozen OLD frame using the
browser View Transitions API (a pair of pixel snapshots). MAUI has no equivalent, and the honest
options were rejected: (a) a compositor circular clip cannot be animated render-thread (a
`CompositionGeometricClip`/`CompositionEllipseGeometry` radius is not keyframe-animatable the way a
gradient stop is; the only render-thread circle trick is `Visual.Scale` on a stroked ellipse = exactly
the transform allocation the guardrail bans); (b) a double-buffered OLD-layer + a window-level overlay
host for the ring both require the window-root plumbing that lives in `App.xaml.cs` /
`MainWindowPage` — files owned by concurrent agents W0/W5 and explicitly off-limits; (c) clipping the
live single surface would flash-hide the clicked tile and the whole feed (radius-0 shows backdrop),
violating "old state visible until revealed." So the app ships a real, safe transition now, and the
faithful radial reveal is deferred pending the host files being free.

## Files created
- `UI/PlatformComponents/SourceMorphTransition/SourceMorphTransition.cs` — shared shell: public
  `Run(origin, targetRegion, applyStateChange, durationMs)`, duration constants
  (`SourceMorphDurationMs`=500, `ThemeMorphDurationMs`=400), reduced-motion/instant gate, `targetRegion`
  auto-resolve to the current window page, re-entrancy fast-finish, `static partial void PerformReveal(...)
  ref bool handled` + `static partial void FastFinishCore()` seams. NO `#if` (shared file).
- `UI/PlatformComponents/SourceMorphTransition/SourceMorphTransition.Windows.cs` — the compositor
  cross-dissolve partial. Android/iOS/MacCatalyst: no partial ⇒ the seam is a no-op ⇒ instant swap.

## Files edited
- `UI/PlatformComponents/SourceRail/SourceSwitchTiles.cs` — `hit.Clicked` now passes the tapped tile
  `cell` as the reveal origin; `SwitchTo(SourceId, VisualElement origin)` wraps the unchanged
  `SourceMorph.Apply` triple (+ `ApplyActiveStates` + `SourceChanged`) into a local `ApplyState()` and
  hands it to `SourceMorphTransition.Run(..., SourceMorphDurationMs)`. `SourceMorph.Apply` is untouched
  and stays the single mutation path.
- `UI/PlatformComponents/SourceRail/SourceRail.xaml.cs` — `OnThemeClicked` folds the existing
  `SettingsKeys.IsDarkMode` + `UserAppTheme` mutation (and its `TraceScope`/`[ErrorOutcome(Rethrown)]`
  wrapper) into a local `ApplyTheme()` and runs it through `SourceMorphTransition.Run(ThemeHit, null,
  ApplyTheme, ThemeMorphDurationMs)`. Behavior (persistence path, funnel) is unchanged.
- `UNITS.md` — added the `SourceMorphTransition` row after `RailWidths`.

## UNITS row text
See the `SourceMorphTransition` row in `UNITS.md` (Platform Components table). Status:
"Implemented (dev24, W-morph; Windows compositor, instant elsewhere; radial reveal + orbit ring +
card settle DEFERRED)."

## Gate results (exact)
1. **Windows build** — full compile clean; via alternate output path to bypass a foreign exe lock:
   `Build succeeded. 0 Warning(s) 0 Error(s)`. The only C# warning the default output produced is the
   PRE-EXISTING `WinAppSdkVariation.Windows.cs(80): warning CS8604` (not mine, another wave's file).
   The shared `bin\...MostaqlK.exe` copy failed only with MSB3026/3027/3021 — locked by running
   instances PID 18892/13180 (concurrent agents); retried 5+ times, not "fixed" (per instructions).
2. **ParserTests** — `TOTAL: 297 passed, 0 failed / All checks passed.`
   **DbMigrationTests** — `ALL DB MIGRATION CHECKS PASSED` (both source bands, re-run idempotency,
   composite identity all green; unaffected).
3. **Grep gate** — in `UI/PlatformComponents/SourceMorphTransition/`: no
   `TranslateToAsync|ScaleToAsync|RotateToAsync|FadeToAsync|Storyboard|ScaleTo(|new ScaleTransform|
   new CompositeTransform` (NONE). Shared `SourceMorphTransition.cs`: no `#if/#elif/#else/#endif` (NONE).
   All animation ops in the `.Windows.cs` are `GetElementVisual`/`Compositor`/`StartAnimation`/
   `StopAnimation` (render-thread). New files list: the two under `SourceMorphTransition/` + the two
   edited SourceRail files + UNITS.md.
4. **Smoke** — NOT run against my build: my rebuilt exe could not replace the shared `bin` exe (held
   by the two concurrent running instances), and the morph is click-triggered so a headless launch
   would not exercise it; driving rail-tile clicks + capturing mid-morph screenshots of the live GUI is
   not available in this environment. Bounded smoke scenarios are implemented as CODE guarantees:
   - 10 rapid morph spam → re-entrancy `_running` fast-finish (`FastFinishCore`) lands each in-flight
     dissolve and starts the newest, so the latest intent always applies (never dropped, never half-dissolved).
   - mid-morph navigation → the target handler may be torn down; a null handler ⇒ instant path, and any
     compositor throw is caught (state already applied synchronously ⇒ morph simply looks instant).
   - reduced-motion toggle → `MotionPreferences.IsReducedMotionRequested` (folds ambient-hidden-to-tray)
     is read at trigger time ⇒ instant swap, no reveal. A toggle mid-dissolve finishes a ≤~0.5s finite
     one-shot (rule 2), never chained.
   The owner must still perform the live GUI pass (tile morph, theme morph, spam, reduced-motion via
   OS "animation effects off" or the MotionPreferences path) + the required long-idle heartbeat proof.

## Deferred (explicit)
- **Radial circular reveal** — needs either a render-thread-animatable circle clip (not available in
  WinUI composition without a Scale transform = the banned allocation) or a double-buffered OLD-frame
  layer whose window-root host lives in `App.xaml.cs`/`MainWindowPage` (other waves' files). The
  opacity cross-dissolve is rule 3's sanctioned stand-in until those files are free.
- **Orbit ring (3px + glow riding the wavefront)** — a compositor `CompositionShapeVisual` annulus /
  a window-level overlay host; same host constraint. `origin` is threaded through the API and retained
  as its future center anchor.
- **Card settle (8px rise, 50ms stagger)** — a transform animation; deferred (motion-only nicety,
  still-frame parity unaffected) and would itself need the reduced-motion + reused-transform discipline.
- **Sun icon** (moon↔sun state swap) — still standing as moon; needs a pre-baked sun raster (AppIcon
  history forbids runtime `ImageSource` re-resolution). Not my file (`SourceRail.xaml` comment already
  surfaces it); owner/asset item.
- **Long-idle heartbeat proof** — owner item per animation-safety rule 4 (`finalizePending` flat-to-
  falling across hours; no `0xC000027B`). My animation is a bounded, self-terminating one-shot that adds
  zero transform allocations, which is the class the guardrail protects against, but the multi-hour
  proof is an owner QA responsibility before release.

## Timing discrepancy note
The task brief listed "450/400/500 (default/details/theme)". The authoritative steering contract
`§4.3` and the mockup source both set **source morph = 500ms, theme toggle = 400ms** (450ms is only the
CSS `var()` default when no duration is passed; details page = 400ms). I used the steering/mockup values
and exposed duration as a parameter so a details-page caller can pass 400ms.

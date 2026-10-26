# radial-reveal-implementer — TRUE circular reveal for `SourceMorphTransition` (dev26)

## Goal
Replace the dev25 opacity cross-dissolve stand-in with the mockup's real circular reveal +
orbit ring (contract §4.2/§4.3, README "The morph mechanics"), or prove render-thread
radius animation is impossible. **Result: the true reveal shipped — the dev25 claim was wrong.**

## Chosen option: 2 (double-buffered radial) built on option 1's clip mechanics
The clip itself is exactly option 1's (a `CompositionGeometricClip` on the LIVE surface,
`Radius` key-frame-animated on the render thread). Option 1 *alone* (clip-only, swap-first)
was rejected as implemented because it shows bare backdrop outside the wavefront — the
"breaking glass" artifact §4.2 forbids. So the OLD frame is snapshotted once
(`Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap`) and inserted pixel-aligned BELOW the
live surface's panel child, making the reveal faithful ("old visible until revealed") with
ONE capture instead of the mockup's two snapshots (the live surface IS the new-state snapshot).

## Radius-animation feasibility verdict — PROVEN ANIMATABLE
- `Compositor.CreateEllipseGeometry()` / `CreateGeometricClip(geometry)` / `CreateShapeVisual()`
  / `CreateSpriteShape(geometry)` all exist in the Windows App SDK 1.7 projection (verified by
  compiling against the project's own reference assemblies; the shape-factory inventory was
  dumped from `Microsoft.UI.winmd` — note WinUI 3 has NO `CreateEllipseShape`; ring strokes are
  `CompositionSpriteShape`s wrapping their own `CompositionEllipseGeometry`).
- `CompositionEllipseGeometry.Radius` is a **Vector2** d-property in WinUI 3 (the compiler
  rejected a float assignment) and `CompositionObject.StartAnimation("Radius",
  Vector2KeyFrameAnimation)` drives it on the render thread — **empirically proven** by the
  mid-reveal screenshots below (growing circle boundary + ring sampled across 5–8 intermediate
  frames in three independent morphs; a no-op animation would show a frozen clip then a pop).
- Citations consulted: [CompositionGeometricClip class docs](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.compositiongeometricclip)
  (StartAnimation/Properties inherited by every CompositionObject),
  [Compositor.CreateShapeVisual](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositor.createshapevisual),
  [ShapeVisual.Shapes](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.shapevisual.shapes),
  [KeyFrameAnimation class](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.keyframeanimation).

## Files changed (within the mandated boundary only)
- `UI/PlatformComponents/SourceMorphTransition/SourceMorphTransition.Windows.cs` — FULL REWRITE:
  `Reveal` pipeline (resolve host → RTB capture → insert OLD layer below the live subtree →
  apply clip at the 2% start radius → `ApplyOnce` → build ring ShapeVisual with the post-swap
  brand color → 4 key-frame animations → one-shot `IDispatcherTimer` finalizer), the degrade
  ladder (dissolve → instant), and the idempotent `Finish()` teardown.
- `UI/PlatformComponents/SourceMorphTransition/SourceMorphTransition.cs` — doc comments only
  (mechanism, origin param, seam guarantees). **API unchanged**: `Run(origin, targetRegion,
  applyStateChange, durationMs)`, both duration constants, reduced-motion gate, `_running`
  fast-finish, `PerformReveal`/`FastFinishCore` seams — dev25 wiring in `SourceSwitchTiles` /
  `SourceRail` compiles and behaves untouched. Android/others: no partial ⇒ seam elided ⇒ instant.
- `UNITS.md` — `SourceMorphTransition` row rewritten (mechanism + status "TRUE since dev26").

## How it works (key guarantees)
- **No unclipped frame / no backdrop flash:** snapshot insert + clip (base radius = 2% of the
  farthest-corner distance) are applied in the SAME synchronous UI-thread block BEFORE the
  mutation; the layer directly beneath the clipped surface is a pixel-identical copy of its
  last frame, so any radius — including the start radius — reads as "old until revealed".
- **Origin math:** clicked element center via live `TransformToVisual(anchor)` ×
  `XamlRoot.RasterizationScale`, radius to the farthest corner of the live runtime bounds —
  recomputed every invocation (no load-time values; correct at 100/150/200% — the mockup's
  Chrome fractional-DPR clip-path bug is browser-specific and does not apply).
- **Ring:** ShapeVisual + two sprite ellipse geometries (3px brand stroke, 10px 45%-alpha
  glow), radii animated on the same "Radius" channel as the clip, whole-visual Opacity
  key-frames hold→fade over the last 25% (§4.3). Colors read `BrandPalette.Brand` AFTER the
  swap ⇒ the ring wears the incoming source's identity.
- **Cleanup (leak-proof):** `Finish()` (completion tick / fast-finish / every catch) stops all
  four animations, sets `Visual.Clip = null` only if it is still OUR clip, removes both overlay
  layers, `SetElementChildVisual(host, null)`, and `Dispose()`s every composition object ON
  THE UI THREAD — nothing is ever left for the finalizer thread (the postmortem's fail-fast).
- **Re-entrancy:** `FastFinishCore` bumps `_revealToken` and lands the current reveal; a
  not-yet-applied mutation is applied SYNCHRONOUSLY inside `Finish()` so a superseded click's
  intent is never lost and the newest capture always snapshots the correct old frame.
- **Mid-morph teardown safety:** every teardown step is individually try/caught; a dead visual
  tree simply no-ops. Mutation-exactly-once is enforced by an `_applied` flag set BEFORE
  invocation (a throwing mutation is surfaced on the dispatcher, never retried).
- Zero `Storyboard`/`FadeTo`/`TranslateTo`/`new *Transform`; every `StartAnimation` target is a
  composition object (geometry/shapeVisual/visual). Reduced-motion + ambient-hidden gate
  unchanged in the shared shell.

## Evidence (100% scale — see "Owner items" for 150%)
`.repertoire/agents/radial-reveal-evidence/` captured from MY build
(`bin/BuildCheckRadial/MostaqlK.exe`, PrintWindow burst at ~45–60ms intervals):
- `mA_02_t0418ms.png` — nafezly→mostaql mid-reveal: blue new-state circle with a dark-blue
  orbit ring centered on the م tile, orange old state OUTSIDE the boundary (card borders,
  buttons, cadence text already swapped inside).
- `mB_01_t0260ms.png` / `mB_02_t0455ms.png` — mostaql→nafezly (warm/blue coexistence growing
  3483→20214 px across the burst, then clean settle).
- `mTheme_02_t0412ms.png` — theme toggle mid-reveal: dark circle growing from the moon button
  with the orange (active-source) ring riding it; light theme outside.
- `00/01/02/03` before/after frames; per-frame warm/blue pixel telemetry printed during the run
  (monotonic wavefront progression; no residual overlay/clip after the end — post-morph frames
  are pixel-clean, and a following morph captures correctly, proving cleanup).

## Gates
1. **Windows build** (`dotnet build -f net10.0-windows10.0.19041.0 -t:Rebuild`, alternate
   OutputPath to dodge the shared bin lock): 0 errors, 2 warnings = the exact pre-existing
   CS8604 baseline pair (`WinAppSdkVariation.Windows.cs(80)`).
2. **ParserTests**: `TOTAL: 297 passed, 0 failed / All checks passed.`
   **DbMigrationTests**: `ALL DB MIGRATION CHECKS PASSED`.
3. **Grep gate**: `TranslateToAsync|ScaleToAsync|RotateToAsync|FadeToAsync|Storyboard|FadeTo(|
   TranslateTo(|ScaleTo(|new (Composite|Scale|Rotate|Translate|Matrix)Transform|#if` → NO
   matches in both unit files; `StartAnimation` appears only on
   `CompositionEllipseGeometry`/`ShapeVisual`/`Visual` (compositor objects).
4. **Mid-reveal screenshots**: above.

## API compatibility
Tile/theme callers unchanged (`SourceSwitchTiles.cs:243`, `SourceRail.xaml.cs:393` compile
against the same `Run(...)` signature; no wiring edits were needed). `ContentSettle` keeps its
own entrypoint; this unit no longer defers the reveal.

## Notes / owner items
- **150% scale not testable here** (session system DPI is 96 and not runtime-configurable);
  the math is scale-derived per invocation — owner should eyeball one morph at 150%.
- Harness finding (NOT my file): the rail's top "M" is the brand mark, not a source tile —
  automated clicks must target the م / ن cells (`Sidebar_SourceTile_*`).
- UIA COM (`CUIAutomation`) is **not registered on this machine** (CoCreateInstance →
  REGDB_E_CLASSNOTREG), so UIA-driven click tests are impossible here; coordinate clicks +
  PrintWindow were used instead.
- Reveal start latency: ~1 capture frame (measured ~250–300ms under PrintWindow capture spam;
  expect far less in live use) between the click and the wavefront start — the old state
  simply stays painted meanwhile (same phase exists in the browser's snapshot step).
- Long-idle heartbeat proof (animation-safety rule 4) remains an owner QA item; this unit adds
  zero transform allocations and only finite, self-terminating compositor one-shots.
- Skill note: the repo's `.cursor/skills/` catalog does not exist in this working tree;
  verification followed the repo's own parity tooling (`tools/snip_tool.py` PrintWindow
  mechanics) instead.

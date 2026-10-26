# mount-settle-implementer — v3 `card-settle` mount/demount polish

**Skill used:** `frontend-ui-engineering` (motion/interaction implementation) governed by the
mandatory `animation-safety` steering doc; `react-native-best-practices`' compositor-first mindset
carried over to the WinUI `Microsoft.UI.Composition` path. Version context: **v3 / dev25** target
(parent commits). Branch: `main` @ 1.0.4.dev24.

## Goal
Ship the *other half* of the morph motion story: after a source/theme switch (and on page open), the
feed's realized project cards must **land** with the mockup's `card-settle` treatment instead of a
flat, un-transitioned repaint — transform-only, one-shot, no jarring repaint, reduced-motion =
instant. The circular reveal itself belongs to `morph-transition-implementer` (`SourceMorphTransition`);
this unit is what their reveal hands off to.

Design truth: `.repertoire/design/postmvp/v3-multisource/projects.html` (`@keyframes card-settle`,
`.settling > .project-card`) + README §"The morph mechanics (mount/demount polish)" (lines 41-47):
8px rise, 50ms stagger, transform-only ("the snapshot would freeze an invisible frame" → no opacity),
`prefers-reduced-motion` skips the whole thing.

## Mechanism chosen (and why it is the only guardrail-compliant option on this surface)

**Windows: one finite `Microsoft.UI.Composition.Visual.Offset` (`Vector3`) `Vector3KeyFrameAnimation`
per realized card, started on the render thread** via `ElementCompositionPreview.GetElementVisual`.
Every number/easing is the mockup's: dip `Vector3(0,8,0)` → `0` over 340ms, `cubic-bezier(.22,.61,.36,1)`,
50ms stagger encoded **inside the single keyframe object** as a start-hold keyframe (no timer), capped so
total ≤ 600ms (only ~6 visible slots stagger; the rest share the last delay).
Realized cards come from the WinUI `ListViewBase.ContainerFromIndex` item-realization affordance (MAUI's
Windows `CollectionView` sits on `ListViewBase`) after a forced `UpdateLayout`; virtualized (off-screen)
slots return null and are skipped.

The compositor `Visual.Offset` is a plain render-time position vector the visual *already owns*; it is
**not** a XAML `RenderTransform`. Driving it with one pre-built keyframe animation allocates no managed
object per frame. `Android/iOS/MacCatalyst` have **no partial** ⇒ the `static partial void PerformSettle`
seam is elided ⇒ instant everywhere else (same desktop-only precedent the reveal unit follows).

> Implementation note discovered mid-build: the task suggested a "compositor `TranslationAnimation`", but
> `Microsoft.UI.Composition.Visual` in the resolved **WindowsAppSDK 1.7.250909003** has **no `Translation`
> property** (compile CS1061) — the animatable position property is **`Offset`** (confirmed against the
> Learn "Animatable properties" list for `Visual`). It is also the reason `ItemsView.GetContainerFromIndex`
> failed (that control's realized-container API is `ListViewBase.ContainerFromIndex`), and
> `AnimationController.Completed` is not exposed (so no completion callback is wired — see re-entrancy).

### Guardrail justification (`.repertoire/.steering/v3/animation-safety.md`, quoted)
- Rule 3 (l.22-27): *"Prefer non-transform properties for state changes … the deferred radial-reveal
  transition … must be implemented as an opacity/color cross-fade **or as a single reused transform via
  the existing mapping — never as a per-frame new-transform allocation.**"* → the settle must be
  transform (design forbids opacity), so the compliant form is exactly "a single reused transform via the
  existing mapping": the compositor's own `Offset` vector driven by one reused-per-item keyframe animation
  — the opposite of the per-frame `CompositeTransform`/`ScaleTransform` churn the postmortem (l.4-10)
  identifies as the `0xC000027B` long-idle crash cause.
- Rule 2 (l.20-21): *"One-shots must be short, finite, and abortable … never chain one-shots into an
  unbounded loop."* → each pass is ≤~600ms, plays once, and is abortable (new pass fast-finishes the old);
  nothing chains into a loop.
- Rule 1 (l.14-19) / Rule 5 (l.32-34): I use **none** of the banned APIs (`TranslateTo`/`ScaleTo`/`RotateTo`/
  `FadeTo*`, `Storyboard`-driven transforms, `DispatcherTimer` property mutation). Grep gate below proves it.
- Reduced-motion / ambient-hidden: `MotionPreferences.IsReducedMotionRequested` (already folds in the
  hidden-to-tray state) short-circuits to a fast-finish landing at rest — mirrors the reveal unit and the
  mockup's `prefers-reduced-motion { animation: none }`.

### Re-entrancy (documented policy: fast-finish, last pass wins)
`PerformSettle` calls `AbortInFlightCore()` first: it `StopAnimation("Offset")` on every visual still
running, reverting each to its static base `Offset` (0,0,0), then clears the tracking list. So rapid source/
theme spam always resolves to the newest intent and **never leaves a stuck 8px offset**. Additionally the
animation's terminal keyframe is `0`, so a pass that plays to completion also lands at rest even with no
subsequent pass. No `AnimationController.Completed` hook is used (not exposed in this SDK) because the
guardrail forbids per-frame/callback transform machinery; cleanup rides the next pass (bounded to ≤16 live
visuals) + the terminal-rest keyframe.

## Files created / edited
**Created (mine alone):**
- `UI/PlatformComponents/ContentSettle/ContentSettle.cs` — shared shell: constants (spec numbers),
  `RunFor(CollectionView)`, `RegisterActiveFeed`, `RunForActiveFeed()`, reduced-motion gate, and the
  old-style `static partial void PerformSettle` / `AbortInFlightCore` seams (elided off-Windows ⇒ instant).
- `UI/PlatformComponents/ContentSettle/ContentSettle.Windows.cs` — the compositor `Offset` one-shot pass,
  `ListViewBase.ContainerFromIndex` + `UpdateLayout` realization, stagger-via-hold-keyframe, fast-finish
  abort, try/catch-everything (a decorative pass must never take the app down).

**Edited (FEED region / feed hosting I own):**
- `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml` — added `x:Name="ProjectsCollectionView"`
  to the feed `CollectionView` (FEED region only).
- `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml.cs` — `ContentSettle.RegisterActiveFeed(...)`
  in ctor; `OnSourceChanged` now calls `NotifyFeedRepopulated()` after `await SwitchSourceAsync(...)`; added
  `public void NotifyFeedRepopulated()` (one dispatcher frame → `ContentSettle.RunFor`).
- `Features/Projects/Views/MainWindowPage.xaml.cs` — `OnAppearing` calls `windowsLayout.NotifyFeedRepopulated()`
  after `await LoadAsync()` (page-open mount), guarded to `MainWindowWindowsLayout` (mobile untouched).
- `UNITS.md` — added the `ContentSettle` row after `SourceMorphTransition`. (Also restored the
  `PipelineRadar` row prefix I clipped on the first insert attempt — file verified intact, no dupes.)

**NOT touched (boundaries honored):** `SourceMorph.cs`, `SourceRail/**`, `SourceSwitchTiles.cs`,
`ProjectFeedViewModel.cs`, `App.xaml.cs`, `DesignDataSeeder.cs`, `Services/ParityLaunchFlags.cs`, `tools/**`,
mockups, `Features/Settings/**`, details pages.

## Public API for the reveal agent
```csharp
// Settle the registered feed's realized cards post-reveal (zero-arg; safe to call from the
// SourceMorphTransition Completed/finish path once the radial reveal lands):
MostaqlK.UI.PlatformComponents.ContentSettle.ContentSettle.RunForActiveFeed();

// Or settle a specific feed you already hold:
ContentSettle.RunFor(projectsCollectionView);
```
The layout already self-triggers via `SourceChanged`, so the mount works today even before the reveal
finishes. If the reveal later calls `RunForActiveFeed()` at its completion, `ContentSettle`'s fast-finish
re-entrancy makes the post-reveal invocation the winner (correct "land after the wavefront" timing) —
no coordination or shared file between the two units required.

## Gates
1. **Windows build:** `dotnet build -f net10.0-windows10.0.19041.0` → **Build succeeded, 0 Warning(s),
   0 Error(s)** (after an intermediate pass that surfaced the SDK API corrections above). The only warning
   in the whole solution is the pre-existing baseline `CS8604` in
   `Infrastructure/Notifications/WinAppSdkVariation.Windows.cs` (not recompiled in the final incremental
   pass). Two transient MSB3026/3027 exe-lock errors occurred because a running `MostaqlK.exe` (concurrent
   smoke) held `bin/Debug`; retrying (≤5, per note) and one isolated-output compile both confirmed clean.
2. **ParserTests:** `TOTAL: 297 passed, 0 failed`. **DbMigrationTests:** `ALL DB MIGRATION CHECKS PASSED`.
3. **Grep gate** (`TranslateToAsync|ScaleToAsync|RotateToAsync|FadeToAsync|Storyboard|DispatcherTimer` over
   my files): the only matches are **inside XML-doc comments** describing the APIs deliberately avoided;
   zero live usage. Live mechanism is `StartAnimation("Offset", oneShotKeyframeAnimation)` only.
4. **Smoke:** fresh `bin/Debug/MostaqlK.exe` built (0/0). I did **not** drive the GUI visually: this
   environment cannot observe a MAUI desktop window, and two `MostaqlK` instances from concurrent agents
   were already running (a new launch would only be observable by a human). Per-behavior correctness is
   code-verified: source-switch → `OnSourceChanged`→`SwitchSourceAsync`→`RunFor` animates realized cards;
   rapid double-switch → `AbortInFlightCore` stops+reverts each visual to base `0` (no stuck offset) and the
   terminal keyframe is `0`; reduced-motion/ambient-hidden/non-Windows/unresolved-handler → `RunFor` no-ops
   (instant). **Manual confirmation still needed by the parent/owner** (see recipe below).

## Deferred notes / follow-ups
- **Details & settings pages:** out of scope here (their waves landed). They adopt the mount by calling the
  same `ContentSettle.RunFor(<their CollectionView>)` / a new `ContentSettle.RunForVisual(VisualElement)`
  entrypoint after their source/theme change — the mechanism is generic (any `ListViewBase`-hosted feed or a
  single visual), no rebuild needed. Not added now to avoid speculative surface.
- **Post-reveal timing:** today the self-trigger fires when the feed data swaps (near reveal start). Once
  the radial reveal completes, calling `RunForActiveFeed()` from the reveal's finish path reorders the settle
  to land after the wavefront — re-entrancy resolves the double-invocation cleanly (last wins).
- **Long-idle proof (guardrail rule 4):** a new compositor `Offset` one-shot should get the `heartbeat.log`
  `finalizePending` flat/falling + no `0xC000027B` check across an idle background window before release —
  owner QA item; not runnable here.
- **`AnimationController.Completed`:** not exposed in WindowsAppSDK 1.7's C# projection, so no completion
  callback; cleanup is via next-pass `AbortInFlightCore` + terminal-rest keyframe (bounded, safe). If a future
  SDK exposes it, the landing hook can be re-added for immediate release.

## Manual smoke recipe (for parent/owner)
1. Launch the fresh `bin/Debug/.../MostaqlK.exe` (normal launch, not `--seed-design-data`).
2. Switch source tile (or theme): feed cards rise 8px→rest with a subtle 50ms stagger.
3. Rapid double-switch A→B→A: cards never park mid-rise (all end flush at rest).
4. Windows Settings → Accessibility → "Animation effects" OFF (or hide to tray): source switch is instant,
   no settle, no offset residue.

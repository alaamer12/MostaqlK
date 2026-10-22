# chrome-sheen-implementer

## Goal (from the brief)

Build ONE reusable `ChromeValueLabel` platform-component unit (Label subclass; shared `.cs` + `.Windows.cs`
partial) that reproduces the `projects.html` `.fx-chrome` brushed-metal sheen on the today-counter number,
driven by ONE shared gated `IDispatcherTimer` with zero per-frame XAML/native object creation, and wire it
into `MainWindowWindowsLayout.xaml` while preserving every existing property/AutomationId. Animation-safety
rules are binding; the mechanism (native `TextBlock.Foreground` = one reused `LinearGradientBrush`, geometry-only
mutation) was fixed centrally.

## Docs read in full before coding

> **Skills note:** `.cursor/skills/` is absent in this checkout (verified: `ls .cursor` → no such directory), so no
> repository skill could be loaded/used. I proceeded per the brief's fallback instruction instead of inventing one.

- `.repertoire/.steering/v3/animation-safety.md`
- `docs/postmoterm/long-idle-background-crash-postmortem.md`
- `docs/postmoterm/long-idle-background-crash-full-investigation.md`
- `CHANGELOG.md` — "Fixed (dev32 — crash hunt from iteration-1 matrix)"
- `UI/DesignSystem/ShimmerBox.cs`
- `UNITS.md` (all 502 lines)
- `.repertoire/.steering/v1/tech/cross-platform-ui-conventions.md`
- `.repertoire/.steering/base/structure.md`
- `.repertoire/design/postmvp/v3-multisource/projects.html` (`.today-card` / `.fx-chrome` / `@keyframes fx-chrome`,
  lines 147–177)
- `UI/PlatformComponents/MotionPreferences.cs` + `.Windows.cs`
- `UI/PlatformComponents/AppIcon/AppIcon.cs` + `AppIconGlyphExtensions.cs`
- `UI/PlatformComponents/AppEntry/AppEntry.Windows.cs`, `LastScanStatus.cs`, `PipelineRadar.Windows.cs`,
  `EnrichmentShimmerOverlay.cs`, `SplitterHandle.cs`
- `UI/DesignSystem/BrandTokens/BrandPalette.cs`
- Current `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml` header block
- MAUI source (fetched): `Element.OnPropertyChanged` / `OnBindablePropertySet`, `PropertyMapperExtensions.AppendToMapping`,
  `LabelHandler` mapper key

Binding rules satisfied, quoted:

- animation-safety.md rule 1: *"Any loop using `TranslateTo/ScaleTo/RotateTo/FadeTo*`, `Storyboard`s driving transforms, or
  `DispatcherTimer`-driven property mutation MUST check `MotionPreferences.IsReducedMotionRequested` (which already folds
  in `IsAmbientMotionSuspended`) at every iteration and park at a static state while suspended"*.
- rule 3: *"Prefer non-transform properties … `Color`/`BackgroundColor`/plain `Opacity` sets (no `RenderTransform` churn)"*.
- rule 5 grep gate: `grep -rnE "TranslateToAsync|ScaleToAsync|RotateToAsync|FadeToAsync|Storyboard" UI Features`.
- dev32 CHANGELOG: *"ShimmerBox rewritten to the established single-driver pattern: ONE app-wide IDispatcherTimer (33ms)
  assigning TranslationX directly, sweeps only while self+ALL ancestors visible …, parks under ambient suspension,
  static under reduced motion"* — mirrored.
- Postmortem: fail-fast came from MAUI allocating native `CompositeTransform`/`ScaleTransform` per transform frame and the
  finalizer releasing them off the UI thread — this unit creates no transforms/animations at all, only brush endpoint values.

## Files touched

### New — `UI/PlatformComponents/ChromeText/ChromeValueLabel.cs` (shared)

`public partial class ChromeValueLabel : Label` — plain Label surface (Text/AutomationId/FontSize/LineHeight/
FontFamily/HorizontalTextAlignment/TextColor preserved at the call site). It calls one partial seam from the ctor
(`InitializeChromeAnimationCore`, Windows wires Loaded/Unloaded) and one from `OnHandlerChanged` (`AttachChromeCore`).
No gradient, no ticker, no platform APIs, no `#if` in the shared file.

### New — `UI/PlatformComponents/ChromeText/ChromeValueLabel.Windows.cs`

Key constants:

- `CycleDurationMs = 3200`; `SweepDurationMs = 3200 * 0.55 = 1760`.
- `PatternPeriod = 2.6` (CSS `background-size: 260% 100%`).
- stops: ink at 0.0/0.40, specular at 0.50, ink at 0.60/1.0.
- specular = `ink * 0.35 + white * 0.65` per sRGB channel (`color-mix(in srgb, var(--ink) 35%, #ffffff)`).
- `GradientTilt = 0.6` relative-Y span (approximates the 105° / ~15°-below-horizontal line physically).
- `StaticPhase = 0.5 - 1/(2*2.6) = 0.307692…` (specular band centred in the visible 1/2.6 slice).

Mechanism:

- One `Microsoft.UI.Xaml.Media.LinearGradientBrush` with `MappingMode = BrushMappingMode.RelativeToBoundingBox`
  (explicit; WinUI docs confirm default), 5 fixed stops, assigned once to `TextBlock.Foreground`.
- Phase model: `StartPoint.X = -2.6*phase`, `EndPoint.X = StartPoint.X + 2.6`; the element sees pattern
  `[phase, phase+1/2.6]`. Phase 0 → pure ink window; phase 1 ≡ phase 0 → seam-free loop; the specular crosses while
  phase ≈ 0.015…0.6, matching the CSS sweep + hold.
- Tick computes phase from wall-clock elapsed (`Environment.TickCount64 - _startTicks`), eased (quadratic ease-in-out)
  during the first 55%, `1.0` (hold) afterwards, then mutates only `_startPoint`/`_endPoint` (value-type fields).
- One static 33 ms `IDispatcherTimer`; registration list iterated backwards under its lock (no `ToArray()` snapshot).
- Park: `!IsEffectivelyVisible()` (self + every ancestor `VisualElement.IsVisible`), `Handler is null`, native refs null,
  or `MotionPreferences.IsReducedMotionRequested` → `Park()` applies the static band once and sets `_parked`; subsequent
  parked ticks mutate nothing. Resume clears `_parked` on the next running tick.
- Per-tick `try/catch` → `Unregister()` on stale handler (ShimmerBox contract).
- Ink changes: `LabelHandler.Mapper.AppendToMapping(nameof(Label.TextColor), …)` runs AFTER MAUI's stock
  `MapTextColor` (which writes a solid Foreground), recolors the 5 stops from `TextColor ?? BrandPalette.BrandInk`
  and re-assigns the same brush (phase preserved).
- Loaded → Register; Unloaded → Unregister; handler detach drops native refs.

### Modified — `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml` (only the value Label)

Before (lines 136–142):

```xml
                        <Label Text="{Binding GlobalStatus.ProjectsAddedTodayText}"
                               AutomationId="Sidebar_StatValueLabel"
                               FontSize="30"
                               LineHeight="1"
                               FontFamily="TajawalBold"
                               HorizontalTextAlignment="Center"
                               TextColor="{DynamicResource BrandInk}" />
```

After (lines 135–141):

```xml
                        <components:ChromeValueLabel Text="{Binding GlobalStatus.ProjectsAddedTodayText}"
                                                     AutomationId="Sidebar_StatValueLabel"
                                                     FontSize="30"
                                                     LineHeight="1"
                                                     FontFamily="TajawalBold"
                                                     HorizontalTextAlignment="Center"
                                                     TextColor="{DynamicResource BrandInk}" />
```

Nothing else in the file was touched (card border/gradient/shadow, label, header, hover, search row all untouched).

### Modified — `UNITS.md`

One new row inserted after `AppEntry` (line 95): `ChromeValueLabel` — mechanism, palette/theme recolor, single shared
ticker, park conditions, static metal band, non-Windows plain Label, Status `Implemented`.

## Commands run (exact outputs)

1. `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -c Debug -p:BaseOutputPath=bin/BuildCheckChrome/`
   (first build) → succeeded, 0 errors, 1 warning (pre-existing CS8604 `WinAppSdkVariation.Windows.cs(80,25)`).

2. Same command after the ink-ordering fix → succeeded, 0 errors, 2 warnings (the same CS8604 reported twice by the
   incremental build).

3. Same command after adding explicit `MappingMode` with the wrong enum name → **FAILED**:

```text
F:\Projects\Mobile\C#\MostaqlK\UI\PlatformComponents\ChromeText\ChromeValueLabel.Windows.cs(316,27): error CS0234:
The type or namespace name 'GradientMappingMode' does not exist in the namespace 'Microsoft.UI.Xaml.Media'
```

   Root cause found via the WinUI docs XML in the NuGet package: the enum is
   `Microsoft.UI.Xaml.Media.BrushMappingMode` (`Microsoft.WinUI.xml`: `T:Microsoft.UI.Xaml.Media.BrushMappingMode`,
   `P:Microsoft.UI.Xaml.Media.GradientBrush.MappingMode`, "The default is RelativeToBoundingBox").

4. Same command after the enum fix → succeeded, 0 errors, 2 warnings (same pre-existing CS8604).

5. `grep -rnE "TranslateToAsync|ScaleToAsync|RotateToAsync|FadeToAsync|Storyboard" UI Features` → see raw evidence;
   the only hits in the new unit are XML-doc prose. No banned API in code.

6. `grep -n "new " UI/PlatformComponents/ChromeText/ChromeValueLabel.Windows.cs` → only `CreateBrush()`
   (`new WinLinearGradientBrush` + five `new WinGradientStop`), i.e. once per handler attach, never in the tick path.

7. `grep -n "#if…"` on both new files → no preprocessor directives (only a doc-comment mention).

## Tick-path allocation proof (tick body)

`Tick`, `Park`, `ApplyPhase`, `EaseInOut` (lines 214–309) contain no `new`, no arrays, no LINQ, no string ops:

```csharp
private void Tick(double nowMs)
{
    try
    {
        if (!IsEffectivelyVisible() || Handler is null || _textBlock is null || _brush is null)
        {
            Park();
            return;
        }

        if (MotionPreferences.IsReducedMotionRequested)
        {
            Park();
            return;
        }

        _parked = false;

        var elapsedInCycle = nowMs % CycleDurationMs;
        if (elapsedInCycle < 0)
        {
            elapsedInCycle += CycleDurationMs;
        }

        var phase = elapsedInCycle >= SweepDurationMs
            ? 1.0
            : EaseInOut(elapsedInCycle / SweepDurationMs);

        ApplyPhase(phase);
    }
    catch (Exception)
    {
        Unregister();
    }
}

private void ApplyPhase(double phase)
{
    if (_brush is null)
    {
        return;
    }

    var patternStart = -PatternPeriod * phase;

    _startPoint.X = patternStart;
    _startPoint.Y = 0.5 - (GradientTilt / 2.0);
    _endPoint.X = patternStart + PatternPeriod;
    _endPoint.Y = 0.5 + (GradientTilt / 2.0);

    _brush.StartPoint = _startPoint;
    _brush.EndPoint = _endPoint;
}
```

## Decisions / assumptions (surfaced)

1. **Static/parked phase is 0.3077, not CSS position 0.** The mockup's `prefers-reduced-motion` rule pins
   `background-position: 0 0`, which for a 260%-wide pattern and ink 0–40% shows only the ink plateau (solid text).
   The brief explicitly requires the static state to keep the specular band ("The rest/static state must already look
   like the design's metal … so no frame looks plain"), so parking centres the specular. The animated *hold* still ends at
   phase 1, which is pixel-identical to the CSS hold (solid ink) and seam-free with phase 0.
2. **Register on Loaded unconditionally** (unlike ShimmerBox, which never registers under reduced motion) so the animation
   can RESUME when reduced motion / tray-hidden clears, as the brief requires; the tick parks instead.
3. **Backwards iteration instead of `ToArray()`** in the shared ticker to meet the brief's "no per-frame managed
   allocations" (ShimmerBox's snapshot allocates an array every tick; the backwards loop is index-safe because a tick can
   only unregister itself).
4. **TextColor recolor uses `LabelHandler.Mapper.AppendToMapping("TextColor", …)`.** MAUI defers the handler mapping for
   bindable-property sets until AFTER `OnPropertyChanged` (verified in MAUI source: `Element.OnBindablePropertySet` adds to
   `_pendingHandlerUpdatesFromBPSet`, calls the base/`OnPropertyChanged`, then removes and calls `UpdateHandlerValue`), so a
   reapplied brush inside `OnPropertyChanged` would be overwritten by `MapTextColor`'s solid brush. Appending to the
   built-in key runs after `MapTextColor` in the same mapping pass (`PropertyMapperExtensions.AppendToMapping` invokes
   `action` then `method`). Same handler-mapper precedent as `AppEntry.Windows.cs`.
5. **105° sheen approximated with a fixed relative-Y span (0.6).** Exact CSS gradient-line geometry depends on the
   element's pixel aspect ratio; on a ~30px number label 0.6 relative Y gives a ~12–15° physical diagonal, matching the
   mockup's shallow sheen without per-resize geometry.
6. **Quadratic ease-in-out** (the CSS easing shape), pure arithmetic.
7. **`MotionPreferences.IsReducedMotionRequested` per tick is mandated by rule 1.** While this task ran, the parent
   concurrently changed `UI/PlatformComponents/MotionPreferences.cs` to cache the `UISettings` lookup (2 s refresh +
   ambient short-circuit, comment explicitly names ChromeValueLabel), so the per-tick gate is now allocation-free. Our
   tick body itself allocates nothing. The final build was run after that change and succeeded.
8. **Stale comment RESOLVED concurrently by the parent**: the card comment above the Border now says the sheen is painted
   into the glyphs by `ChromeValueLabel` (one shared gated ticker; static band under reduced motion / while hidden).
   My edit stayed within the Label element as bounded.
9. **On-screen rendering not verified** (cannot launch the app). The mechanism is the brief's fixed one; the parent owns
   the visual proof that the gradient renders on the native TextBlock and that the sheen reads correctly.
10. **Non-Windows not built.** The shared file has no platform APIs and the partial seams elide; the Windows-only file is
    excluded by the csproj globs. Android build not run (not required by the brief).
11. **MAUI mapper ordering is load-bearing**: `AppendToMapping` on the built-in `TextColor` key is required because MAUI
    defers handler mappings for bindable-property sets until after `OnPropertyChanged`; this was verified against the MAUI
    source, not assumed. Keep the append (a `ModifyMapping` would disable the stock mapping; a custom key would not run).

## Working-tree note (important)

While this task ran, another agent/parent staged the working tree (`git add`), so
`UI/PlatformComponents/ChromeText/ChromeValueLabel.Windows.cs` is `AM` in `git status`: the final
`BrushMappingMode.RelativeToBoundingBox` hunk is UNSTAGED. The parent must re-stage that file before committing.
`UNITS.md` and `MainWindowWindowsLayout.xaml` staged copies already contain this unit's changes.

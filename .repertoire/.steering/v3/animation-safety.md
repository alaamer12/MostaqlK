# Animation Safety (binding) — postmortem guardrail

Source of truth: `docs/postmoterm/long-idle-background-crash-full-investigation.md`.
Short version: on Windows, every MAUI transform animation frame allocates native WinUI
`CompositeTransform`/`ScaleTransform` objects; when the CLR **finalizer thread** later
releases them off the UI dispatcher, WinUI fail-fasts the process (`0xC000027B`, inner
`E_ILLEGAL_METHOD_CALL`) — uncatchable, silent (tray icon simply vanishes), invisible to
Sentry's managed handlers. The v1 fixes: `TransformReuseMapping` (one reused transform per
element), `MotionPreferences.IsAmbientMotionSuspended` (all ambient loops pause while
hidden-to-tray), the out-of-process watchdog, and the heartbeat log.

## Rules for ANY new or extended animation (applies to v3 morph work and beyond)

1. **Perpetual / looping transform animations are banned while ambient-suspended.** Any
   loop using `TranslateTo/ScaleTo/RotateTo/FadeTo*`, `Storyboard`s driving transforms, or
   `DispatcherTimer`-driven property mutation MUST check `MotionPreferences.IsReducedMotionRequested`
   (which already folds in `IsAmbientMotionSuspended`) at every iteration and park at a static
   state while suspended — exactly as `ShimmerBox`, `EnrichmentShimmerOverlay` and
   `LastScanStatus` do. An animation that ignores this reproduces the crash class.
2. **One-shots must be short, finite, and abortable** (`AbortAnimation`/`FinishCurrentTransition`
   pattern from `OnboardingPage.xaml.cs`); never chain one-shots into an unbounded loop.
3. **Prefer non-transform properties** for state changes — the safer new-animation palette is
   `Color`/`BackgroundColor`/plain `Opacity` sets (no `RenderTransform` churn). Instantaneous
   repaint (e.g. `BrandPalette` DynamicResource swap, `SourceSwitchTiles` active state) is the
   **preferred** morph expression; the deferred radial-reveal transition (`SourceMorphTransition`)
   must be implemented as an opacity/color cross-fade or as a single reused transform via the
   existing mapping — never as a per-frame new-transform allocation.
4. **Long-idle proof required at QA:** after introducing any new animation, before release,
   check `log\<version-tag>\heartbeat.log` across a background period (hours): `finalizePending`
   must stay flat-to-falling and no `0xC000027B` in the event log / watchdog relaunch records.
   A new animation without this evidence does not ship.
5. **Review grep gate** (run in the final wave of any UI milestone):
   `grep -rnE "TranslateToAsync|ScaleToAsync|RotateToAsync|FadeToAsync|Storyboard" UI Features` —
   every hit must map to a unit in the gated list above or carry its own suspension check.
6. **No tick or animation callback may throw into WinUI — route it through `TickerGuard`.**
   `UI/PlatformComponents/TickerGuard.cs` is the one seam every `IDispatcherTimer.Tick`
   subscription and every `Animation.Commit` callback runs inside. Reason, proven from two crash
   dumps on 2026-10-28: WinUI raises dispatcher-timer ticks through its own delegate adapter, so a
   managed exception leaving that adapter is **stowed** by combase and turned into
   `RoFailFastWithErrorContextInternal2` → `0xC000027B` (faulting chain
   `DispatcherQueueTimer::TimerCallback → TimeoutManager::Callback_OnDispatch →
   Microsoft.UI.Xaml.dll → combase → RaiseFailFastException`). `Application.UnhandledException` is
   **not** consulted on this path — the hook that saves every other XAML fault cannot help here, and
   the two dumps carried different inner errors (`0x80004002`, `0x80004005`), which is what proves
   the *escape* is the fatal event rather than any one throw. This is a second, independent
   fail-fast path, not a repeat of the finalizer/transform one above: it happens on the UI thread,
   with zero transform churn, seconds after launch. `ParserTests`
   `TestTickerCallbacksAreGuarded` is the gate that keeps it from rotting.

## Status @ dev21

The entire v3 surface (palette repaint, morph tiles, FilterModal flyout, details schema
renderer, contributed settings) introduces **zero** new transform animations (grep-verified);
all existing animated units remain under the v1 gates. The radial reveal is not yet built —
rule 3 governs its day-one design.

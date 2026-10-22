using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using WinUIVisual = Microsoft.UI.Composition.Visual;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Windows half of <see cref="SourceMorphTransition"/>: a compositor-level opacity cross-dissolve
/// on the render thread (<see cref="Microsoft.UI.Composition.Visual"/>.<c>Opacity</c> key-frame
/// animation), the animation-safety-sanctioned stand-in for the mockup's View-Transitions radial
/// reveal (see the shared file for the full rationale). Runs entirely off
/// <see cref="ElementCompositionPreview.GetElementVisual"/> so it churns ZERO managed
/// <c>CompositeTransform</c>/<c>ScaleTransform</c> objects — the exact allocation the
/// <c>TransformReuseMapping</c> postmortem identifies as the long-idle background crash cause.
/// <para>
/// Platform code lives here only (no <c>#if</c> in the shared shell, per
/// <c>cross-platform-ui-conventions.md</c>); Android/iOS/MacCatalyst get the no-op partial ⇒ instant.
/// Type aliases are used where a WinUI name would collide with a MAUI implicit-using namesake
/// (<c>Visual</c> ⇄ <c>Microsoft.Maui.Visual</c> → CS0104).
/// </para>
/// <para>
/// Cleanup is a single-shot <see cref="IDispatcherTimer"/> bounded to the reveal duration — NOT a
/// per-frame property loop (animation-safety rule 1) — that only stops the held compositor animation
/// and restores the static opacity once the render-thread dissolve lands.
/// </para>
/// </summary>
public static partial class SourceMorphTransition
{
    // The opacity the surface dips to at the dissolve's midpoint, then returns from. Kept high
    // (never near 0) so the page's own opaque background never fully shows through — the dip is a
    // decorative beat, not a hide-then-reveal that could flash a blank frame (contract §4.2's
    // "no breaking-glass" guardrail, honored without a double-buffered old layer).
    private const float DipOpacity = 0.55f;

    // The only compositor property this unit animates; opacity is a plain (non-transform) property.
    private const string OpacityPropertyName = "Opacity";

    // Small grace so the single-shot cleanup tick lands just after the render-thread animation ends.
    private const double CompletionGraceMs = 60d;

    // The visual whose dissolve is currently in flight (fast-finish target). Single reveal at a time.
    private static WinUIVisual? _runningVisual;

    // One reused single-shot timer that finalizes the dissolve; stopped + restarted per reveal, and
    // stopped outright on a fast-finish so a superseded reveal never restores over the newest one.
    private static IDispatcherTimer? _cleanupTimer;

    static partial void PerformReveal(
        VisualElement? origin,
        VisualElement targetRegion,
        Action applyStateChange,
        uint durationMs,
        ref bool handled)
    {
        if (targetRegion.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
        {
            // Handler not realized yet — nothing to animate. Leave handled=false; Run applies instantly.
            return;
        }

        // The caller's single, atomic mutation path runs FIRST and synchronously ("swap theme
        // dictionaries + content at the START" — contract §4.2). Everything after is presentational,
        // so the morph is permanent the instant this returns even if the dissolve is then aborted.
        applyStateChange();

        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            var animation = compositor.CreateScalarKeyFrameAnimation();
            animation.Duration = TimeSpan.FromMilliseconds(durationMs);
            // ease-in-out, matching the mockup's --morph-dur easing on the reveal.
            var easeInOut = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.42f, 0.0f), new Vector2(0.58f, 1.0f));
            animation.InsertKeyFrame(0.0f, 1.0f);
            animation.InsertKeyFrame(0.45f, DipOpacity, easeInOut);
            animation.InsertKeyFrame(1.0f, 1.0f, easeInOut);

            visual.Opacity = 1.0f; // static base to which the property reverts on stop
            visual.StartAnimation(OpacityPropertyName, animation);
            handled = true;

            if (EnsureCleanupTimer() is { } timer)
            {
                _runningVisual = visual;
                _running = true;
                timer.Stop();
                timer.Interval = TimeSpan.FromMilliseconds(durationMs + CompletionGraceMs);
                timer.Start();
            }
            else
            {
                // No dispatcher timer available (odd host): never leave a held animation running —
                // stop it now so the surface simply shows the already-swapped new state (instant).
                StopDissolve(visual);
            }
        }
        catch (Exception)
        {
            // A decorative transition must never take the app down. The state change already landed
            // above (and handled=true), so on any compositor failure the morph is simply instant.
            StopDissolve(element);
            _runningVisual = null;
            CompleteReveal();
        }
    }

    static partial void FastFinishCore()
    {
        // Land the in-flight dissolve at its static end state immediately, cancel its pending cleanup
        // tick, and clear the re-entrancy flag so the newest reveal can start fresh.
        _cleanupTimer?.Stop();

        if (_runningVisual is { } visual)
        {
            StopDissolve(visual);
            _runningVisual = null;
        }

        CompleteReveal();
    }

    // Releases the held compositor opacity animation and restores the fully-opaque static value.
    private static void StopDissolve(WinUIVisual visual)
    {
        visual.StopAnimation(OpacityPropertyName);
        visual.Opacity = 1.0f;
    }

    private static void StopDissolve(Microsoft.UI.Xaml.UIElement element)
    {
        try
        {
            StopDissolve(ElementCompositionPreview.GetElementVisual(element));
        }
        catch (Exception)
        {
            // best-effort visual restore during an already-failing path; nothing further to do
        }
    }

    // One reused single-shot timer whose tick finalizes whatever dissolve is currently running.
    private static IDispatcherTimer? EnsureCleanupTimer()
    {
        if (_cleanupTimer is not null)
        {
            return _cleanupTimer;
        }

        var timer = Application.Current?.Dispatcher.CreateTimer();
        if (timer is null)
        {
            return null;
        }

        timer.Tick += OnCleanupTick;
        _cleanupTimer = timer;
        return timer;
    }

    private static void OnCleanupTick(object? sender, EventArgs e)
    {
        // MAUI's IDispatcherTimer repeats until stopped; stopping here makes each reveal's finalize
        // a single one-shot (re-armed via Stop()+Start() on the next reveal).
        _cleanupTimer?.Stop();

        if (_runningVisual is { } visual)
        {
            StopDissolve(visual);
            _runningVisual = null;
        }

        CompleteReveal();
    }
}

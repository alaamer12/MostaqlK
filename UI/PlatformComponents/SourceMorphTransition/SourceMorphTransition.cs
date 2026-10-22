namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// The v3 source-morph / theme-toggle presentation wrapper (contract §4.2; the unit the mockup's
/// magicui <c>AnimatedThemeToggler</c> circular-reveal stands for). It is a PRESENTATION wrapper
/// ONLY: the caller owns the real change (the canonical <c>SourceMorph.Apply</c> triple for a tile,
/// or <c>SettingsKeys.IsDarkMode</c> + <c>UserAppTheme</c> for the theme button), and this unit
/// runs that change as an atomic, instant mutation at the START of the effect, then plays a short
/// transition around it. It never touches the pipeline and never becomes a second mutation path.
/// <para>
/// <b>Chosen mechanism (Windows): a compositor-driven radial reveal over a snapshotted OLD
/// frame.</b> The browser mockup reveals the NEW frame over a frozen OLD frame via the View
/// Transitions API. The Windows partial reproduces exactly that pair with composition primitives:
/// the current state is captured once with a WinUI <c>RenderTargetBitmap</c> and placed, through a
/// runtime-resolved window-host overlay (no <c>MainWindowPage</c>/<c>App.xaml.cs</c> edits), as a
/// non-hit-testable layer BEHIND the live content; the caller's mutation then lands instantly (the
/// live surface is now the NEW state); the live visual is clipped by a
/// <c>CompositionGeometricClip</c> whose <c>CompositionEllipseGeometry</c> <c>Radius</c> (a
/// <c>Vector2</c> of semi-axes in WinUI 3) is grown
/// from 2% to the farthest-corner distance by a render-thread key-frame animation started with
/// <c>CompositionObject.StartAnimation("Radius", …)</c> (ease-in-out), while a compositor
/// <c>ShapeVisual</c> orbit ring (3px brand stroke + 45%-alpha
/// glow stroke, their sprite geometries' own <c>Radius</c> animations riding the same wavefront, opacity
/// fading over the last 25%) rides above everything. Every animated property is a plain compositor
/// d-property on a compositor object — zero per-frame managed transforms, so the
/// <c>TransformReuseMapping</c> crash class (see <c>.repertoire/.steering/v3/animation-safety.md</c>)
/// is not reproduced; and because the clipped surface's immediate neighbour underneath is a
/// pixel-identical snapshot, no unclipped frame of the new state and no backdrop ("breaking glass")
/// frame can ever show. If the snapshot or the host overlay is unavailable (capture failure, an
/// unexpected window tree), the unit degrades to the animation-safety rule 3 cross-dissolve
/// (finite compositor <c>Visual.Opacity</c> key-frame dip) rather than a raw clip that would
/// flash the backdrop; and if even that is unavailable, to the instant swap.
/// </para>
/// <para>
/// <b>Reduced motion / any other platform / unresolved handler ⇒ instant swap.</b>
/// <see cref="MotionPreferences.IsReducedMotionRequested"/> (which already folds in the
/// ambient-hidden-to-tray state) or an OS that has no compositor partial skips everything with no
/// reveal, no ring, no settle — the app NEVER animates against the user/system preference (the
/// radar crossfade-precedent). Android/iOS/MacCatalyst intentionally get no <c>.Android.cs</c>/
/// <c>.iOS.cs</c> partial: the shared <see cref="PerformReveal"/> seam is a no-op there, so the
/// <c>handled</c> flag stays false and <see cref="Run"/> applies the change instantly (mobile morph
/// deferred on mockup evidence — desktop-only design per B5 precedent).
/// </para>
/// <para>
/// <b>Re-entrancy: fast-finish.</b> A new request while one is running immediately ends the
/// in-flight reveal (stop its compositor animations, remove the overlay layers, restore the
/// unclipped visual, and land a not-yet-applied mutation synchronously) and starts the new one, so
/// rapid tile spam always lands the newest user intent, never loses an intermediate one, and never
/// leaves a leaked clip or overlay. This is the chosen policy (vs. the mockup's plain
/// <c>transitioning</c> ignore-guard) — documented per §4.2's "pick one and document."
/// </para>
/// </summary>
public static partial class SourceMorphTransition
{
    /// <summary>Source morph duration (contract §4.3 + <c>projects.html</c> <c>morphTo(...,500)</c>).</summary>
    public const uint SourceMorphDurationMs = 500;

    /// <summary>Light/dark theme toggle duration (contract §4.3 + <c>projects.html</c> <c>toggleTheme(...,400)</c>).</summary>
    public const uint ThemeMorphDurationMs = 400;

    // A reveal (or its cross-dissolve degradation) is in-flight on a platform that actually
    // animates. Only ever true on Windows; every other target applies instantly and this stays
    // false, so the re-entrancy fast-finish path is simply never taken there.
    private static bool _running;

    /// <summary>
    /// Runs <paramref name="applyStateChange"/> (the caller's single, atomic mutation) and plays the
    /// transition around it. Reduced motion / non-Windows / an unrealized handler ⇒ instant swap.
    /// </summary>
    /// <param name="origin">
    /// The control the reveal grows from (the tapped tile or the theme button). Its center — read
    /// from the LIVE platform visuals at invocation time, never a value baked at load — is the
    /// radial clip's ellipse center and the orbit ring's center (contract §4.2 "from the clicked
    /// tile's center point"). A null/unresolvable origin falls back to the target's own center.
    /// </param>
    /// <param name="targetRegion">
    /// The surface to animate. Pass <c>null</c> to morph the whole application — resolved to the
    /// current window's root page (matches "the entire application morphs").
    /// </param>
    /// <param name="applyStateChange">The mutation to run exactly once, at the start of the effect.</param>
    /// <param name="durationMs">Reveal length; use <see cref="SourceMorphDurationMs"/> / <see cref="ThemeMorphDurationMs"/>.</param>
    public static void Run(
        VisualElement? origin,
        VisualElement? targetRegion,
        Action applyStateChange,
        uint durationMs)
    {
        ArgumentNullException.ThrowIfNull(applyStateChange);

        targetRegion ??= ResolveDefaultTarget();

        // Reduced motion, ambient-hidden-to-tray, or nothing to animate: swap instantly, no reveal,
        // no ring, no settle. Mandatory, not optional (contract §4.2 / animation-safety rule 1).
        if (MotionPreferences.IsReducedMotionRequested || targetRegion is null)
        {
            MostaqlK.Services.Diagnostics.InteractionLogger.Mark("Morph.RevealStarted", "instant");
            applyStateChange();
            return;
        }

        // Fast-finish any in-flight reveal before starting the newest one (re-entrancy policy).
        if (_running)
        {
            FastFinishCore();
        }

        var handled = false;
        PerformReveal(origin, targetRegion, applyStateChange, durationMs, ref handled);

        // No compositor path for this target/platform (or a handler not realized yet): the platform
        // seam left `handled` false, so complete the morph instantly.
        if (!handled)
        {
            applyStateChange();
        }
    }

    /// <summary>
    /// The surface a whole-application morph animates when the caller (the rail) only knows its own
    /// trigger, not the host page's content column: the visible window's root page. Resolved lazily
    /// and best-effort — a null here simply routes <see cref="Run"/> to the instant swap.
    /// </summary>
    private static VisualElement? ResolveDefaultTarget()
    {
        var window = Application.Current?.Windows?.FirstOrDefault(static w => w.Page is not null);
        return window?.Page;
    }

    /// <summary>
    /// Per-platform reveal seam (default no-op ⇒ instant everywhere except where a partial provides
    /// an implementation). An implementation that successfully takes over the transition MUST run
    /// <paramref name="applyStateChange"/> EXACTLY ONCE itself (the Windows partial does so at the
    /// start of its reveal, synchronously or on its capture continuation — see the Windows file for
    /// the guarantees) and set <paramref name="handled"/> true; leaving it false (or throwing before
    /// taking ownership) makes <see cref="Run"/> apply the change itself, exactly once.
    /// </summary>
    static partial void PerformReveal(
        VisualElement? origin,
        VisualElement targetRegion,
        Action applyStateChange,
        uint durationMs,
        ref bool handled);

    /// <summary>
    /// Immediately ends an in-flight reveal started by <see cref="PerformReveal"/> (default no-op):
    /// stops its compositor animations, removes any overlay layers it inserted, restores the
    /// unclipped fully-opaque surface, and — when the reveal had not yet applied its mutation —
    /// applies it synchronously, so the superseded intent is never lost.
    /// </summary>
    static partial void FastFinishCore();

    /// <summary>Clears the in-flight flag; called by the platform when a reveal lands or is finished.</summary>
    private static void CompleteReveal() => _running = false;
}

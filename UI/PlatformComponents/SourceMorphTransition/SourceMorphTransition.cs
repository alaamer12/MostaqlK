namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// The v3 source-morph / theme-toggle presentation wrapper (contract §4.2; the unit the mockup's
/// magicui <c>AnimatedThemeToggler</c> circular-reveal stands for). It is a PRESENTATION wrapper
/// ONLY: the caller owns the real change (the canonical <c>SourceMorph.Apply</c> triple for a tile,
/// or <c>SettingsKeys.IsDarkMode</c> + <c>UserAppTheme</c> for the theme button), and this unit
/// runs that change as an atomic, instant mutation at the START of the effect, then plays a short
/// transition around it. It never touches the pipeline and never becomes a second mutation path.
/// <para>
/// <b>Chosen mechanism (Windows): a compositor-driven opacity cross-dissolve.</b> The browser
/// mockup reveals the NEW frame over a frozen OLD frame via the View Transitions API — a pair of
/// pixel snapshots MAUI has no equivalent for, and a double-buffered old-layer would require the
/// window-root host plumbing that lives in <c>App.xaml.cs</c> / <c>MainWindowPage</c> (owned by
/// other waves). A per-frame managed radial clip is banned by
/// <c>.repertoire/.steering/v3/animation-safety.md</c> (the long-idle background crash class). The
/// sanctioned fallback that rule 3 names explicitly — an <em>opacity cross-fade of the existing
/// single surface</em> — is what ships: one finite, abortable <see cref="Microsoft.UI.Composition"/>
/// <c>Visual.Opacity</c> key-frame animation started on the render thread, so it adds ZERO
/// per-frame managed transforms. The surface is dipped and returned around the instant swap, and
/// the dip is kept well above transparent so the page's own opaque background never fully shows
/// through (no blank/"breaking-glass" frame).
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
/// in-flight dissolve (stop its compositor animation, restore opacity) and starts the new one, so
/// rapid tile spam always lands the newest user intent and never leaves a half-dissolved surface.
/// This is the chosen policy (vs. the mockup's plain <c>transitioning</c> ignore-guard) — documented
/// per §4.2's "pick one and document."
/// </para>
/// </summary>
public static partial class SourceMorphTransition
{
    /// <summary>Source morph duration (contract §4.3 + <c>projects.html</c> <c>morphTo(...,500)</c>).</summary>
    public const uint SourceMorphDurationMs = 500;

    /// <summary>Light/dark theme toggle duration (contract §4.3 + <c>projects.html</c> <c>toggleTheme(...,400)</c>).</summary>
    public const uint ThemeMorphDurationMs = 400;

    // The cross-dissolve is in-flight on a platform that actually animates. Only ever true on
    // Windows; every other target applies instantly and this stays false, so the re-entrancy
    // fast-finish path is simply never taken there.
    private static bool _running;

    /// <summary>
    /// Runs <paramref name="applyStateChange"/> (the caller's single, atomic mutation) and plays the
    /// transition around it. Reduced motion / non-Windows / an unrealized handler ⇒ instant swap.
    /// </summary>
    /// <param name="origin">
    /// The control the reveal grows from (the tapped tile or the theme button). Today it only anchors
    /// the DEFERRED orbit-ring / radial-clip center; the shipped cross-dissolve is spatially uniform,
    /// so it is accepted (and kept) rather than discarded to preserve the eventual §4.2 geometry.
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
            applyStateChange();
            return;
        }

        // Fast-finish any in-flight dissolve before starting the newest one (re-entrancy policy).
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
    /// an implementation). An implementation that successfully starts its render-thread transition
    /// MUST run <paramref name="applyStateChange"/> synchronously itself and set <paramref name="handled"/>
    /// true; leaving it false (or throwing) makes <see cref="Run"/> apply the change itself, exactly once.
    /// </summary>
    static partial void PerformReveal(
        VisualElement? origin,
        VisualElement targetRegion,
        Action applyStateChange,
        uint durationMs,
        ref bool handled);

    /// <summary>
    /// Immediately ends an in-flight dissolve started by <see cref="PerformReveal"/> (default no-op).
    /// The state mutation already applied at the start of the reveal, so fast-finishing only restores
    /// the visual to its static fully-opaque end state.
    /// </summary>
    static partial void FastFinishCore();

    /// <summary>Clears the in-flight flag; called by the platform when a reveal lands or is finished.</summary>
    private static void CompleteReveal() => _running = false;
}

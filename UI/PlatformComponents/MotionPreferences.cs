namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Reads the platform's "reduced motion" / animation accessibility preference (Mechanism 1: same
/// shape, per-OS lookup). Units that animate continuously must honour this - see
/// <c>UI/PlatformComponents/PipelineRadar/</c>, which drops its ambient scanner, breathing and
/// pulses and falls back to plain fades when motion is reduced.
/// <para>
/// Platform-specific lookup lives in the matching partial (e.g. <c>MotionPreferences.Windows.cs</c>);
/// the shared shell defaults to "motion allowed" whenever no platform partial supplies a value.
/// </para>
/// </summary>
public static partial class MotionPreferences
{
    private static volatile bool _isAmbientMotionSuspended;

    /// <summary>
    /// True when the user asked the OS to minimise animations. Falls back to <c>false</c> (motion
    /// allowed) whenever the platform does not expose the setting, so the visualisation still works.
    /// Also true while <see cref="IsAmbientMotionSuspended"/> (window hidden in the tray).
    /// </summary>
    public static bool IsReducedMotionRequested
    {
        get
        {
            // Nobody can see an animation while the window is hidden in the tray, but every frame
            // still drives native XAML work (and, before TransformReuseMapping, native garbage the
            // finalizer thread had to release - the long-idle background crash). Treat "hidden"
            // exactly like "the user asked for reduced motion" so every unit that already honours
            // this flag goes quiet in the background for free.
            if (_isAmbientMotionSuspended)
            {
                return true;
            }

            var isReduced = false;
            // partial void: no-op on platforms without a .Windows/.Android/... partial, leaving
            // isReduced = false (motion allowed) — same fallback the previous #if WINDOWS / #else
            // branch used.
            ResolveReducedMotion(ref isReduced);
            return isReduced;
        }
    }

    /// <summary>
    /// True while the app window is hidden (tray / background). Cheap static read, safe to poll from
    /// per-frame tickers. Driven by <c>AppLifecycleService.IsInBackgroundChanged</c> (wired at startup).
    /// </summary>
    public static bool IsAmbientMotionSuspended
    {
        get => _isAmbientMotionSuspended;
        set => _isAmbientMotionSuspended = value;
    }

    /// <summary>
    /// Implemented per OS (see <c>MotionPreferences.Windows.cs</c>); a no-op elsewhere, which
    /// leaves <paramref name="isReduced"/> at its default of <c>false</c> (motion allowed).
    /// </summary>
    static partial void ResolveReducedMotion(ref bool isReduced);
}

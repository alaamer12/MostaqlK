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
    /// <summary>
    /// True when the user asked the OS to minimise animations. Falls back to <c>false</c> (motion
    /// allowed) whenever the platform does not expose the setting, so the visualisation still works.
    /// </summary>
    public static bool IsReducedMotionRequested
    {
        get
        {
            var isReduced = false;
            // partial void: no-op on platforms without a .Windows/.Android/... partial, leaving
            // isReduced = false (motion allowed) — same fallback the previous #if WINDOWS / #else
            // branch used.
            ResolveReducedMotion(ref isReduced);
            return isReduced;
        }
    }

    /// <summary>
    /// Implemented per OS (see <c>MotionPreferences.Windows.cs</c>); a no-op elsewhere, which
    /// leaves <paramref name="isReduced"/> at its default of <c>false</c> (motion allowed).
    /// </summary>
    static partial void ResolveReducedMotion(ref bool isReduced);
}

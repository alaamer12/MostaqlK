namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Reads the platform's "reduced motion" / animation accessibility preference (Mechanism 1: same
/// shape, per-OS lookup). Units that animate continuously must honour this - see
/// <c>UI/PlatformComponents/PipelineRadar/</c>, which drops its ambient scanner, breathing and
/// pulses and falls back to plain fades when motion is reduced.
/// </summary>
public static class MotionPreferences
{
    /// <summary>
    /// True when the user asked the OS to minimise animations. Falls back to <c>false</c> (motion
    /// allowed) whenever the platform does not expose the setting, so the visualisation still works.
    /// </summary>
    public static bool IsReducedMotionRequested
    {
        get
        {
#if WINDOWS
            try
            {
                return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            }
            catch (Exception)
            {
                // Some unpackaged/headless Windows contexts fail to construct UISettings.
                return false;
            }
#else
            return false;
#endif
        }
    }
}

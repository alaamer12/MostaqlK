namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Windows-only lookup for <see cref="MotionPreferences"/> (V1 scope): reads
/// <c>Windows.UI.ViewManagement.UISettings.AnimationsEnabled</c>.
/// </summary>
public static partial class MotionPreferences
{
    static partial void ResolveReducedMotion(ref bool isReduced)
    {
        try
        {
            isReduced = !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch (Exception ex)
        {
            // Some unpackaged/headless Windows contexts fail to construct UISettings.
            MostaqlK.Services.Diagnostics.InteractionLogger.Fault("MotionPreferences.ResolveReducedMotion", ex);
            isReduced = false;
        }
    }
}

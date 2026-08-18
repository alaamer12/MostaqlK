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
        catch (Exception)
        {
            // Some unpackaged/headless Windows contexts fail to construct UISettings.
            isReduced = false;
        }
    }
}

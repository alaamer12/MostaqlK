namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Shared "mobile OS family" implementation for <see cref="PressableEffect"/> — behavior that
/// Android and iOS both want identically, and that desktop/mouse-driven Windows should not get
/// (a haptic tick on every mouse click would feel wrong on a desktop app). This mirrors how a
/// native <c>Pressable</c>/<c>TouchableOpacity</c> (React Native) gives touch-native tactile
/// feedback on tap — see <c>cross-platform-ui-conventions.md</c>'s "Native feel" section.
///
/// Naming: the leading underscore (<c>_PressableEffect.Mobile.cs</c>) marks this as a
/// family-shared implementation file, NOT a platform-suffixed file the .NET SDK auto-selects by
/// TargetFramework ("Mobile" is not a recognized TargetPlatformIdentifier the way "Android"/"iOS"/
/// "Windows" are) — so this file compiles for EVERY target, including Windows. It must therefore
/// contain no platform-specific APIs of its own; it is only ever invoked from the real
/// platform-suffixed files that DO get selected per target (<c>PressableEffect.Android.cs</c>
/// and <c>PressableEffect.iOS.cs</c> both call <see cref="ApplyMobilePressFeedback"/>), so on
/// Windows this method simply exists unused with zero runtime effect.
/// </summary>
public partial class PressableEffect
{
    /// <summary>
    /// Touch-native press acknowledgement shared by Android + iOS: a light haptic "click" tick on
    /// touch-down. Best-effort: haptics can be unsupported or disabled by OS/device settings, so
    /// failures are swallowed exactly like the Windows cursor-application workaround does.
    /// </summary>
    private void ApplyMobilePressFeedback()
    {
        try
        {
            Microsoft.Maui.Devices.HapticFeedback.Default.Perform(Microsoft.Maui.Devices.HapticFeedbackType.Click);
        }
        catch (Exception)
        {
            // Haptics are a nicety - some devices/OS settings disable them entirely.
        }
    }
}

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// iOS's half of <see cref="PressableEffect"/>. Structurally identical to
/// <see cref="PressableEffect.Android.cs"/> — see that file's remarks for the full reasoning
/// (hover has no touch equivalent, so both hover partial hooks stay intentionally no-op; native
/// press feel comes from the shared press/release scale+opacity feedback plus the mobile-OS-family
/// haptic tick exported from <see cref="_PressableEffect.Mobile.cs"/>).
///
/// Kept as its own file (rather than merged into the Android one) so each platform's future
/// platform-specific additions (e.g. an iOS-only spring-animation nuance) have an obvious home
/// without disturbing the shared mobile-family logic both currently rely on identically.
/// </summary>
public partial class PressableEffect
{
    partial void HandleMobilePressStarted() => ApplyMobilePressFeedback();

    // Hover hooks (HandlePointerEntered/HandlePointerExited) intentionally left as no-ops — see
    // PressableEffect.Android.cs class remarks for the shared reasoning.
}

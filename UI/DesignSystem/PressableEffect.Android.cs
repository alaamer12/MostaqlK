namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Android's half of <see cref="PressableEffect"/>.
///
/// Hover (<c>PointerEntered</c>/<c>PointerExited</c>) is a mouse/pointer concept that has no
/// equivalent on a touch screen — there is no "the finger is near but not touching" state to
/// react to. So unlike <c>PressableEffect.Windows.cs</c>, both hover partial hooks stay
/// intentionally no-op here — this is a documented design decision, not a missing implementation.
///
/// Android's native press feel instead comes from two sources: (1) the shared, already
/// cross-platform press/release scale+opacity feedback in <c>PressableEffect.cs</c>
/// (<c>OnPointerPressed</c>/<c>OnPointerReleased</c>), and (2) the mobile-OS-family haptic tick
/// exported from <c>_PressableEffect.Mobile.cs</c> (shared with iOS, see that file for why
/// it lives separately from this one) — together the touch-native "something responded to my tap"
/// pattern MAUI apps use in place of a full Material ripple when one isn't implemented.
///
/// A real Android ripple effect (via a custom handler mapper) can be added here later once the
/// project actually targets/builds for Android, without touching the shared file, the Windows
/// implementation, or the mobile-shared haptic file.
/// </summary>
public partial class PressableEffect
{
    partial void HandleMobilePressStarted() => ApplyMobilePressFeedback();

    // Hover hooks (HandlePointerEntered/HandlePointerExited) intentionally left as no-ops — see
    // class remarks above.
}

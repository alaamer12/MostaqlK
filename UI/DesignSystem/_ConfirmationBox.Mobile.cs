namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Mobile-OS-family shared stub for <see cref="ConfirmationBox.TryGetActiveNativeWindow"/> (see
/// <c>cross-platform-ui-conventions.md</c>'s <c>_X.{Family}.cs</c> pattern). Android and
/// iOS/MacCatalyst agree on the same "no native window surface yet" answer today, so the identical
/// logic lives here once instead of being duplicated in <c>ConfirmationBox.Android.cs</c> and
/// <c>ConfirmationBox.MaciOS.cs</c> (both of which just call <see cref="MobileTryGetActiveNativeWindow"/>).
/// </summary>
public static partial class ConfirmationBox
{
    private static object? MobileTryGetActiveNativeWindow() => null;
}

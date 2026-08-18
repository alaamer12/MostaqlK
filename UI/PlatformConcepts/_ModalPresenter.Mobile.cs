namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// Mobile-OS-family shared stub for <see cref="ModalPresenter"/>'s confirmation surface (see
/// <c>cross-platform-ui-conventions.md</c>'s <c>_X.{Family}.cs</c> pattern). Android and iOS agree
/// on the same "not implemented until V3" safe/non-destructive answer today, so the identical
/// logic lives here once instead of being duplicated in <c>ModalPresenter.Android.cs</c> and
/// <c>ModalPresenter.iOS.cs</c> (both of which just call <see cref="MobileShowConfirmationAsync"/>).
/// This file itself carries no platform-specific APIs and compiles unchanged on every TFM — on
/// Windows it simply exists unused, exactly like <c>_PressableEffect.Mobile.cs</c>.
/// </summary>
public static partial class ModalPresenter
{
    private static Task<ConfirmationResult> MobileShowConfirmationAsync()
        // TODO: BottomSheet — real mobile confirmation UI, added only when V3 mobile work starts.
        // Until then, any dismissal path is the safe/non-destructive outcome, never destructive.
        => Task.FromResult(new ConfirmationResult(IsSecondary: false, Remember: false));
}

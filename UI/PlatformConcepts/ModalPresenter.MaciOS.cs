namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// iOS + MacCatalyst half of <see cref="ModalPresenter"/> — both compile this file (see
/// <c>MostaqlK.csproj</c>'s <c>*.MaciOS.cs</c> multi-targeting rule) and both export the
/// mobile-family stub (see <c>_ModalPresenter.Mobile.cs</c>), since neither has a real
/// confirmation UI yet.
/// </summary>
public static partial class ModalPresenter
{
    private static partial Task<ConfirmationResult> ShowConfirmationAsyncCore(object? window, ConfirmationOptions options)
        => MobileShowConfirmationAsync();
}

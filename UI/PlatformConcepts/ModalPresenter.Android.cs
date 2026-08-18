namespace MostaqlK.UI.PlatformConcepts;

/// <summary>Android half of <see cref="ModalPresenter"/> — exports the mobile-family stub (see <c>_ModalPresenter.Mobile.cs</c>).</summary>
public static partial class ModalPresenter
{
    private static partial Task<ConfirmationResult> ShowConfirmationAsyncCore(object? window, ConfirmationOptions options)
        => MobileShowConfirmationAsync();
}

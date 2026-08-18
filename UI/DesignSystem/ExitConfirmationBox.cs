using MostaqlK.Services;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Specialization of <see cref="ConfirmationBox"/> for the X-button "keep running in background"
/// confirmation (see <see cref="CloseBehaviorService"/>). Supplies only this prompt's own Arabic
/// wording/options and maps the result to <see cref="CloseAction"/> — the reusable dialog
/// mechanism (RTL, remember checkbox, result mapping) lives in
/// <see cref="ModalPresenter"/>/<see cref="ConfirmationBox"/>, mirroring how
/// <c>SearchInputField</c> only adds its own behaviour on top of <c>DebouncedEntry</c>.
/// Drop-in replacement for the former <c>CloseConfirmationDialog.ShowAsync(window)</c>.
/// </summary>
public static class ExitConfirmationBox
{
    /// <summary>
    /// Shows the exit confirmation and returns the action the user chose plus whether
    /// "remember my choice" was checked. Must be called on the UI thread. <paramref name="window"/>
    /// stays platform-neutral (<see cref="object"/>) here for the same reason as
    /// <see cref="ConfirmationBox.ShowAsync"/> — the caller passes whatever native window handle
    /// it already has (a WinUI <c>Window</c> on Windows), with no <c>#if</c> needed at this layer.
    /// </summary>
    public static async Task<(CloseAction Action, bool Remember)> ShowAsync(object? window)
    {
        var result = await ConfirmationBox.ShowAsync(
            window,
            title: "إغلاق التطبيق",
            message: "سيبقى مستقلك يعمل في الخلفية لمتابعة رصد المشاريع الجديدة.\n" +
                     "لإغلاقه نهائيًا: أوقف الفحص من أيقونة النظام ثم اضغط الإغلاق مجددًا، أو اختر \"إغلاق نهائي\" هنا، أو أغلقه من قائمة أيقونة النظام مباشرة.",
            primaryText: "الاستمرار في الخلفية",
            secondaryText: "إغلاق نهائي",
            rememberText: "تذكر خياري ولا تسألني مرة أخرى");

        var action = result.IsSecondary ? CloseAction.Exit : CloseAction.MinimizeToTray;
        return (action, result.Remember);
    }
}

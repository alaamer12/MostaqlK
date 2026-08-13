using Microsoft.UI.Xaml.Controls;
using MostaqlK.Services;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// The "keep running in background" confirmation shown the first time the user clicks the
/// native window's X button (see <see cref="CloseBehaviorService"/> for the persisted decision
/// this dialog produces). A thin wrapper over <see cref="ConfirmationDialog"/> - it only supplies
/// this prompt's own wording/options, the reusable dialog mechanism (RTL, remember checkbox,
/// result mapping) lives there, mirroring how <c>SearchInputField</c> only adds its own
/// icon/clear-button on top of the reusable <c>DebouncedEntry</c> mechanism.
/// </summary>
public static class CloseConfirmationDialog
{
    /// <summary>
    /// Shows the dialog and returns the action the user chose plus whether "remember my choice"
    /// was checked. Awaits the native <see cref="ContentDialog.ShowAsync()"/> (via
    /// <see cref="ConfirmationDialog.ShowAsync"/>), so callers must already be on the UI thread
    /// (true for any WinUI event handler).
    /// </summary>
    public static async Task<(CloseAction Action, bool Remember)> ShowAsync(Microsoft.UI.Xaml.Window window)
    {
        var result = await ConfirmationDialog.ShowAsync(window, new ConfirmationDialog.Options
        {
            Title = "إغلاق التطبيق",
            Message = "سيبقى مستقلك يعمل في الخلفية لمتابعة رصد المشاريع الجديدة.\n" +
                      "لإغلاقه نهائيًا: أوقف الفحص من أيقونة النظام ثم اضغط الإغلاق مجددًا، أو اختر \"إغلاق نهائي\" هنا، أو أغلقه من قائمة أيقونة النظام مباشرة.",
            PrimaryButtonText = "الاستمرار في الخلفية",
            SecondaryButtonText = "إغلاق نهائي",
            RememberCheckBoxText = "تذكر خياري ولا تسألني مرة أخرى",
        });

        var action = result.IsSecondary ? CloseAction.Exit : CloseAction.MinimizeToTray;
        return (action, result.Remember);
    }
}

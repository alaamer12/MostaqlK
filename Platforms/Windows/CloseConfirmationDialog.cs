using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MostaqlK.Services;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// The "keep running in background" confirmation shown the first time the user clicks the
/// native window's X button (see <see cref="CloseBehaviorService"/> for the persisted decision
/// this dialog produces). A native WinUI <see cref="ContentDialog"/> rather than a MAUI page:
/// it is raised straight out of the WinUI <c>AppWindow.Closing</c> handler while the window may
/// already be tearing down, so it cannot depend on MAUI's own navigation stack/Shell being
/// alive - the same reasoning that keeps <c>TrayIconNativeHost</c> a plain interop class.
/// </summary>
public static class CloseConfirmationDialog
{
    /// <summary>
    /// Shows the dialog and returns the action the user chose plus whether "remember my choice"
    /// was checked. Awaits the native <see cref="ContentDialog.ShowAsync()"/>, so callers must
    /// already be on the UI thread (true for any WinUI event handler).
    /// </summary>
    public static async Task<(CloseAction Action, bool Remember)> ShowAsync(Microsoft.UI.Xaml.Window window)
    {
        var rememberCheckBox = new Microsoft.UI.Xaml.Controls.CheckBox
        {
            Content = "تذكر خياري ولا تسألني مرة أخرى",
            Margin = new Microsoft.UI.Xaml.Thickness(0, 16, 0, 0),
        };

        var messageText = new TextBlock
        {
            Text = "سيبقى مستقلك يعمل في الخلفية لمتابعة رصد المشاريع الجديدة.\n" +
                   "لإغلاقه نهائيًا: أوقف الفحص من أيقونة النظام ثم اضغط الإغلاق مجددًا، أو اختر \"إغلاق نهائي\" هنا، أو أغلقه من قائمة أيقونة النظام مباشرة.",
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(messageText);
        panel.Children.Add(rememberCheckBox);

        var dialog = new ContentDialog
        {
            Title = "إغلاق التطبيق",
            Content = panel,
            PrimaryButtonText = "الاستمرار في الخلفية",
            SecondaryButtonText = "إغلاق نهائي",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = window.Content?.XamlRoot,
        };

        var result = await dialog.ShowAsync();

        var action = result == ContentDialogResult.Secondary ? CloseAction.Exit : CloseAction.MinimizeToTray;
        return (action, rememberCheckBox.IsChecked == true);
    }
}

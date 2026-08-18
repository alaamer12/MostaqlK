using WinUIWindow = Microsoft.UI.Xaml.Window;
using WinUICheckBox = Microsoft.UI.Xaml.Controls.CheckBox;
using WinUIContentDialog = Microsoft.UI.Xaml.Controls.ContentDialog;
using WinUIContentDialogButton = Microsoft.UI.Xaml.Controls.ContentDialogButton;
using WinUIContentDialogResult = Microsoft.UI.Xaml.Controls.ContentDialogResult;
using WinUIFrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using WinUIFlowDirection = Microsoft.UI.Xaml.FlowDirection;
using WinUIStackPanel = Microsoft.UI.Xaml.Controls.StackPanel;
using WinUITextBlock = Microsoft.UI.Xaml.Controls.TextBlock;
using WinUITextWrapping = Microsoft.UI.Xaml.TextWrapping;
using WinUIThickness = Microsoft.UI.Xaml.Thickness;

namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// Windows half of <see cref="ModalPresenter"/>: shows the confirmation prompt as a native WinUI
/// <see cref="WinUIContentDialog"/>. Built directly against the platform window (not a MAUI page),
/// because callers such as the window's <c>AppWindow.Closing</c> handler may run while MAUI's own
/// Shell/navigation stack is already tearing down. Type aliases are used throughout since MAUI's
/// own <c>Window</c>/<c>CheckBox</c>/<c>Thickness</c>/<c>FlowDirection</c> types are implicitly in
/// scope in this project and would otherwise collide (CS0104) with their WinUI namesakes.
/// </summary>
public static partial class ModalPresenter
{
    /// <summary>
    /// Windows-only default-button choice for the confirmation dialog, referenced from the shared
    /// <see cref="ConfirmationOptions"/> docs above. Kept as a small internal helper rather than a
    /// public option, since only this file's <see cref="WinUIContentDialogButton"/> mapping needs it.
    /// </summary>
    private static WinUIContentDialogButton ResolveDefaultButton() => WinUIContentDialogButton.Primary;

    private static async partial Task<ConfirmationResult> ShowConfirmationAsyncCore(object? window, ConfirmationOptions options)
    {
        if (window is not WinUIWindow nativeWindow || nativeWindow.Content?.XamlRoot is not { } xamlRoot)
        {
            // No native window/XamlRoot available (e.g. called before the window fully loads) —
            // treat as the safe/non-destructive outcome rather than throwing.
            return new ConfirmationResult(IsSecondary: false, Remember: false);
        }

        WinUICheckBox? rememberCheckBox = null;
        WinUIFrameworkElement content;

        var messageBlock = new WinUITextBlock
        {
            Text = options.Message,
            TextWrapping = WinUITextWrapping.Wrap,
        };

        if (!string.IsNullOrEmpty(options.RememberCheckBoxText))
        {
            rememberCheckBox = new WinUICheckBox
            {
                Content = options.RememberCheckBoxText,
                Margin = new WinUIThickness(0, 12, 0, 0),
            };

            var stack = new WinUIStackPanel();
            stack.Children.Add(messageBlock);
            stack.Children.Add(rememberCheckBox);
            content = stack;
        }
        else
        {
            content = messageBlock;
        }

        var dialog = new WinUIContentDialog
        {
            XamlRoot = xamlRoot,
            Title = options.Title,
            Content = content,
            PrimaryButtonText = options.PrimaryButtonText,
            SecondaryButtonText = options.SecondaryButtonText,
            DefaultButton = ResolveDefaultButton(),
            // RTL fix: WinUI's ContentDialog does not inherit FlowDirection from its host window
            // automatically in every theme/style combination, so it must be set explicitly to
            // match the app's Arabic-first layout direction.
            FlowDirection = nativeWindow.Content is WinUIFrameworkElement { FlowDirection: var hostFlow }
                ? hostFlow
                : WinUIFlowDirection.RightToLeft,
        };

        var contentDialogResult = await dialog.ShowAsync();

        // Any dismissal other than an explicit Secondary pick (Primary, closing via Esc, or the
        // system back gesture) is treated as the safe/non-destructive outcome.
        var isSecondary = contentDialogResult == WinUIContentDialogResult.Secondary;
        var remember = rememberCheckBox?.IsChecked == true;

        return new ConfirmationResult(isSecondary, remember);
    }
}

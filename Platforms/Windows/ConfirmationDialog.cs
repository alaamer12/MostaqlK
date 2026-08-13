using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// Generic "primary / secondary + optional remember-my-choice checkbox" native confirmation
/// dialog. Base of the same reusable-hierarchy shape as <c>AppEntry</c> -&gt;
/// <c>DebouncedEntry</c> -&gt; <c>SearchInputField</c>: this owns the mechanism (building and
/// showing a native WinUI <see cref="ContentDialog"/>, RTL flow direction, the optional
/// "remember" <see cref="CheckBox"/>, mapping the result), while <see cref="CloseConfirmationDialog"/>
/// (and any future confirmation) supplies only its own wording/options. A native
/// <see cref="ContentDialog"/> rather than a MAUI page/alert: callers (e.g. the window's
/// <c>AppWindow.Closing</c> handler) may run while the window is already tearing down, so this
/// cannot depend on MAUI's own navigation stack/Shell being alive.
/// </summary>
public static class ConfirmationDialog
{
    /// <summary>Wording/behaviour for a single confirmation prompt.</summary>
    public sealed class Options
    {
        public required string Title { get; init; }
        public required string Message { get; init; }
        public required string PrimaryButtonText { get; init; }
        public required string SecondaryButtonText { get; init; }

        /// <summary>Text for the "remember my choice" checkbox; omit to hide it entirely.</summary>
        public string? RememberCheckBoxText { get; init; }

        public ContentDialogButton DefaultButton { get; init; } = ContentDialogButton.Primary;
    }

    /// <summary>
    /// <paramref name="IsSecondary"/> is true only when the user explicitly picked the secondary
    /// (destructive/exit) button - dismissing the dialog any other way (Primary, or closing it
    /// without a choice) is treated as the safe/non-destructive outcome, matching how callers
    /// previously handled a bare <c>ContentDialogResult</c>.
    /// </summary>
    public sealed record Result(bool IsSecondary, bool Remember);

    /// <summary>
    /// Builds and shows the dialog. Awaits the native <see cref="ContentDialog.ShowAsync()"/>, so
    /// callers must already be on the UI thread (true for any WinUI event handler).
    /// </summary>
    public static async Task<Result> ShowAsync(Microsoft.UI.Xaml.Window window, Options options)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = options.Message,
            TextWrapping = TextWrapping.Wrap,
        });

        Microsoft.UI.Xaml.Controls.CheckBox? rememberCheckBox = null;
        if (options.RememberCheckBoxText is not null)
        {
            rememberCheckBox = new Microsoft.UI.Xaml.Controls.CheckBox
            {
                Content = options.RememberCheckBoxText,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 16, 0, 0),
            };
            panel.Children.Add(rememberCheckBox);
        }

        var dialog = new ContentDialog
        {
            Title = options.Title,
            Content = panel,
            PrimaryButtonText = options.PrimaryButtonText,
            SecondaryButtonText = options.SecondaryButtonText,
            DefaultButton = options.DefaultButton,
            XamlRoot = window.Content?.XamlRoot,
            // The app is Arabic-first/RTL (see AppShell.xaml's FlowDirection="RightToLeft" and
            // every page mirroring it) - a native ContentDialog does not inherit that from the
            // MAUI-hosted content, it defaults to LTR on its own XamlRoot, which is why the close
            // confirmation was rendering mirrored (title/buttons on the wrong side, text LTR).
            FlowDirection = Microsoft.UI.Xaml.FlowDirection.RightToLeft,
        };

        var result = await dialog.ShowAsync();

        return new Result(
            IsSecondary: result == ContentDialogResult.Secondary,
            Remember: rememberCheckBox?.IsChecked == true);
    }
}

using MostaqlK.Features.Notifications.ViewModels;

namespace MostaqlK.Features.Notifications.Views;

public partial class RecentNotificationsFlyout : ContentView
{
    /// <summary>Raised when the header's X button is tapped, so the host page can hide this flyout
    /// (see <c>MainWindowPage.SetNotificationsFlyoutVisible</c>). Previously there was no way to
    /// dismiss the popover other than re-clicking whatever opened it (bell/sidebar entry).</summary>
    public event EventHandler? CloseRequested;

    public RecentNotificationsFlyout()
    {
        InitializeComponent();
    }

    public RecentNotificationsFlyout(NotificationCenterViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }

    private void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

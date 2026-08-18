using MostaqlK.Features.Notifications.ViewModels;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class MainWindowMobileLayout : ContentView
{
    private readonly NotificationCenterViewModel _notificationCenterViewModel;

    public MainWindowMobileLayout(NotificationCenterViewModel notificationCenterViewModel)
    {
        InitializeComponent();
        _notificationCenterViewModel = notificationCenterViewModel;
        NotificationsFlyout.BindingContext = _notificationCenterViewModel;
        NotificationsFlyout.CloseRequested += OnNotificationsFlyoutCloseRequested;
    }

    /// <summary>Shows the recent-notifications flyout overlay.</summary>
    public void OpenNotificationsFlyout()
    {
        SetNotificationsFlyoutVisible(true);
    }

    private void SetNotificationsFlyoutVisible(bool visible)
    {
        NotificationsFlyout.IsVisible = visible;
        NotificationsBackdrop.IsVisible = visible;
        if (visible)
        {
            _notificationCenterViewModel.MarkAllAsSeen();
        }
    }

    private void OnNotificationsFlyoutCloseRequested(object? sender, EventArgs e) =>
        SetNotificationsFlyoutVisible(false);

    private void OnNotificationsBackdropTapped(object? sender, TappedEventArgs e) =>
        SetNotificationsFlyoutVisible(false);

    private void OnNotificationsButtonTapped(object? sender, TappedEventArgs e) =>
        SetNotificationsFlyoutVisible(!NotificationsFlyout.IsVisible);
}

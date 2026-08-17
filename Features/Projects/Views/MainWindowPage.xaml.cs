using MostaqlK.Features.Notifications.ViewModels;
using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK.Features.Projects.Views;

public partial class MainWindowPage : ContentPage
{
    private readonly ProjectFeedViewModel _viewModel;
    private readonly NotificationCenterViewModel _notificationCenterViewModel;
    private readonly Services.AppLifecycleService _appLifecycleService;

    public MainWindowPage(
        ProjectFeedViewModel viewModel, 
        NotificationCenterViewModel notificationCenterViewModel,
        Services.AppLifecycleService appLifecycleService)
    {
        MostaqlK.Services.Diagnostics.InteractionLogger.Mark("MainWindowPage.Ctor", "A");
        InitializeComponent();
        _viewModel = viewModel;
        _notificationCenterViewModel = notificationCenterViewModel;
        _appLifecycleService = appLifecycleService;
        BindingContext = _viewModel;
        NotificationsFlyout.BindingContext = notificationCenterViewModel;
        NotificationsFlyout.CloseRequested += OnNotificationsFlyoutCloseRequested;
    }

    /// <summary>Shows the recent-notifications flyout overlay, used both by the sidebar entry and the tray icon's "Recent notifications" menu action.</summary>
    public void OpenNotificationsFlyout()
    {
        SetNotificationsFlyoutVisible(true);
    }

    /// <summary>
    /// Opening the notifications menu marks every notification as read (the unread badge, not
    /// the individual project cards in the feed - those keep their own separate read state).
    /// Closing it back does nothing: only the act of opening should count as "seen".
    /// </summary>
    private void SetNotificationsFlyoutVisible(bool visible)
    {
        NotificationsFlyout.IsVisible = visible;
        // The backdrop shares the flyout's visibility 1:1: it exists purely to catch outside
        // clicks (auto-dismiss) without intercepting anything while the flyout is closed.
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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        _appLifecycleService.IsReadyToNotify = true;
    }

    /// <summary>
    /// Keeps the pipeline panel's resize range honest: the panel may never grow past the point
    /// where the project feed would drop under its own minimum width, so panning simply stops
    /// there instead of squeezing the cards.
    /// </summary>
    private void OnRootSizeChanged(object? sender, EventArgs e)
    {
        const double minimumFeedWidth = 520;
        const double sidebarWidth = 256;

        var available = Root.Width - sidebarWidth - minimumFeedWidth;
        PanelSplitter.Maximum = Math.Max(PanelSplitter.Minimum, available);
    }

    private void OnPanelSplitterDragCompleted(object? sender, double width) =>
        PipelinePanel.PersistWidth();

    private void OnProjectsNavClicked(object? sender, EventArgs e)
    {
        // Already on the projects feed — no-op for now.
    }

    private async void OnAdvancedSearchNavClicked(object? sender, EventArgs e)
    {
        // TODO: navigate to the advanced search route once implemented.
        await Task.CompletedTask;
    }

    private void OnNotificationsNavClicked(object? sender, EventArgs e)
    {
        SetNotificationsFlyoutVisible(!NotificationsFlyout.IsVisible);
    }

    private async void OnSettingsNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//SettingsPanel");
    }

    private void OnNotificationsButtonTapped(object? sender, TappedEventArgs e)
    {
        SetNotificationsFlyoutVisible(!NotificationsFlyout.IsVisible);
    }

    private async void OnAboutNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//AboutPage");
    }
}

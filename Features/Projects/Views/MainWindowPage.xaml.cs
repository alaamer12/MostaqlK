using MostaqlK.Features.Notifications.ViewModels;
using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK.Features.Projects.Views;

public partial class MainWindowPage : ContentPage
{
    private readonly ProjectFeedViewModel _viewModel;

    public MainWindowPage(ProjectFeedViewModel viewModel, NotificationCenterViewModel notificationCenterViewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        NotificationsFlyout.BindingContext = notificationCenterViewModel;
    }

    /// <summary>Shows the recent-notifications flyout overlay, used both by the sidebar entry and the tray icon's "Recent notifications" menu action.</summary>
    public void OpenNotificationsFlyout()
    {
        NotificationsFlyout.IsVisible = true;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
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
        NotificationsFlyout.IsVisible = !NotificationsFlyout.IsVisible;
    }

    private async void OnSettingsNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//SettingsPanel");
    }

    private void OnNotificationsButtonTapped(object? sender, TappedEventArgs e)
    {
        NotificationsFlyout.IsVisible = !NotificationsFlyout.IsVisible;
    }

    private async void OnAboutNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//AboutPage");
    }
}

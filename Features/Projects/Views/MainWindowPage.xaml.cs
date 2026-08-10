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
        await Shell.Current.GoToAsync("SettingsPanel");
    }

    private async void OnAboutNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("AboutPage");
    }
}

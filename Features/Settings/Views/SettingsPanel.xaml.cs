using MostaqlK.Features.Settings.ViewModels;

namespace MostaqlK.Features.Settings.Views;

public partial class SettingsPanel : ContentPage
{
    public SettingsPanel(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnProjectsNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainWindowPage");
    }

    private async void OnAdvancedSearchNavClicked(object? sender, EventArgs e)
    {
        // TODO: navigate to the advanced search route once implemented.
        await Task.CompletedTask;
    }

    private void OnNotificationsNavClicked(object? sender, EventArgs e)
    {
        // Notifications flyout is owned by MainWindowPage; navigate back to it first.
        Shell.Current.GoToAsync("//MainWindowPage");
    }

    private void OnSettingsNavClicked(object? sender, EventArgs e)
    {
        // Already on Settings — no-op.
    }

    private async void OnAboutNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//AboutPage");
    }
}

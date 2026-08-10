using Microsoft.Maui.ApplicationModel;

namespace MostaqlK.Features.Projects.Views;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();

        // Pulled live from the OS/package manifest via AppInfo, never hardcoded, so this stays
        // correct across builds without editing this page.
        VersionLabel.Text = $"الإصدار {AppInfo.Current.VersionString} — MVP";
    }

    private async void OnMostaqlLinkTapped(object? sender, TappedEventArgs e)
    {
        await Launcher.Default.OpenAsync("https://mostaql.com");
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

    private async void OnNotificationsNavClicked(object? sender, EventArgs e)
    {
        // Notifications flyout is owned by MainWindowPage; navigate back to it first.
        await Shell.Current.GoToAsync("//MainWindowPage");
    }

    private async void OnSettingsNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("SettingsPanel");
    }

    private void OnAboutNavClicked(object? sender, EventArgs e)
    {
        // Already on About — no-op.
    }
}

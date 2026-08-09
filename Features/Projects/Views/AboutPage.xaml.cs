using Microsoft.Maui.ApplicationModel;

namespace MostaqlK.Features.Projects.Views;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();

        // Pulled live from the OS/package manifest via AppInfo, never hardcoded, so this stays
        // correct across builds without editing this page.
        VersionLabel.Text = $"الإصدار {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
    }

    private async void OnMostaqlLinkTapped(object? sender, TappedEventArgs e)
    {
        await Launcher.Default.OpenAsync("https://mostaql.com");
    }
}

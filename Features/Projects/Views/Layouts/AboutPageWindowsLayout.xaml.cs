using MostaqlK.Core.Navigation;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class AboutPageWindowsLayout : ContentView
{
    public AboutPageWindowsLayout()
    {
        InitializeComponent();
        try
        {
            VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
        }
        catch
        {
            VersionLabel.Text = "v1.0.4";
        }
    }

    private async void OnProjectsNavClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.Projects);
    }

    private async void OnAdvancedSearchNavClicked(object? sender, EventArgs e)
    {
        await Task.CompletedTask;
    }

    private async void OnSettingsNavClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.Settings);
    }

    private async void OnAboutNavClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.About);
    }

    private async void OnMostaqlLinkTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://mostaql.com"));
        }
        catch (Exception)
        {
            // Ignored
        }
    }
}

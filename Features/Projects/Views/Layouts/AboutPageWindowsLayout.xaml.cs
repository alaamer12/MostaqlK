using MostaqlK.Core.Navigation;
using MostaqlK.Core.Platform;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class AboutPageWindowsLayout : ContentView
{
    public AboutPageWindowsLayout()
    {
        InitializeComponent();
        try
        {
            VersionLabel.Text = $"v{AppInfo.Current.VersionString}{ChannelIdentity.Suffix}";
        }
        catch
        {
            VersionLabel.Text = $"v1.0.4{ChannelIdentity.Suffix}";
        }
    }

    public AboutPageWindowsLayout(Services.GlobalAppStatusService globalStatus) : this()
    {
        BindingContext = globalStatus;
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

    /// <summary>
    /// Rail bell: the About page owns no notifications flyout (the projects feed does), so the bell
    /// navigates back to the feed — mirroring the rail's event semantics and
    /// <c>SidebarNavigationTests.FromAbout_Click_NotificationsRow_NavigatesBackToProjects</c>.
    /// </summary>
    private async void OnRailNotificationsClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.Projects);
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

using MostaqlK.Core.Navigation;
using MostaqlK.Core.Sources;
using MostaqlK.Features.Settings.ViewModels;

namespace MostaqlK.Features.Settings.Views.Layouts;

public partial class SettingsPanelWindowsLayout : ContentView
{
    public SettingsPanelWindowsLayout()
    {
        InitializeComponent();
    }

    public SettingsPanelWindowsLayout(SettingsViewModel viewModel) : this()
    {
        BindingContext = viewModel;
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
    /// Rail bell (v3 contract §3/D7). The settings surface owns no notifications flyout — that
    /// overlay lives on the projects feed — so the bell navigates back to the feed, mirroring the
    /// rail's existing event semantics and <c>SidebarNavigationTests.FromSettings_Click_NotificationsRow_
    /// NavigatesBackToProjects</c>.
    /// </summary>
    private async void OnRailNotificationsClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.Projects);
    }

    /// <summary>
    /// A source switch (rail tiles or the header switcher) has already applied the global projection
    /// inside <see cref="SourceSwitchTiles"/>; here we only re-point this page's descriptor-driven
    /// rows so the settings surface reflects the freshly-active platform.
    /// </summary>
    private void OnRailSourceChanged(object? sender, SourceId source)
    {
        if (BindingContext is SettingsViewModel viewModel)
        {
            viewModel.RefreshForActiveSource();
        }
    }

    private async void OnViewArchiveClicked(object? sender, EventArgs e)
    {
        // "الأرشيف · عرض" jumps to the projects feed, scoped to the active source (the feed already
        // re-points via the ActivePipeline projection set by the rail).
        await AppRoutes.NavigateAsync(AppRoutes.Projects);
    }
}

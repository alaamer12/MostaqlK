using Microsoft.Maui.ApplicationModel.Communication;
using MostaqlK.Core.Navigation;
using MostaqlK.Core.Sources;
using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class ProjectDetailsWindowsLayout : ContentView
{
    private readonly ProjectDetailsViewModel? _viewModel;

    public ProjectDetailsWindowsLayout()
    {
        InitializeComponent();
    }

    public ProjectDetailsWindowsLayout(ProjectDetailsViewModel viewModel) : this()
    {
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    // Resolves the page VM either from the constructor field or, when the layout was built via the
    // parameterless constructor, from the BindingContext — so the footer actions always act on the
    // project actually on screen.
    private ProjectDetailsViewModel? ViewModel => _viewModel ?? BindingContext as ProjectDetailsViewModel;

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
    /// Rail bell: the details page owns no notifications flyout (the projects feed does), so the bell
    /// navigates back to the feed — mirroring the rail's event semantics and
    /// <c>SidebarNavigationTests.FromProjectDetails_Click_NotificationsRow_NavigatesBackToProjects</c>.
    /// </summary>
    private async void OnRailNotificationsClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.Projects);
    }

    /// <summary>Footer "نسخ رابط المشروع": copies the project's source URL to the clipboard.</summary>
    private async void OnCopyLinkClicked(object? sender, EventArgs e)
    {
        var url = ViewModel?.Details?.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            await Clipboard.Default.SetTextAsync(url);
        }
        catch (Exception ex)
        {
            InteractionLogger.Mark("ProjectDetails.CopyLinkFailed", "E", new { Url = url, Error = ex.Message });
        }
    }

    /// <summary>
    /// Footer "تحديد كمقروء والعودة": persists the read flag through the shared repository path
    /// (<see cref="ProjectDetailsViewModel.MarkAsReadAsync"/>), then returns to the feed.
    /// </summary>
    private async void OnMarkAsReadClicked(object? sender, EventArgs e)
    {
        if (ViewModel is { } vm)
        {
            await vm.MarkAsReadAsync();
        }

        await AppRoutes.NavigateAsync(AppRoutes.Projects);
    }

    /// <summary>
    /// A details page belongs to the project's own source. Swapping platforms from this page must
    /// not leave the previous source's project on screen: the rail has already applied the global
    /// projection (<c>SourceMorph.ApplyOptimistic</c>), so return to the feed, whose feed view-model
    /// re-points through its <c>ActiveSource</c> subscription. The navigation waits for the 500 ms
    /// source-morph reveal to land first — tearing this page down while its compositor reveal is
    /// still in flight reproduced the native 0xC000027B fail-fast.
    /// </summary>
    private async void OnRailSourceChanged(object? sender, SourceId source)
    {
        await Task.Delay(650);
        await AppRoutes.NavigateAsync(AppRoutes.Projects);
    }
}

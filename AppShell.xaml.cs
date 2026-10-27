using MostaqlK.Core.Navigation;
using MostaqlK.Core.Platform;
using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.Features.Projects.Views;

namespace MostaqlK;

public partial class AppShell : Shell
{
    private readonly StartupNavigation _startup;

    public AppShell(StartupNavigation startup)
    {
        MostaqlK.Services.Diagnostics.InteractionLogger.Mark("AppShell.Ctor", "A");
        _startup = startup;
        InitializeComponent();

        // Channel-derived so a canary window is never labeled like a stable install; the XAML
        // Title stays as the design-time default only.
        Title = ChannelIdentity.AppName;

        // Detail-only route (not a ShellContent tab): reached via Shell.Current.GoToAsync
        // from ProjectFeedViewModel.SelectProjectCommand.
        Routing.RegisterRoute(AppRoutes.ProjectDetailsPageName, typeof(ProjectDetailsPage));
        Routing.RegisterRoute(AppRoutes.SettingsShapesPageName, typeof(MostaqlK.Features.Settings.Views.SettingsShapesPage));
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        MostaqlK.Core.Sources.SourceId detailsSource = MostaqlK.Core.Sources.SourceId.TryFrom(_startup.SourceId, out var parsedSource)
            ? parsedSource
            : MostaqlK.Core.Sources.SourceId.Default;
        var route = _startup.DefaultPage switch
        {
            StartupPage.Settings => AppRoutes.Settings,
            StartupPage.Shapes => AppRoutes.SettingsShapes,
            StartupPage.About => AppRoutes.About,
            StartupPage.ProjectDetails => _startup.ProjectId.HasValue
                ? AppRoutes.ProjectDetails(detailsSource, _startup.ProjectId.Value)
                : AppRoutes.ProjectDetails(),
            _ => AppRoutes.Projects
        };
        await this.GoToAsync(route);
    }
}

public enum StartupPage { Projects, ProjectDetails, Settings, About, Shapes }

public sealed record StartupNavigation(StartupPage DefaultPage, long? ProjectId, string? SourceId = null)
{
    public static StartupNavigation FromArguments(string[] args)
    {
        var page = StartupPage.Projects;
        long? projectId = null;
        string? sourceId = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--default-page=", StringComparison.OrdinalIgnoreCase))
            {
                page = arg[15..].ToLowerInvariant() switch
                {
                    "project-details" => StartupPage.ProjectDetails,
                    "settings" => StartupPage.Settings,
                    "about" => StartupPage.About,
                    // Parity-harness-only entry: tools/v3_parity.py launches the CRAFT gallery
                    // with --default-page=settings-shapes. No UI affordance leads here.
                    "settings-shapes" => StartupPage.Shapes,
                    _ => StartupPage.Projects
                };
            }
            else if (arg.StartsWith("--project-id=", StringComparison.OrdinalIgnoreCase) && long.TryParse(arg[13..], out var id))
            {
                projectId = id;
            }
            else if (arg.StartsWith("--source-id=", StringComparison.OrdinalIgnoreCase) && arg[12..].Length > 0)
            {
                sourceId = arg[12..];
            }
        }
        return new StartupNavigation(page, projectId, sourceId);
    }

    /// <summary>
    /// Resolves the startup <see cref="AppTheme"/>. A `--theme=light` / `--theme=dark` argument wins;
    /// otherwise the persisted dark-mode preference decides if present; if not set, defaults to matching system (<see cref="AppTheme.Unspecified"/>).
    /// </summary>
    public static AppTheme ResolveTheme(string[] args, bool? storedIsDarkMode = null)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (arg[8..].ToLowerInvariant())
            {
                case "dark":
                    return AppTheme.Dark;
                case "light":
                    return AppTheme.Light;
            }
        }

        if (storedIsDarkMode.HasValue)
        {
            return storedIsDarkMode.Value ? AppTheme.Dark : AppTheme.Light;
        }

        return AppTheme.Unspecified;
    }

    public static AppTheme ResolveExplicitTheme(string[] args)
    {
        return ResolveTheme(args, null);
    }
}

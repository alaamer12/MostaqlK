using Microsoft.Maui.Controls;

namespace MostaqlK.Core.Navigation;

/// <summary>
/// Branded value object representing a verified, strongly-typed Shell navigation route.
/// </summary>
public readonly record struct AppRoute
{
    public string Path { get; }

    public AppRoute(string path)
    {
        Path = path ?? string.Empty;
    }

    public override string ToString() => Path;

    public static implicit operator string(AppRoute route) => route.Path;
    public static explicit operator AppRoute(string path) => new(path);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Path);
}

/// <summary>
/// Single authoritative ground for all application navigation routes, query parameters,
/// and typed navigation dispatchers.
/// </summary>
public static class AppRoutes
{
    // Route Names (for Shell registration)
    public const string MainWindowPageName = "MainWindowPage";
    public const string MainPageName = "MainPage";
    public const string SettingsPanelName = "SettingsPanel";
    public const string AboutPageName = "AboutPage";
    public const string ProjectDetailsPageName = "ProjectDetailsPage";

    // Query Parameter Keys
    public const string ProjectIdQueryParam = "projectId";

    // Typed Absolute Routes (Top-level Shell destinations)
    public static readonly AppRoute MainWindow = new("//" + MainWindowPageName);
    public static readonly AppRoute Projects = MainWindow;
    public static readonly AppRoute Settings = new("//" + SettingsPanelName);
    public static readonly AppRoute About = new("//" + AboutPageName);
    public static readonly AppRoute Main = new("//" + MainPageName);

    // Parameterized Route Builders
    public static AppRoute ProjectDetails(long projectId) =>
        new($"{ProjectDetailsPageName}?{ProjectIdQueryParam}={projectId}");

    public static AppRoute ProjectDetails(string? projectId = null) =>
        string.IsNullOrWhiteSpace(projectId)
            ? new(ProjectDetailsPageName)
            : new($"{ProjectDetailsPageName}?{ProjectIdQueryParam}={projectId}");

    /// <summary>
    /// Executes typed navigation on the current Shell instance.
    /// Safely switches to the main UI thread if not already running on it.
    /// </summary>
    public static async Task NavigateAsync(AppRoute route, bool animate = true)
    {
        if (Shell.Current is null || route.IsEmpty)
        {
            return;
        }

        if (MainThread.IsMainThread)
        {
            await Shell.Current.GoToAsync(route.Path, animate);
        }
        else
        {
            await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(route.Path, animate));
        }
    }

    /// <summary>
    /// Extension method on Shell for typed route navigation.
    /// </summary>
    public static Task GoToAsync(this Shell shell, AppRoute route, bool animate = true)
    {
        ArgumentNullException.ThrowIfNull(shell);
        return shell.GoToAsync(route.Path, animate);
    }
}

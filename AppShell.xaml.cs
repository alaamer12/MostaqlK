using MostaqlK.Features.Projects.Views;
using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK;

public partial class AppShell : Shell
{
	private readonly StartupNavigation _startup;

	public AppShell(StartupNavigation startup)
	{
		MostaqlK.Services.Diagnostics.InteractionLogger.Mark("AppShell.Ctor", "A");
		_startup = startup;
		InitializeComponent();

		// Detail-only route (not a ShellContent tab): reached via Shell.Current.GoToAsync
		// from ProjectFeedViewModel.SelectProjectCommand.
		Routing.RegisterRoute(nameof(ProjectDetailsPage), typeof(ProjectDetailsPage));
		Loaded += OnLoaded;
	}

	private async void OnLoaded(object? sender, EventArgs e)
	{
		Loaded -= OnLoaded;
		var route = _startup.DefaultPage switch
		{
			StartupPage.Settings => "//SettingsPanel",
			StartupPage.About => "//AboutPage",
			StartupPage.ProjectDetails => $"{nameof(ProjectDetailsPage)}?projectId={_startup.ProjectId?.ToString() ?? string.Empty}",
			_ => "//MainWindowPage"
		};
		await GoToAsync(route);
	}
}

public enum StartupPage { Projects, ProjectDetails, Settings, About }

public sealed record StartupNavigation(StartupPage DefaultPage, long? ProjectId)
{
	public static StartupNavigation FromArguments(string[] args)
	{
		var page = StartupPage.Projects;
		long? projectId = null;
		foreach (var arg in args)
		{
			if (arg.StartsWith("--default-page=", StringComparison.OrdinalIgnoreCase))
			{
				page = arg[15..].ToLowerInvariant() switch
				{
					"project-details" => StartupPage.ProjectDetails,
					"settings" => StartupPage.Settings,
					"about" => StartupPage.About,
					_ => StartupPage.Projects
				};
			}
			else if (arg.StartsWith("--project-id=", StringComparison.OrdinalIgnoreCase) && long.TryParse(arg[13..], out var id))
			{
				projectId = id;
			}
		}
		return new StartupNavigation(page, projectId);
	}

	/// <summary>
	/// Resolves the startup <see cref="AppTheme"/>. A `--theme=light` / `--theme=dark` argument wins;
	/// otherwise the persisted dark-mode preference decides (light by default, per the mockups).
	/// </summary>
	public static AppTheme ResolveTheme(string[] args, bool storedIsDarkMode)
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

		return storedIsDarkMode ? AppTheme.Dark : AppTheme.Light;
	}

	public static AppTheme ResolveExplicitTheme(string[] args)
	{
		foreach (var arg in args)
		{
			if (!arg.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase)) continue;
			if (arg[8..].Equals("dark", StringComparison.OrdinalIgnoreCase)) return AppTheme.Dark;
			if (arg[8..].Equals("light", StringComparison.OrdinalIgnoreCase)) return AppTheme.Light;
		}

		return AppTheme.Unspecified;
	}
}

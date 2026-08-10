using MostaqlK.Features.Projects.Views;
using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK;

public partial class AppShell : Shell
{
	private readonly StartupNavigation _startup;

	public AppShell(StartupNavigation startup)
	{
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
}

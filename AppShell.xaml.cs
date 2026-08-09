using MostaqlK.Features.Projects.Views;

namespace MostaqlK;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Detail-only route (not a ShellContent tab): reached via Shell.Current.GoToAsync
		// from ProjectFeedViewModel.SelectProjectCommand.
		Routing.RegisterRoute(nameof(ProjectDetailsPage), typeof(ProjectDetailsPage));
	}
}

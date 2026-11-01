using MostaqlK.Core.Navigation;
using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class ProjectDetailsMobileLayout : ContentView
{
    public ProjectDetailsMobileLayout()
    {
        InitializeComponent();
    }

    public ProjectDetailsMobileLayout(ProjectDetailsViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        // FEED BUG HUNT (dev74): temporary.
        MostaqlK.Services.Diagnostics.InteractionLogger.Mark("FeedDiag.BackToFeed", "mobileBack",
            new { route = MostaqlK.Core.Navigation.AppRoutes.Projects.Path });
        await AppRoutes.NavigateAsync(AppRoutes.Projects);
    }
}

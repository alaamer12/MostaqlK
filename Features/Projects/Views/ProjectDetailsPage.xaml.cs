using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK.Features.Projects.Views;

[QueryProperty(nameof(ProjectId), "projectId")]
public partial class ProjectDetailsPage : ContentPage
{
    private readonly ProjectDetailsViewModel _viewModel;

    public string? ProjectId { get; set; }

    public ProjectDetailsPage(ProjectDetailsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (long.TryParse(ProjectId, out var projectId))
        {
            await _viewModel.LoadAsync(projectId);
        }
    }
}

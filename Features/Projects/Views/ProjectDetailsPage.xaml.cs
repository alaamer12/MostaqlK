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

    private async void OnProjectsNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainWindowPage");
    }

    private async void OnAdvancedSearchNavClicked(object? sender, EventArgs e)
    {
        // TODO: navigate to the advanced search route once implemented.
        await Task.CompletedTask;
    }

    private async void OnNotificationsNavClicked(object? sender, EventArgs e)
    {
        // Notifications flyout is owned by MainWindowPage; navigate back to it first.
        await Shell.Current.GoToAsync("//MainWindowPage");
    }

    private async void OnSettingsNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("SettingsPanel");
    }

    private async void OnAboutNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("AboutPage");
    }
}

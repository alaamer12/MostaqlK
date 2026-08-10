using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.Infrastructure.Database;

namespace MostaqlK.Features.Projects.Views;

[QueryProperty(nameof(ProjectId), "projectId")]
public partial class ProjectDetailsPage : ContentPage
{
    private readonly ProjectDetailsViewModel _viewModel;
    private readonly IProjectRepository _projectRepository;

    public string? ProjectId { get; set; }

    public ProjectDetailsPage(ProjectDetailsViewModel viewModel, IProjectRepository projectRepository)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _projectRepository = projectRepository;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (string.IsNullOrWhiteSpace(ProjectId))
        {
            var newest = await _projectRepository.GetNewestProjectIdAsync();
            if (!newest.IsOk)
            {
                _viewModel.SetError(newest.Error.ExternalMessage);
            }
            else if (newest.Value is long newestId)
            {
                await _viewModel.LoadAsync(newestId);
            }
            else
            {
                _viewModel.SetError("لا توجد مشاريع محفوظة لعرضها.");
            }
        }
        else if (long.TryParse(ProjectId, out var projectId) && projectId > 0)
        {
            await _viewModel.LoadAsync(projectId);
        }
        else
        {
            _viewModel.SetError("معرّف المشروع غير صالح.");
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

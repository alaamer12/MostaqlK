using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.Features.Projects.Views.Layouts;
using MostaqlK.Infrastructure.Database;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Projects.Views;

[QueryProperty(nameof(ProjectId), "projectId")]
public partial class ProjectDetailsPage : ContentPage
{
    private readonly ProjectDetailsViewModel _viewModel;
    private readonly IProjectRepository _projectRepository;
    private readonly View? _activeLayout;

    public string? ProjectId { get; set; }

    public ProjectDetailsPage(ProjectDetailsViewModel viewModel, IProjectRepository projectRepository)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _projectRepository = projectRepository;
        BindingContext = _viewModel;

        var layoutFactory = PlatformSelect.For<Func<View>>(
            windows: () => new ProjectDetailsWindowsLayout(_viewModel),
            android: () => new ProjectDetailsMobileLayout(_viewModel),
            ios: () => new ProjectDetailsMobileLayout(_viewModel),
            macCatalyst: () => new ProjectDetailsWindowsLayout(_viewModel)
        );
        _activeLayout = layoutFactory?.Invoke();
        Content = _activeLayout;
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
}

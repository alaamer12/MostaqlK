using MostaqlK.Core.Sources;
using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.Features.Projects.Views.Layouts;
using MostaqlK.Infrastructure.Database;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Projects.Views;

[QueryProperty(nameof(ProjectId), "projectId")]
[QueryProperty(nameof(SourceIdParam), "sourceId")]
public partial class ProjectDetailsPage : ContentPage
{
    private readonly ProjectDetailsViewModel _viewModel;
    private readonly IProjectRepository _projectRepository;
    private readonly View? _activeLayout;

    public string? ProjectId { get; set; }

    /// <summary>Source the project belongs to; absent (deep links from older builds) means the default source.</summary>
    public string? SourceIdParam { get; set; }

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
        var source = SourceId.TryFrom(SourceIdParam, out var parsedSource)
            ? parsedSource
            : SourceId.Default;
        if (string.IsNullOrWhiteSpace(ProjectId))
        {
            var newest = await _projectRepository.GetNewestProjectIdAsync(source);
            if (!newest.IsOk)
            {
                _viewModel.SetError(newest.Error.ExternalMessage);
            }
            else if (newest.Value is SourceProjectId newestId)
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
            await _viewModel.LoadAsync(new SourceProjectId(source, projectId));
        }
        else
        {
            _viewModel.SetError("معرّف المشروع غير صالح.");
        }
    }
}

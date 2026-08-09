using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Models;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>Per-attachment wrapper exposing a download action + live status for a single <see cref="Asset"/>.</summary>
public sealed partial class AttachmentItemViewModel : ObservableObject
{
    private readonly AssetDownloadService _assetDownloadService;

    public Asset Asset { get; }

    public string FileName => Asset.FileName;

    public string? SizeText => Asset.SizeText;

    [ObservableProperty]
    private AttachmentStatus? _status;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isResolving;

    public AttachmentItemViewModel(Asset asset, AssetDownloadService assetDownloadService)
    {
        Asset = asset;
        _assetDownloadService = assetDownloadService;
    }

    [RelayCommand]
    public async Task ResolveAsync()
    {
        IsResolving = true;
        try
        {
            var resolution = await _assetDownloadService.ResolveAsync(Asset);
            Status = resolution.Status;
            StatusMessage = resolution.Message ?? resolution.LocalPath ?? resolution.Url;
        }
        finally
        {
            IsResolving = false;
        }
    }
}

/// <summary>
/// View-model for the project details page (project-details.html): loads a
/// <see cref="ProjectDetails"/> by id and exposes skills/budget/owner-stats/attachments, each
/// attachment wrapped in an <see cref="AttachmentItemViewModel"/> wired to
/// <see cref="AssetDownloadService"/>.
/// </summary>
public sealed partial class ProjectDetailsViewModel : ObservableObject
{
    private readonly IProjectRepository _projectRepository;
    private readonly AssetDownloadService _assetDownloadService;

    public ObservableCollection<ProjectSkill> Skills { get; } = [];

    public ObservableCollection<AttachmentItemViewModel> Attachments { get; } = [];

    [ObservableProperty]
    private ProjectDetails? _details;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>True once loading has finished with no error — drives the success-state ScrollView.</summary>
    public bool ShowDetails => !IsLoading && !HasError && Details is not null;

    public ProjectDetailsViewModel(IProjectRepository projectRepository, AssetDownloadService assetDownloadService)
    {
        _projectRepository = projectRepository;
        _assetDownloadService = assetDownloadService;
    }

    [RelayCommand]
    public async Task LoadAsync(long projectId)
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;
        try
        {
            var result = await _projectRepository.GetDetailsAsync(projectId);
            if (!result.IsOk)
            {
                HasError = true;
                ErrorMessage = result.Error.ExternalMessage;
                return;
            }

            if (result.Value is null)
            {
                HasError = true;
                ErrorMessage = "لم يتم العثور على تفاصيل هذا المشروع.";
                return;
            }

            Details = result.Value;

            Skills.Clear();
            foreach (var skill in Details.Skills)
            {
                Skills.Add(skill);
            }

            Attachments.Clear();
            foreach (var asset in Details.Attachments)
            {
                Attachments.Add(new AttachmentItemViewModel(asset, _assetDownloadService));
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowDetails));
        }
    }
}

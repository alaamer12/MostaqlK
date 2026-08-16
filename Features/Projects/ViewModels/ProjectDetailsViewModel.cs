using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Models;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>Per-attachment wrapper exposing a download action + live status for a single <see cref="Asset"/>.</summary>
public sealed partial class AttachmentItemViewModel : ObservableObject
{
    private readonly AssetDownloadService _assetDownloadService;

    public Asset Asset { get; }

    public string FileName => Asset.FileName;

    public string? SizeText => Asset.SizeText;

    [ObservableProperty]
    public partial AttachmentStatus? Status { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsResolving { get; set; }

    public AttachmentItemViewModel(Asset asset, AssetDownloadService assetDownloadService)
    {
        Asset = asset;
        _assetDownloadService = assetDownloadService;
    }

    [TraceInteraction("ResolveCommand")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "ResolveCommand")]
    [RelayCommand]
    public async Task ResolveAsync()
    {
        using var _ = TraceScope.Begin("ResolveCommand", new { FileName });
        IsResolving = true;
        try
        {
            var resolution = await _assetDownloadService.ResolveAsync(Asset);
            Status = resolution.Status;
            StatusMessage = resolution.Message ?? resolution.LocalPath ?? resolution.Url;
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
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
    private readonly GlobalAppStatusService _globalStatus;

    public ObservableCollection<ProjectSkill> Skills { get; } = [];

    public ObservableCollection<AttachmentItemViewModel> Attachments { get; } = [];

    [ObservableProperty]
    public partial ProjectDetails? Details { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>True once loading has finished with no error — drives the success-state ScrollView.</summary>
    public bool ShowDetails => !IsLoading && !HasError && Details is not null;

    public GlobalAppStatusService GlobalStatus => _globalStatus;

    public ProjectDetailsViewModel(IProjectRepository projectRepository, AssetDownloadService assetDownloadService, GlobalAppStatusService globalStatus)
    {
        _projectRepository = projectRepository;
        _assetDownloadService = assetDownloadService;
        _globalStatus = globalStatus;
    }

    [RelayCommand]
    public async Task OpenOnMostaqlAsync()
    {
        var url = Details?.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            if (Details?.ProjectId > 0)
            {
                url = $"https://mostaql.com/project/{Details.ProjectId}";
            }
            else
            {
                return;
            }
        }

        try
        {
            await Launcher.Default.OpenAsync(url);
        }
        catch (Exception ex)
        {
            MostaqlK.Services.Diagnostics.InteractionLogger.Fault("ProjectDetailsViewModel.OpenOnMostaqlAsync", ex);
        }
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

    public void SetError(string message)
    {
        Details = null;
        Skills.Clear();
        Attachments.Clear();
        HasError = true;
        ErrorMessage = message;
        IsLoading = false;
        OnPropertyChanged(nameof(ShowDetails));
    }
}

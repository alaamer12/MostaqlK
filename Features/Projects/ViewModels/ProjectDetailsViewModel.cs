using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Core.Formatting;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Models;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;
using MostaqlK.UI.DesignSystem.Badges;
using MostaqlK.UI.PlatformComponents;

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

    [ObservableProperty]
    public partial bool IsDownloaded { get; set; }

    private string? _localPath;

    public AttachmentItemViewModel(Asset asset, AssetDownloadService assetDownloadService)
    {
        Asset = asset;
        _assetDownloadService = assetDownloadService;
        
        // Initial state: if Asset already has a LocalPath, it's downloaded.
        if (!string.IsNullOrEmpty(asset.LocalPath) && File.Exists(asset.LocalPath))
        {
            IsDownloaded = true;
            _localPath = asset.LocalPath;
        }
    }

    [RelayCommand]
    public async Task BrowseAsync()
    {
        var url = Asset.RawUrl ?? Asset.Url;
        if (!string.IsNullOrEmpty(url))
        {
            await Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(url);
        }
    }

    [RelayCommand]
    public async Task RevealAsync()
    {
        if (string.IsNullOrEmpty(_localPath) || !File.Exists(_localPath)) return;

        try
        {
            // Platform-specific reveal (Explorer /select on Windows, open folder elsewhere)
            // lives behind IFileRevealService — no ad hoc #if WINDOWS at this call site.
            await FileRevealService.Current.RevealAsync(_localPath);
        }
        catch (Exception ex)
        {
            InteractionLogger.Mark("AttachmentItem.RevealFailed", "E", new { FileName, Error = ex.Message });
        }
    }

    [TraceInteraction("ResolveCommand")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "ResolveCommand")]
    [RelayCommand]
    public async Task ResolveAsync()
    {
        using var _ = TraceScope.Begin("ResolveCommand", new { FileName });
        IsResolving = true;
        StatusMessage = "جارٍ التحميل...";
        try
        {
            var resolution = await _assetDownloadService.ResolveAsync(Asset);
            Status = resolution.Status;
            StatusMessage = resolution.Message ?? resolution.LocalPath ?? resolution.Url;

            // Actually act on the resolution: open the URL or the downloaded file.
            if (resolution.Status == AttachmentStatus.ReadyUrl && !string.IsNullOrEmpty(resolution.Url))
            {
                await Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(resolution.Url);
            }
            else if (resolution.Status == AttachmentStatus.Downloaded && !string.IsNullOrEmpty(resolution.LocalPath))
            {
                IsDownloaded = true;
                _localPath = resolution.LocalPath;
                
                // On Windows, we can open the file directly.
                await Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(new Microsoft.Maui.ApplicationModel.OpenFileRequest
                {
                    File = new Microsoft.Maui.Storage.ReadOnlyFile(resolution.LocalPath)
                });
            }
            else if (resolution.Status == AttachmentStatus.ManualDownloadRequired && !string.IsNullOrEmpty(Asset.RawUrl))
            {
                // If manual download is required, we still want to help the user by opening the link.
                await Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(Asset.RawUrl);
            }
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
    [NotifyPropertyChangedFor(nameof(EnrichmentStatus))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeText))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeBackground))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeForeground))]
    [NotifyPropertyChangedFor(nameof(EnrichmentBadgeIcon))]
    [NotifyPropertyChangedFor(nameof(Budget))]
    [NotifyPropertyChangedFor(nameof(Duration))]
    [NotifyPropertyChangedFor(nameof(PublishTimeText))]
    [NotifyPropertyChangedFor(nameof(ProposalCountText))]
    [NotifyPropertyChangedFor(nameof(OwnerName))]
    [NotifyPropertyChangedFor(nameof(OwnerRegisteredAt))]
    [NotifyPropertyChangedFor(nameof(OwnerHiringRateText))]
    [NotifyPropertyChangedFor(nameof(OwnerOpenProjectsText))]
    [NotifyPropertyChangedFor(nameof(OwnerInProgressProjectsText))]
    [NotifyPropertyChangedFor(nameof(OwnerOngoingCommunicationsText))]
    [NotifyPropertyChangedFor(nameof(OwnerCompletedProjectsText))]
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
    
    public EnrichmentStatus EnrichmentStatus => Details?.EnrichmentStatus ?? EnrichmentStatus.Pending;

    public string EnrichmentBadgeText => EnrichmentBadgeStyle.GetText(EnrichmentStatus);

    public string EnrichmentBadgeBackground => EnrichmentBadgeStyle.GetBackgroundHex(EnrichmentStatus);

    public string EnrichmentBadgeForeground => EnrichmentBadgeStyle.GetForegroundHex(EnrichmentStatus);

    public AppIconGlyph EnrichmentBadgeIcon => EnrichmentBadgeStyle.GetIcon(EnrichmentStatus);

    /// <summary>Formatted budget in canonical currency presentation (e.g., "2,500 - 5,500 ر.س").</summary>
    public string Budget => Details?.Budget is not null ? BudgetFormatter.Format(Details.Budget) : "—";

    /// <summary>Formatted delivery duration in canonical Arabic plural form.</summary>
    public string Duration => Details?.DeliveryDays is int days ? ArabicRelativeTime.Days(days) : "—";

    /// <summary>Relative publish time computed dynamically from discovered timestamp.</summary>
    public string PublishTimeText => Details is not null ? ArabicRelativeTime.Since(Details.DiscoveredAt) : "—";

    /// <summary>Formatted proposal count in canonical Arabic plural form.</summary>
    public string ProposalCountText => Details is not null ? ArabicProposalParser.Format(Details.ProposalCount) : "—";

    /// <summary>Owner name or fallback.</summary>
    public string OwnerName => !string.IsNullOrWhiteSpace(Details?.Owner?.Name) ? Details.Owner.Name : "—";

    /// <summary>Owner registration date or fallback.</summary>
    public string OwnerRegisteredAt => !string.IsNullOrWhiteSpace(Details?.Owner?.RegisteredAt) ? Details.Owner.RegisteredAt : "—";

    /// <summary>Formatted owner hiring rate (e.g., "50%", "6.25%" or "لم يحسب بعد").</summary>
    public string OwnerHiringRateText => Details?.Owner?.HiringRatePercent is double rate
        ? $"{rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%"
        : "لم يحسب بعد";

    /// <summary>Owner open projects count or fallback.</summary>
    public string OwnerOpenProjectsText => Details?.Owner?.OpenProjectsCount is int count ? count.ToString() : "0";

    /// <summary>Owner in-progress projects count or fallback.</summary>
    public string OwnerInProgressProjectsText => Details?.Owner?.InProgressProjectsCount is int count ? count.ToString() : "0";

    /// <summary>Owner ongoing communications count or fallback.</summary>
    public string OwnerOngoingCommunicationsText => Details?.Owner?.OngoingCommunicationsCount is int count ? count.ToString() : "0";

    /// <summary>Owner completed projects count or fallback.</summary>
    public string OwnerCompletedProjectsText => Details?.Owner?.CompletedProjectsCount is int count ? count.ToString() : "0";

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

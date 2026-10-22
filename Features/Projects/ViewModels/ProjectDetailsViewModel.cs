using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Core.Formatting;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Models;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;
using MostaqlK.Services.Pipeline;
using MostaqlK.UI.DesignSystem.Badges;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>Per-attachment wrapper exposing a download action + live status for a single <see cref="Asset"/>.</summary>
public sealed partial class AttachmentItemViewModel : ObservableObject
{
    private readonly AssetDownloadService _assetDownloadService;
    private readonly SourceId _source;

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

    public AttachmentItemViewModel(Asset asset, AssetDownloadService assetDownloadService, SourceId source)
    {
        Asset = asset;
        _assetDownloadService = assetDownloadService;
        _source = source;

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
            var resolution = await _assetDownloadService.ResolveAsync(_source, Asset);
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
    private readonly ActivePipeline _activePipeline;

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
    [NotifyPropertyChangedFor(nameof(ShowExecutorSection))]
    [NotifyPropertyChangedFor(nameof(ExecutorName))]
    [NotifyPropertyChangedFor(nameof(ShowRetryAffordance))]
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

    /// <summary>Formatted budget with the project source's currency suffix (e.g., "2,500 - 5,500" + suffix).</summary>
    public string Budget => Details?.Budget is not null ? BudgetFormatter.Format(Details.Budget, MostaqlK.Core.Sources.SourceRegistryProvider.DescriptorFor(Details.Source)?.CurrencySuffix ?? string.Empty) : "—";

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

    // -----------------------------------------------------------------
    // Schema-driven / capability-gated composition (B4, contract §1/§7/§9)
    // -----------------------------------------------------------------

    /// <summary>
    /// The meta rows for every source section, grouped by the descriptor's <c>SchemaMap</c> section
    /// and ordered within each group by its <c>Order</c>. Built by <see cref="DetailSchemaRenderer"/>
    /// straight from <c>descriptor.SchemaMap</c> + the loaded <see cref="Details"/>; a field with no
    /// value is omitted, and the executor group only appears when the source's
    /// <see cref="SourceCapabilities.HasExecutorInfo"/> holds AND the project carries an executor name.
    /// Empty ⇒ no section is rendered (never an empty header).
    /// </summary>
    public IReadOnlyList<DetailSectionGroup> MetaSections { get; private set; } = [];

    /// <summary>
    /// Real UI read of <see cref="SourceCapabilities.HasExecutorInfo"/> (contract §1): the executor
    /// affordance is present iff the active source's capability says so AND the row carries a name.
    /// The layout binds section visibility to this so an absent capability never renders empty.
    /// </summary>
    public bool ShowExecutorSection =>
        SourceDescriptor?.Capabilities.HasExecutorInfo == true
        && !string.IsNullOrWhiteSpace(Details?.ExecutorName);

    /// <summary>The executor name for the gated block (only meaningful when <see cref="ShowExecutorSection"/>).</summary>
    public string ExecutorName => Details?.ExecutorName ?? string.Empty;

    /// <summary>
    /// §9 retry affordance: shown only on a project whose enrichment failed (reachable-but-gated page).
    /// Retrying re-enqueues it through the ACTIVE pipeline's queue (<see cref="ActivePipeline"/>), the
    /// one seam the parent shipped for acting on whichever source is currently shown.
    /// </summary>
    public bool ShowRetryAffordance => Details?.EnrichmentStatus == EnrichmentStatus.Failed;

    /// <summary>Skills chip block is rendered only when the project actually has skills (absent ⇒ no section).</summary>
    public bool HasSkills => Skills.Count > 0;

    /// <summary>Attachments block is rendered only when the project actually has attachments (absent ⇒ no section).</summary>
    public bool HasAttachments => Attachments.Count > 0;

    private ISourceDescriptor? SourceDescriptor =>
        Details is null ? null : SourceRegistryProvider.DescriptorFor(Details.Source);

    [RelayCommand]
    public async Task RetryEnrichmentAsync()
    {
        if (Details is not { } details || details.EnrichmentStatus != EnrichmentStatus.Failed)
        {
            return;
        }

        // Re-enqueue the composite identity on the ACTIVE pipeline's queue; the worker picks it up
        // on its next pass. Optimistically drop back to Pending so the badge/affordance reflect the
        // re-queued state immediately (the store-and-forget row keeps its last known facts meanwhile).
        await _activePipeline.Queue.EnqueueAsync(new SourceProjectId(details.Source, details.ProjectId));
        details.EnrichmentStatus = EnrichmentStatus.Pending;
        Details = null;
        Details = details;
    }

    public ProjectDetailsViewModel(
        IProjectRepository projectRepository,
        AssetDownloadService assetDownloadService,
        GlobalAppStatusService globalStatus,
        ActivePipeline activePipeline)
    {
        _projectRepository = projectRepository;
        _assetDownloadService = assetDownloadService;
        _globalStatus = globalStatus;
        _activePipeline = activePipeline;
    }

    [RelayCommand]
    public async Task OpenOnSourceAsync()
    {
        var url = Details?.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            if (Details?.ProjectId > 0)
            {
                url = MostaqlK.Core.Sources.SourceRegistryProvider.DescriptorFor(Details.Source)?.BuildProjectUrl(Details.ProjectId) ?? string.Empty;
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
            MostaqlK.Services.Diagnostics.InteractionLogger.Fault("ProjectDetailsViewModel.OpenOnSourceAsync", ex);
        }
    }

    [RelayCommand]
    public async Task LoadAsync(SourceProjectId projectId)
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
                Attachments.Add(new AttachmentItemViewModel(asset, _assetDownloadService, Details.Source));
            }

            // Schema-driven meta rows: read the descriptor's SchemaMap/Capabilities, never the source id.
            RebuildMetaSections();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowDetails));
            OnPropertyChanged(nameof(HasSkills));
            OnPropertyChanged(nameof(HasAttachments));
        }
    }

    /// <summary>
    /// Rebuilds <see cref="MetaSections"/> from the descriptor's <c>SchemaMap</c> + the loaded
    /// <see cref="Details"/>, grouping the renderer's rows by section (in first-appearance order)
    /// while preserving within-section <c>Order</c>. Called whenever the details/skill state changes.
    /// </summary>
    private void RebuildMetaSections()
    {
        var descriptor = SourceDescriptor;
        var details = Details;
        if (descriptor is null || details is null)
        {
            MetaSections = [];
            return;
        }

        MetaSections = DetailSchemaRenderer.Render(descriptor, details)
            .GroupBy(r => r.Section)
            .Select(g => new DetailSectionGroup(
                g.Key,
                DetailSchemaRenderer.SectionTitleAr(g.Key),
                g.OrderBy(r => r.Order).ToList()))
            .ToList();

        OnPropertyChanged(nameof(MetaSections));
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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Core.Formatting;
using MostaqlK.Core.Platform;
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
    private readonly SourceFeedStore _store;

    /// <summary>The id of the last load request — the Broken state's retry re-issues exactly this.</summary>
    private SourceProjectId _lastRequested;

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
    [NotifyPropertyChangedFor(nameof(SourceNameAr))]
    [NotifyPropertyChangedFor(nameof(ShowSourceChip))]
    [NotifyPropertyChangedFor(nameof(DiscoveredAtText))]
    [NotifyPropertyChangedFor(nameof(BudgetIsMoney))]
    [NotifyPropertyChangedFor(nameof(OwnerInitials))]
    [NotifyPropertyChangedFor(nameof(OwnerSubLine))]
    [NotifyPropertyChangedFor(nameof(HasOwnerSub))]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    [NotifyPropertyChangedFor(nameof(ShowDescriptionToggle))]
    public partial ProjectDetails? Details { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// True while the page shows the Provisional shell built from the feed store's in-memory
    /// summary (v3 plan Step 3: a cold details open paints an item shell the same frame, the
    /// awaited <c>GetDetailsAsync</c> only refines it). Never a spinner gate: navigation has
    /// already happened and this page's items always exist — shell → Confirmed, or → Broken with
    /// the retry affordance, never a blocked/blank page.
    /// </summary>
    [ObservableProperty]
    public partial bool IsProvisional { get; set; }

    /// <summary>Broken details state: load failed while navigation already completed — show honest error + retry, keep the UI interactive.</summary>
    public bool IsBroken => HasError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBroken))]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether the description paragraph is fully expanded (v3 mockup: clamped to a couple of lines
    /// with a "عرض المزيد" toggle). Drives <see cref="DescriptionLineBreakMode"/>,
    /// <see cref="DescriptionMaxLines"/>, <see cref="DescriptionToggleText"/> and the toggle's own
    /// visibility via <see cref="ShowDescriptionToggle"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionLineBreakMode))]
    [NotifyPropertyChangedFor(nameof(DescriptionMaxLines))]
    [NotifyPropertyChangedFor(nameof(DescriptionToggleText))]
    [NotifyPropertyChangedFor(nameof(ShowDescriptionToggle))]
    public partial bool IsDescriptionExpanded { get; set; }

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
    // v3 details header / decision strip / owner card / description clamp
    // (project-details.html visual truth; all data-driven, zero source-id branches)
    // -----------------------------------------------------------------

    /// <summary>The active source's Arabic display name, for the header caption chip (from the descriptor, never a literal).</summary>
    public string SourceNameAr => SourceDescriptor?.DisplayNameAr ?? string.Empty;

    /// <summary>Header source chip is shown only when the descriptor carries a display name.</summary>
    public bool ShowSourceChip => !string.IsNullOrWhiteSpace(SourceNameAr);

    /// <summary>Discovery timestamp rendered for the header "· اكتُشف {date}" caption. Empty ⇒ the caption is omitted.</summary>
    public string DiscoveredAtText => Details is null
        ? string.Empty
        : Details.DiscoveredAt.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// True when the loaded project's budget is a Latin-currency figure (nafezly "$") that the decision
    /// strip must isolate in <c>FlowDirection=LeftToRight</c>; an Arabic currency stays in the RTL flow.
    /// Pure function of descriptor data (reuses <see cref="DetailSchemaRenderer.MoneyNeedsLtr"/>).
    /// </summary>
    public bool BudgetIsMoney => Details is not null && DetailSchemaRenderer.MoneyNeedsLtr(SourceDescriptor?.CurrencySuffix);

    /// <summary>
    /// The DECISION STRIP facts (الميزانية · مدة التسليم · العروض المقدمة · الحالة · نُشر), in the fixed
    /// approved order. Each entry is emitted ONLY when its underlying value is present, so a source that
    /// does not carry one of the fields simply shows fewer facts — never a placeholder, never a branch.
    /// </summary>
    public IReadOnlyList<DecisionFact> DecisionFacts { get; private set; } = [];

    /// <summary>Whether the decision strip has any fact to render (drives the strip + its divider visibility).</summary>
    public bool HasDecisionFacts => DecisionFacts.Count > 0;

    /// <summary>
    /// The owner card's inline stat cells — the descriptor's <c>owner</c>-section rows minus the name and
    /// registration-date fields (which the card renders as the identity line). Absent fields never appear,
    /// so nafezly (name-only owner schema) renders an owner card with no stat cells.
    /// </summary>
    public IReadOnlyList<DetailMetaRow> OwnerStats { get; private set; } = [];

    /// <summary>Whether the owner card has any inline stat cell to render.</summary>
    public bool HasOwnerStats => OwnerStats.Count > 0;

    /// <summary>Whether the owner card renders at all — true when the project carries any owner data.</summary>
    public bool ShowOwnerCard =>
        !string.IsNullOrWhiteSpace(Details?.Owner?.Name)
        || !string.IsNullOrWhiteSpace(Details?.Owner?.RegisteredAt)
        || OwnerStats.Count > 0;

    /// <summary>First letters of the owner's name for the brand-avatar (two-word names give two letters).</summary>
    public string OwnerInitials
    {
        get
        {
            var name = Details?.Owner?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{parts[0][0]}{parts[1][0]}";
            }

            return parts[0][0].ToString();
        }
    }

    /// <summary>Owner sub-line (member-since, as scraped). No country field exists, so none is invented.</summary>
    public string OwnerSubLine => Details?.Owner?.RegisteredAt ?? string.Empty;

    /// <summary>Whether the owner sub-line has data to show.</summary>
    public bool HasOwnerSub => !string.IsNullOrWhiteSpace(Details?.Owner?.RegisteredAt);

    /// <summary>Whether the project carries any description text (drives the الوصف section).</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Details?.Description);

    /// <summary>Line-break mode for the description: truncated when clamped, wrapping when expanded.</summary>
    public LineBreakMode DescriptionLineBreakMode => IsDescriptionExpanded ? LineBreakMode.WordWrap : LineBreakMode.TailTruncation;

    /// <summary>Max rendered description lines: a short clamp when collapsed, unlimited (0) when expanded.</summary>
    public int DescriptionMaxLines => IsDescriptionExpanded ? 0 : 2;

    /// <summary>The toggle only appears when there is a description long enough that clamping actually hides text.</summary>
    public bool ShowDescriptionToggle =>
        HasDescription && (IsDescriptionExpanded || LooksLongEnoughToClamp(Details!.Description));

    /// <summary>Toggle caption: "عرض المزيد" while clamped, "عرض أقل" while expanded.</summary>
    public string DescriptionToggleText => IsDescriptionExpanded ? "عرض أقل" : "عرض المزيد";

    /// <summary>
    /// Heuristic for whether the description exceeds the collapsed 2-line budget on a desktop width.
    /// Approximate by character run + explicit line breaks; a false positive only shows a harmless
    /// "عرض المزيد" that reveals the same short text.
    /// </summary>
    private static bool LooksLongEnoughToClamp(string text) =>
        text.Length > 220 || text.Split('\n').Count(l => l.Trim().Length > 0) >= 3;

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

    /// <summary>
    /// Footer "تحديد كمقروء والعودة": the read flip is OPTIMISTIC — the store overlay marks the
    /// feed row read now and the command returns immediately, so the back-navigation the layout
    /// performs right after this call never waits on the DB. The repository write (the same path
    /// the notification/feed read uses) is deferred through the last-wins queue; the store's read
    /// overlay keeps the row unflagged until the DB truth lands.
    /// </summary>
    [RelayCommand]
    public Task MarkAsReadAsync()
    {
        if (Details is not { } details)
        {
            return Task.CompletedTask;
        }

        var identity = new SourceProjectId(details.Source, details.ProjectId);
        _store.ApplyOptimisticRead(identity.Source, identity.ProjectId);
        DeferredPreferences.Enqueue($"read:{identity.Source.Value}:{identity.ProjectId}", async () =>
        {
            var result = await _projectRepository.MarkAsReadAsync(identity);
            if (!result.IsOk)
            {
                InteractionLogger.Fault("ProjectDetailsViewModel.MarkAsRead", new InvalidOperationException(result.Error.InternalMessage));
            }
        });

        return Task.CompletedTask;
    }

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
    /// The Windows details layout's generic schema block: every <see cref="MetaSections"/> group EXCEPT
    /// <c>project</c> (rendered specially as the DECISION STRIP) and <c>owner</c> (rendered specially as
    /// the owner CARD). In practice this is the nafezly-only <c>executor</c> group — kept fully
    /// schema/capability-driven per backend-ui-contract §7 — plus any future generic section. Mobile keeps
    /// rendering the full <see cref="MetaSections"/>; this filter is by section id (schema data), never a
    /// source id.
    /// </summary>
    public IReadOnlyList<DetailSectionGroup> SchemaSections { get; private set; } = [];

    /// <summary>Whether any generic schema section (nafezly's executor block, etc.) is present.</summary>
    public bool HasSchemaSections => SchemaSections.Count > 0;

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
        ActivePipeline activePipeline,
        SourceFeedStore store)
    {
        _projectRepository = projectRepository;
        _assetDownloadService = assetDownloadService;
        _globalStatus = globalStatus;
        _activePipeline = activePipeline;
        _store = store;
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

    /// <summary>
    /// Loads one project's details. Doctrine-compliant shape (v3 plan Step 3): the page item
    /// EXISTS before any query — the shell is built synchronously from the feed store's in-memory
    /// summary when that source's snapshot holds the row — then the (off-render-loop)
    /// <c>GetDetailsAsync</c> refines it to Confirmed, or lands the Broken state with retry while
    /// navigation is already done and the rest of the UI stays interactive.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync(SourceProjectId projectId)
    {
        _lastRequested = projectId;
        HasError = false;
        ErrorMessage = null;

        // Same-frame shell paint from memory (UI items always exist): if the feed store still
        // holds this project's summary, shape the page shell from it NOW, not after the DB.
        var shell = BuildShellFromStore(projectId);
        if (shell is not null)
        {
            Details = shell;
            IsProvisional = true;
            IsLoading = false;
            RebuildMetaSections();
            OnPropertyChanged(nameof(ShowDetails));
        }
        else
        {
            IsProvisional = false;
            IsLoading = true;
        }

        try
        {
            var result = await _projectRepository.GetDetailsAsync(projectId);

            // A newer navigation superseded this load (page reused): drop the stale batch — never
            // paint project A's details under project B's request.
            if (projectId != _lastRequested)
            {
                return;
            }

            if (!result.IsOk)
            {
                // Broken state WITH navigation already done — honest error + retry, not a spinner block.
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
            IsProvisional = false;

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

            // Fresh project ⇒ the description clamp starts collapsed (v3 details mockup: "عرض المزيد").
            ResetDescriptionState();
        }
        finally
        {
            if (projectId == _lastRequested)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(ShowDetails));
                OnPropertyChanged(nameof(HasSkills));
                OnPropertyChanged(nameof(HasAttachments));
            }
        }
    }

    /// <summary>
    /// The Broken details state's retry: re-issues the exact last load. The page never left the
    /// navigated state, so retry only re-runs the refine — the shell (if present) keeps painting.
    /// </summary>
    [RelayCommand]
    public Task RetryLoadAsync() => LoadAsync(_lastRequested);

    /// <summary>
    /// Builds the Provisional page shell from the feed store's memory (never a DB read): the
    /// summary-backed facts the decision strip/header can show this frame. Null when the store has
    /// no snapshot row for this id (true cold start) — the layout then shows its shimmer shell.
    /// </summary>
    private ProjectDetails? BuildShellFromStore(SourceProjectId projectId)
    {
        var item = _store.Peek(projectId.Source)?.Items
            .FirstOrDefault(i => i.ProjectId == projectId.ProjectId && i.Summary is not null);
        if (item?.Summary is not { } summary)
        {
            return null;
        }

        return new ProjectDetails
        {
            Source = summary.Source,
            ProjectId = summary.ProjectId,
            Title = summary.Title,
            Url = summary.Url,
            Description = summary.Description,
            Budget = summary.Budget,
            DeliveryDays = summary.DeliveryDays,
            ProjectStatus = summary.ProjectStatus,
            PublishTimeNumber = summary.PublishTimeNumber,
            PublishTimeText = summary.PublishTimeText,
            ProposalCount = summary.ProposalCount,
            ProposalCountText = summary.ProposalCountText,
            EnrichmentStatus = summary.EnrichmentStatus,
            DiscoveredAt = summary.DiscoveredAt,
            EnrichedAt = summary.EnrichedAt,
        };
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
            SchemaSections = [];
            DecisionFacts = [];
            OwnerStats = [];
            OnPropertyChanged(nameof(MetaSections));
            OnPropertyChanged(nameof(SchemaSections));
            OnPropertyChanged(nameof(HasSchemaSections));
            OnPropertyChanged(nameof(DecisionFacts));
            OnPropertyChanged(nameof(HasDecisionFacts));
            OnPropertyChanged(nameof(OwnerStats));
            OnPropertyChanged(nameof(HasOwnerStats));
            OnPropertyChanged(nameof(ShowOwnerCard));
            return;
        }

        MetaSections = DetailSchemaRenderer.Render(descriptor, details)
            .GroupBy(r => r.Section)
            .Select(g => new DetailSectionGroup(
                g.Key,
                DetailSchemaRenderer.SectionTitleAr(g.Key),
                g.OrderBy(r => r.Order).ToList()))
            .ToList();

        // Windows renders the project + owner sections specially (decision strip / owner card);
        // only the remaining generic groups (nafezly's executor block, any future section) go through
        // the generic schema block. Filtered by section id — schema data, never a source id.
        SchemaSections = MetaSections
            .Where(g => g.Section is not ("project" or "owner"))
            .ToList();

        // The owner card's inline stat cells = the owner-section rows minus the identity fields.
        OwnerStats = MetaSections
            .FirstOrDefault(g => g.Section == "owner")
            ?.Rows
            .Where(r => r.Key is not ("owner_name" or "registration_date"))
            .ToList() ?? [];

        RebuildDecisionStrip();

        OnPropertyChanged(nameof(MetaSections));
        OnPropertyChanged(nameof(SchemaSections));
        OnPropertyChanged(nameof(HasSchemaSections));
        OnPropertyChanged(nameof(OwnerStats));
        OnPropertyChanged(nameof(HasOwnerStats));
        OnPropertyChanged(nameof(ShowOwnerCard));
    }

    /// <summary>
    /// Builds the fixed approved DECISION STRIP fact set from the loaded project's own values, in the mockup's
    /// order (الميزانية · مدة التسليم · العروض المقدمة · الحالة · نُشر). A fact is emitted only when its value
    /// is present, so each source shows exactly the subset it carries with no per-source branch and no "—".
    /// </summary>
    private void RebuildDecisionStrip()
    {
        var facts = new List<DecisionFact>();
        if (Details is { } d)
        {
            if (!string.IsNullOrWhiteSpace(d.Budget))
            {
                facts.Add(new DecisionFact("budget", "الميزانية", Budget, BudgetIsMoney, IsAccent: true));
            }

            if (d.DeliveryDays is not null)
            {
                facts.Add(new DecisionFact("duration", "مدة التسليم", Duration, IsMoney: false, IsAccent: false));
            }

            if (d.ProposalCount > 0)
            {
                facts.Add(new DecisionFact("proposals", "العروض المقدمة", d.ProposalCount.ToString(System.Globalization.CultureInfo.InvariantCulture), IsMoney: false, IsAccent: false));
            }

            if (!string.IsNullOrWhiteSpace(d.ProjectStatus))
            {
                facts.Add(new DecisionFact("status", "الحالة", d.ProjectStatus, IsMoney: false, IsAccent: true));
            }

            if (d.DiscoveredAt != default)
            {
                facts.Add(new DecisionFact("published", "نُشر", PublishTimeText, IsMoney: false, IsAccent: false));
            }
        }

        DecisionFacts = facts;
        OnPropertyChanged(nameof(DecisionFacts));
        OnPropertyChanged(nameof(HasDecisionFacts));
    }

    /// <summary>Resets the transient UI state (description clamp) whenever a fresh project is loaded.</summary>
    private void ResetDescriptionState() => IsDescriptionExpanded = false;

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

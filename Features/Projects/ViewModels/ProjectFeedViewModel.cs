using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MostaqlK.Core;
using MostaqlK.Core.Navigation;
using MostaqlK.Core.Platform;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Models;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;
using MostaqlK.Services.Pipeline;
using MostaqlK.UI.DesignSystem;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>
/// View-model for the main project feed page (projects.html): holds the sidebar
/// navigation state and the scrollable collection of <see cref="ProjectCardViewModel"/>.
/// <para>
/// <b>v3 read-model renderer (frame decoupling, plan Step 1/3):</b> this type NEVER queries the
/// database on the render path anymore. It paints <see cref="SourceFeedStore.Peek"/> (O(1) memory)
/// and repaints exclusively from <see cref="SourceFeedStore.SnapshotUpdated"/> batches — the old
/// <c>Projects.Clear()</c> + awaited DB round-trips + the fire-and-forget
/// <c>GlobalAppStatusService.PropertyChanged → SwitchSourceAsync</c> double-trigger race are gone:
/// source switches are synchronous memory paints, and every DB round runs inside the store's
/// coalesced, generation-guarded background passes.
/// </para>
/// <para>
/// Row lifecycle states (<see cref="FeedRowState"/>) ride the cards so a failure shows an honest
/// broken placeholder with retry instead of a blank list or a frozen UI; the debounced
/// <see cref="SearchQuery"/> re-enters the store (the FTS page is bounded there), never a direct
/// <c>FtsQueryService</c> call.
/// </para>
/// </summary>
public sealed partial class ProjectFeedViewModel : ObservableObject
{
    private readonly IProjectRepository _projectRepository;
    private readonly SourceFeedStore _store;
    private readonly IPollService _pollService;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly GlobalAppStatusService _globalStatus;
    private readonly ISettingsRepository _settings;
    private SourceId _source = SourceId.Default;

    // Generation-checked batch apply guard (renderer side of the store's contract): a snapshot
    // older than what was already painted for this source is DROPPED, never applied.
    private SourceId _lastAppliedSource = SourceId.Default;
    private long _lastAppliedGeneration = -1;

    // The settle mount plays ONCE per source switch: armed by SwitchSource, consumed by the first
    // content-bearing batch applied (routine pipeline refine batches never re-trigger it).
    private bool _settleOnNextBatch;

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    /// <summary>
    /// Raised on the UI thread after a confirmed snapshot batch has been diff-applied to
    /// <see cref="Projects"/>. The Windows layout hooks this as the <c>ContentSettle</c> trigger:
    /// the mount pass re-times off the snapshot-applied paint-complete frame instead of off a
    /// data await (v3 plan Step 4).
    /// </summary>
    public event EventHandler? BatchApplied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchActive))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateSubtitle))]
    [NotifyPropertyChangedFor(nameof(FooterTrackedText))]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadCountText))]
    public partial int UnreadCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackedCountText))]
    [NotifyPropertyChangedFor(nameof(FooterTrackedText))]
    public partial int TrackedCount { get; set; }

    public GlobalAppStatusService GlobalStatus => _globalStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollIntervalText))]
    [NotifyPropertyChangedFor(nameof(LiveStatusText))]
    [NotifyPropertyChangedFor(nameof(PollToggleLabel))]
    [NotifyPropertyChangedFor(nameof(PollToggleColor))]
    [NotifyPropertyChangedFor(nameof(PollToggleIcon))]
    [NotifyPropertyChangedFor(nameof(PollPillLabel))]
    public partial bool IsPollingActive { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollIntervalText))]
    public partial int PollIntervalSeconds { get; set; } = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateLimitText))]
    public partial int RequestsPerMinute { get; set; } = 12;

    /// <summary>
    /// True when the ACTIVE source's descriptor exposes typed filter facets (B3) — the FilterModal
    /// affordances are present-by-data and hide for a source with an empty vocabulary (mostaql's
    /// preset-suffix flow keeps its Settings-only behavior; contract §8/§10 — never a source-id check).
    /// </summary>
    [ObservableProperty]
    public partial bool HasFilterFacets { get; set; }

    /// <summary>
    /// The active-query line's value (projects.html footer "الاستعلام النشط"): the source's stored
    /// <c>QueryParams</c> verbatim, or the "all projects" copy when nothing is set. The string is
    /// whatever the module's builder wrote — the UI reads it, never parses or concatenates it.
    /// </summary>
    [ObservableProperty]
    public partial string ActiveQueryText { get; set; } = "كل المشاريع";

    /// <summary>The active source's descriptor as the FilterModal's vocabulary/parser seam (null only in design-time hosts).</summary>
    public ISourceDescriptor? FilterDescriptor => SourceRegistryProvider.DescriptorFor(_source);

    /// <summary>The active source's stored raw query string, handed to the modal for prefill parsing.</summary>
    public string FilterStoredQuery => _settings.Get(_source, SettingsKeys.QueryParams, string.Empty);

    /// <summary>True once loading has finished with no error and at least one result — drives the success-state ScrollView.</summary>
    public bool ShowFeed => !IsLoading && !HasError && !IsEmpty;

    /// <summary>Header cadence caption, worded exactly like projects.html ("المسح كل 30 ثانية").</summary>
    public string PollIntervalText => $"المسح كل {PollIntervalSeconds} ثانية";

    /// <summary>Header rate caption; projects.html prints "12 طلب/دقيقة" (no spaces around the slash).</summary>
    public string RateLimitText => $"{RequestsPerMinute} طلب/دقيقة";

    public string LiveStatusText => IsPollingActive ? "مباشر" : "متوقف";

    public string PollToggleLabel => IsPollingActive ? "إيقاف الفحص" : "استئناف الفحص";

    public Color PollToggleColor => IsPollingActive
        ? DesignTokens.Colors.PollToggleActive
        : DesignTokens.Colors.PollToggleInactive;

    public AppIconGlyph PollToggleIcon => IsPollingActive ? AppIconGlyph.Pause : AppIconGlyph.Play;

    // -----------------------------------------------------------------
    // W3 feed-visual display glue (additive; reads the active descriptor / existing props only —
    // NEVER the database, never re-couples the store renderer to an await). The header caption,
    // search placeholder, footer count line and poll-pill label are all descriptor-driven per-source
    // figures that the requirements §4.1/§5 say must swap atomically on a source morph.
    // -----------------------------------------------------------------

    /// <summary>The active source's Arabic display name (مستقل / نفذلي), from the descriptor — never a literal.</summary>
    public string ActiveSourceName => SourceRegistryProvider.DescriptorFor(_source)?.DisplayNameAr ?? string.Empty;

    /// <summary>Search box placeholder, worded exactly like projects.html.</summary>
    public string SearchPlaceholder => $"بحث في مشاريع {ActiveSourceName} (العنوان، الوصف، المهارات، الميزانية...)";

    /// <summary>
    /// Footer inline-start tracked count (mockup "98 مشروعًا من مستقل"). Kept separate from
    /// <see cref="TrackedCountText"/> (the "…مشروع متتبَّع" variant older callers/tests use) so the
    /// v3 footer can carry the mockup's "{n} مشروعًا من {source}" wording without touching that text.
    /// </summary>
    public string FooterTrackedText => IsSearchActive
        ? $"{TrackedCount} نتيجة مطابقة"
        : $"{TrackedCount} مشروعًا من {ActiveSourceName}";

    /// <summary>Poll-pill caption (mockup footer "إيقاف الفحص" / "استئناف الفحص"), distinct from the compact <see cref="PollToggleLabel"/>.</summary>
    public string PollPillLabel => IsPollingActive ? "إيقاف الفحص" : "استئناف الفحص";

    public string TrackedCountText => IsSearchActive
        ? $"{TrackedCount} نتيجة مطابقة"
        : $"{TrackedCount} مشروع متتبَّع";

    public string UnreadCountText => $"{UnreadCount} غير مقروء";

    /// <summary>True while a search term is active — drives filtered footer counts and the empty-state copy.</summary>
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchQuery);

    public string EmptyStateTitle => IsSearchActive
        ? $"لا توجد نتائج لـ \"{SearchQuery.Trim()}\""
        : "لا توجد مشاريع حالياً";

    public string EmptyStateSubtitle => IsSearchActive
        ? "جرّب كلمات بحث مختلفة أو تحقّق من الإملاء."
        : "سيتم عرض المشاريع الجديدة هنا فور توفرها.";

    public ProjectFeedViewModel(
        IProjectRepository projectRepository,
        SourceFeedStore store,
        IPollService pollService,
        TokenBucketRateLimiter rateLimiter,
        GlobalAppStatusService globalStatus,
        ISettingsRepository settings)
    {
        _projectRepository = projectRepository;
        _store = store;
        _pollService = pollService;
        _rateLimiter = rateLimiter;
        _globalStatus = globalStatus;
        _settings = settings;

        // The VM is constructed after App's startup restore (or a parity `--source-id` launch)
        // has already re-pointed the projection, and events cannot reach a view-model that does
        // not exist yet — so start on the CURRENTLY projected source instead of always mostaql
        // (requirements §4.1: "every page opens in the remembered identity").
        _source = globalStatus.ActiveSource;

        RefreshHeaderStatus();

        // THE single feed-batch trigger (v3 plan Step 1): the store raises one marshalled event
        // per applied snapshot. The old design ALSO repainted from a fire-and-forget
        // projection-changed handler and from a debouncer that directly called LoadAsync — two
        // racing async chains over one list. Both are gone; pipeline events now only re-enqueue a
        // store refresh (coalesced there), and repaints arrive solely through SnapshotUpdated.
        _store.SnapshotUpdated += OnSnapshotUpdated;

        // §6 morph-first re-point (toast activation / restored projection): kept — but as a
        // SYNCHRONOUS idempotent call into SwitchSource, not the old async race path. The rail
        // tile's own SourceChanged event and this listener converge on the same guard
        // (`source == _source` returns instantly) and the store collapses the refresh burst.
        _globalStatus.PropertyChanged += OnActiveSourceRepointed;

        // Warm paint: seed the list from memory THIS frame when the store already holds a
        // snapshot for the startup source (the first-ever cold open keeps the shimmer path).
        if (_store.Peek(_source) is { } warm)
        {
            ApplySnapshot(warm);
        }
        else
        {
            IsLoading = true;
            OnPropertyChanged(nameof(ShowFeed));
            _ = _store.RefreshAsync(_source, EffectiveQuery);
        }

        // Auto-refresh: a discovered project lands in `projects` immediately and a completed
        // worker rewrites it with enriched details, so either event means the recent page has
        // changed. Bursts (a discovery storm, several workers finishing at once) are collapsed by
        // the store's coalescing window into ONE bounded query pass + ONE batched apply — the
        // view no longer owns a debouncer or touches the DB. MainWindowPage is created once for
        // the app's lifetime, so these subscriptions do not need tearing down.
        _globalStatus.ProjectDiscovered += OnProjectDiscovered;
        _globalStatus.WorkerStateChanged += OnWorkerStateChanged;
        _globalStatus.ProjectRemovedFromQueue += OnProjectRemovedFromQueue;
    }

    /// <summary>The source whose projects this feed currently lists.</summary>
    public SourceId CurrentSourceId => _source;

    // -----------------------------------------------------------------
    // Store plumbing
    // -----------------------------------------------------------------

    /// <summary>The store-scoped query for the current search state (null = the recent list).</summary>
    private string? EffectiveQuery => string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery.Trim();

    private void OnProjectDiscovered(long projectId, string title) => RequestStoreRefresh();

    private void OnProjectRemovedFromQueue(long projectId) => RequestStoreRefresh();

    private void OnWorkerStateChanged(int workerIndex, WorkerState state)
    {
        if (state is WorkerState.Completed or WorkerState.Error)
        {
            RequestStoreRefresh();
        }
    }

    /// <summary>Fire-and-forget re-enqueue of the current source's refresh; the store coalesces bursts and runs the queries off the UI path.</summary>
    private void RequestStoreRefresh() => _ = _store.RefreshAsync(_source, EffectiveQuery);

    /// <summary>
    /// Synchronous idempotent re-point kept ONLY as the morph-first safety net for switches that
    /// do not raise the rail's <c>SourceChanged</c> (toast activation). The removed race path was
    /// the fire-and-forget <c>async</c> variant of this handler — every property change of the
    /// projection fired a second concurrent <c>SwitchSourceAsync</c> with its own
    /// <c>Projects.Clear()</c> and DB chain. <see cref="SwitchSource"/> is sync + guarded, so the
    /// double signal now collapses to one memory paint + one coalesced store refresh.
    /// </summary>
    private void OnActiveSourceRepointed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var active = _globalStatus.ActiveSource;
        if ((e.PropertyName is null or "" or nameof(GlobalAppStatusService.ActiveSource)) && active != _source)
        {
            SwitchSource(active);
        }
    }

    /// <summary>
    /// Re-points the feed at a freshly-active source (the shell's source-morph hook and the
    /// projection listener share this). Synchronous by contract: paint the target's in-memory
    /// snapshot THIS frame (or keep the shimmer overlay when memory is cold), then let the store
    /// refine in the background. Never <c>Projects.Clear()</c> — the diff-apply swaps rows so the
    /// list is never blank between sources. No pipeline work is started or stopped — both engines
    /// already run unattended; this is pure projection (contract §2/§4.1).
    /// </summary>
    public void SwitchSource(SourceId source)
    {
        if (source == _source)
        {
            return;
        }

        _source = source;
        SearchQuery = string.Empty;
        _lastAppliedSource = source;
        _lastAppliedGeneration = -1;
        _settleOnNextBatch = true;
        RefreshHeaderStatus();

        if (_store.Peek(source) is { } warm)
        {
            ApplySnapshot(warm);
        }
        else
        {
            // Cold memory: the existing shimmer loading state covers the list (items-as-skeletons
            // per doctrine) — rows from the previous source are NOT cleared first, the overlay +
            // the imminent Provisional batch replace them.
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;
            OnPropertyChanged(nameof(ShowFeed));
        }

        _ = _store.RefreshAsync(source, null);
    }

    /// <summary>Async alias kept for the existing shell call-site shape; the paint is synchronous now.</summary>
    public Task SwitchSourceAsync(SourceId source)
    {
        SwitchSource(source);
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------
    // Snapshot application — the only feed mutation path
    // -----------------------------------------------------------------

    private void OnSnapshotUpdated(FeedSnapshot snapshot)
    {
        // Warm-peer prefetch batches for other sources must not paint the active list.
        if (snapshot.Source != _source)
        {
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(FeedSnapshot snapshot)
    {
        // Generation-checked batch apply: a stale-generation result is DROPPED, never applied
        // (renderer-side belt for the store's suspenders — the store also refuses to publish one).
        if (snapshot.Source == _lastAppliedSource && snapshot.Generation < _lastAppliedGeneration)
        {
            return;
        }

        _lastAppliedSource = snapshot.Source;
        _lastAppliedGeneration = snapshot.Generation;

        var confirmed = snapshot.Items
            .Where(static item => item.Summary is not null
                && item.State is FeedRowState.Confirmed or FeedRowState.Stale)
            .ToList();

        if (snapshot.IsProvisional)
        {
            // Cold shell → keep the shimmer loading state (skeleton rows ARE the items; nothing
            // is blocked on the query).
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;
            IsEmpty = false;
            OnPropertyChanged(nameof(ShowFeed));
            return;
        }

        IsLoading = false;

        if (snapshot.Error is not null)
        {
            if (confirmed.Count == 0)
            {
                // Broken page state WITH everything already painted: the honest error card + the
                // existing retry affordance (Projects_RetryButton → RefreshCommand → store
                // re-enqueue). The list never silently disappears and never spun for it.
                HasError = true;
                ErrorMessage = snapshot.Error;
                IsEmpty = false;
                OnPropertyChanged(nameof(ShowFeed));
                return;
            }

            // Prior rows still exist on screen (dimmed via the Stale batch the store published
            // before the failing pass); the failure is logged, not user-blocking.
            InteractionLogger.Fault("ProjectFeedViewModel.Snapshot", new InvalidOperationException(snapshot.Error));
        }

        HasError = false;
        ErrorMessage = null;

        // Diff-apply (reuse-by-id, then position) — the O(n) patch the feed already trusted, now
        // driven purely from a memory batch instead of a just-awaited query.
        var existingById = new Dictionary<long, ProjectCardViewModel>();
        foreach (var card in Projects)
        {
            existingById.TryAdd(card.Project.ProjectId, card);
        }

        var newIds = confirmed
            .Where(static item => item.ProjectId >= 0)
            .Select(static item => item.ProjectId)
            .ToHashSet();

        for (int i = Projects.Count - 1; i >= 0; i--)
        {
            if (!newIds.Contains(Projects[i].Project.ProjectId))
            {
                Projects.RemoveAt(i);
            }
        }

        for (int i = 0; i < confirmed.Count; i++)
        {
            var item = confirmed[i];
            var summary = item.Summary!;
            if (existingById.TryGetValue(summary.ProjectId, out var existing))
            {
                existing.UpdateFrom(item);
                var currentIndex = Projects.IndexOf(existing);
                if (currentIndex != i && currentIndex != -1)
                {
                    Projects.Move(currentIndex, i);
                }
                else if (currentIndex == -1)
                {
                    Projects.Insert(i, existing);
                }
            }
            else
            {
                Projects.Insert(i, ProjectCardViewModel.ForFeedItem(item, card => _ = SelectProjectAsync(card)));
            }
        }

        if (IsSearchActive)
        {
            // While a search filter is active, the footer reflects the filtered (visible) results
            // rather than the whole store's totals.
            TrackedCount = Projects.Count;
            UnreadCount = Projects.Count(static p => p.IsUnread);
        }
        else
        {
            // No filter: totals come from the snapshot's counts — the store already queried the
            // whole store on its own pass; the render path reads memory only.
            TrackedCount = snapshot.Counts.Tracked;
            UnreadCount = snapshot.Counts.Unread;
        }

        if (snapshot.Counts.Today >= 0)
        {
            _globalStatus.SetProjectsAddedToday(snapshot.Counts.Today);
        }

        IsEmpty = Projects.Count == 0;
        OnPropertyChanged(nameof(ShowFeed));

        // Settle hook (v3 plan Step 4): the mount pass re-times off the snapshot-applied
        // paint-complete frame — armed once per source switch only, so routine pipeline refine
        // batches never replay the animation (same trigger budget the old await-based path had).
        if (_settleOnNextBatch)
        {
            _settleOnNextBatch = false;
            BatchApplied?.Invoke(this, EventArgs.Empty);
        }
    }

    // -----------------------------------------------------------------
    // Commands
    // -----------------------------------------------------------------

    [TraceInteraction("RefreshCommand")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "RefreshCommand")]
    [RelayCommand]
    public async Task RefreshAsync()
    {
        using var _ = TraceScope.Begin("RefreshCommand");
        try
        {
            // Ask the pipeline for a scan now (the footer's "آخر فحص" reflects real scans, so the
            // button causes one rather than pretending), and re-enqueue the store refresh. The
            // repaint arrives via SnapshotUpdated, not by awaiting the DB here.
            _pollService.RequestCheckNow();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    /// <summary>
    /// The page's "load" entry (OnAppearing + LoadCommand): refresh-header reads, then re-enqueue
    /// the store. Returns when the coalesced batch loop goes quiet — the list itself painted from
    /// memory the same frame <see cref="SwitchSource"/> / the constructor ran.
    /// </summary>
    [RelayCommand]
    public Task LoadAsync()
    {
        RefreshHeaderStatus();
        return _store.RefreshAsync(_source, EffectiveQuery);
    }

    /// <summary>Invoked by <c>SearchInputField.DebouncedCommand</c> once the debounce window elapses.</summary>
    [RelayCommand]
    public Task SearchAsync(string? query)
    {
        SearchQuery = query ?? string.Empty;
        // The search page is the store's bounded FTS pass (hard LIMIT now — the raw MATCH set was
        // unbounded before this wave). No DB call from the renderer.
        return _store.RefreshAsync(_source, EffectiveQuery);
    }

    [RelayCommand]
    public Task ClearSearchAsync()
    {
        SearchQuery = string.Empty;
        return _store.RefreshAsync(_source, null);
    }

    public void RefreshHeaderStatus()
    {
        // Interval is a SYSTEM value, so the header shows the shared setting run through the same
        // per-site clamp the poll loop uses — otherwise the caption promises a cadence the pipeline
        // refuses to run at. The rate budget stays per platform, because each pipeline owns its limiter.
        var storedInterval = _settings.GetGlobal(SettingsKeys.PollIntervalSeconds, _pollService.PollIntervalSeconds);
        PollIntervalSeconds = SourceRegistryProvider.DescriptorFor(_source) is { } intervalDescriptor
            ? intervalDescriptor.ClampPollInterval(storedInterval)
            : storedInterval;
        RequestsPerMinute = _settings.Get(_source, SettingsKeys.MaxRequestsPerMinute, Math.Max(1, _rateLimiter.Capacity));
        IsPollingActive = !_pollService.IsPaused;

        // The filter vocabulary is descriptor data of the CURRENT source: re-read it on every
        // header refresh (load + source morph) so the modal affordance and the active-query line
        // always describe the source whose feed is on screen.
        HasFilterFacets = SourceRegistryProvider.DescriptorFor(_source)?.FilterFacets.Count > 0;
        var storedQuery = FilterStoredQuery;
        ActiveQueryText = string.IsNullOrWhiteSpace(storedQuery) ? "كل المشاريع" : storedQuery;

        // Per-source display figures (header caption / search placeholder / footer name) re-emit
        // here so a morph swaps them atomically alongside the cadence + rate reads above.
        OnPropertyChanged(nameof(ActiveSourceName));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(FooterTrackedText));
    }

    /// <summary>
    /// Persists a FilterModal Apply through the SAME single mutation path SettingsViewModel uses
    /// (<c>OnQueryParamsChanged</c>: write the namespaced setting, then hand the string to the
    /// poll service) — the query itself is serialized module-side by the descriptor, and the next
    /// poll cycle consumes it exactly like a Settings edit (no pipeline start/stop, no immediate
    /// scan, per the B3 conservative-default decision). The preference WRITE is deferred
    /// (last-wins queue): the apply click never waits on the settings-file round-trip.
    /// </summary>
    public void ApplyFilterSelection(IReadOnlyDictionary<string, IReadOnlyList<string>> selection)
    {
        if (SourceRegistryProvider.DescriptorFor(_source) is not { } descriptor)
        {
            return;
        }

        var built = descriptor.BuildQueryFromFacets(selection);
        DeferredPreferences.Set(_settings, _source, SettingsKeys.QueryParams, built);
        // Property push goes to the LIVE projected pipeline — transient resolution of
        // ActivePipeline == what MauiProgram registers IPollService as, so this is the same
        // mutation path, just re-pointed per call.
        (IPlatformApplication.Current?.Services?.GetService<ActivePipeline>()?.Poll ?? _pollService).QueryParams = built;
        RefreshHeaderStatus();
    }

    [TraceInteraction("TogglePolling")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "TogglePolling")]
    [RelayCommand]
    public void TogglePolling()
    {
        using var _ = TraceScope.Begin("TogglePolling", new { IsPollingActive });
        try
        {
            var nextPaused = IsPollingActive;
            _pollService.SetPaused(nextPaused);
            IsPollingActive = !_pollService.IsPaused;

            // Persist the state ("reserve the state it closed with") — deferred through the
            // last-wins queue so the toggle is a synchronous UI flip + a background write.
            DeferredPreferences.SetGlobal(_settings, SettingsKeys.IsPollingActive, IsPollingActive);

            OnPropertyChanged(nameof(PollIntervalText));
            OnPropertyChanged(nameof(LiveStatusText));
            OnPropertyChanged(nameof(PollToggleLabel));
            OnPropertyChanged(nameof(PollToggleColor));
            OnPropertyChanged(nameof(PollToggleIcon));
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    // NOTE: name stays WITHOUT the "Async" suffix — a synchronous void method named MarkAllReadAsync
    // makes the RelayCommand generator emit MarkAllReadAsyncCommand, and both layouts bind
    // MarkAllReadCommand (dev29's async→void rewrite silently broke the footer action).
    [RelayCommand]
    public void MarkAllRead()
    {
        foreach (var project in Projects)
        {
            // Overlay FIRST: card.MarkAsRead() mutates the very ProjectSummary instance the
            // snapshot's FeedItem references, so if the store is asked afterwards the row already
            // reads as seen — Counts.Unread never decrements and the next batch repaints the stale
            // footer count ("3 غير مقروء" after marking all read).
            _store.ApplyOptimisticRead(project.Project.Source, project.Project.ProjectId);
            project.MarkAsRead();
        }

        UnreadCount = 0;

        // Deferred DB truth through the last-wins queue — without the eventual write the next
        // refined CountTracked would resurrect every row still unread in the store (the historic
        // "badge jumps back to 7" bug the comment in the old async version documented).
        DeferredPreferences.Enqueue($"read:all:{_source.Value}", async () =>
        {
            var result = await _projectRepository.MarkAllAsReadAsync(_source);
            if (!result.IsOk)
            {
                InteractionLogger.Fault("ProjectFeedViewModel.MarkAllRead", new InvalidOperationException(result.Error.InternalMessage));
            }
        });
    }

    [TraceInteraction("SelectCommand")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "SelectCommand")]
    [RelayCommand]
    public async Task SelectProjectAsync(ProjectCardViewModel? card)
    {
        using var _ = TraceScope.Begin("SelectCommand", new { ProjectId = card?.Project.ProjectId });
        try
        {
            if (card is null)
            {
                return;
            }

            // Optimistic interaction (plan Step 3): the read flip is LOCAL first — the store's
            // next-snapshot overlay AND the card both go read now (the overlay must be applied
            // BEFORE the mutation: it shares the card's ProjectSummary instance, and after
            // MarkAsRead the store can no longer see that a decrement is due). The DB write is
            // deferred behind the navigate below, so tap-to-details latency never includes a
            // store round-trip.
            _store.ApplyOptimisticRead(card.Project.Source, card.Project.ProjectId);
            card.MarkAsRead();
            UnreadCount = Projects.Count(static p => p.IsUnread);

            var identity = new SourceProjectId(card.Project.Source, card.Project.ProjectId);
            DeferredPreferences.Enqueue($"read:{identity.Source.Value}:{identity.ProjectId}", async () =>
            {
                var result = await _projectRepository.MarkAsReadAsync(identity);
                if (!result.IsOk)
                {
                    InteractionLogger.Fault("ProjectFeedViewModel.SelectProjectAsync", new InvalidOperationException(result.Error.InternalMessage));
                }
            });

            await AppRoutes.NavigateAsync(AppRoutes.ProjectDetails(card.Project.Source, card.Project.ProjectId));
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }
}

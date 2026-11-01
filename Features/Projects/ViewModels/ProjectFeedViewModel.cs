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
    private readonly PlatformEnablement _enablement;
    private SourceId _source = SourceId.Default;

    // Generation-checked batch apply guard (renderer side of the store's contract): a snapshot
    // older than what was already painted for this source is DROPPED, never applied.
    private SourceId _lastAppliedSource = SourceId.Default;
    private long _lastAppliedGeneration = -1;

    // The settle mount plays ONCE per source switch: armed by SwitchSource, consumed by the first
    // content-bearing batch applied (routine pipeline refine batches never re-trigger it).
    private bool _settleOnNextBatch;

    /// <summary>
    /// True when the NEXT batch should adopt the store's ranking wholesale (cold open, source morph,
    /// explicit refresh, search). Background refine batches leave it false and keep the reader's
    /// order stable — see the ORDER POLICY note in <see cref="ApplySnapshot"/>.
    /// </summary>
    private bool _reorderPending = true;

    /// <summary>
    /// Existing rows keep the order the user is looking at; rows that left the page drop out; rows new
    /// to the page go on top in the store's own order. No already-listed row ever changes rank.
    /// </summary>
    private static List<FeedItem> KeepOrderWithNewOnTop(List<FeedItem> batch, List<long> previousIds)
    {
        var byId = new Dictionary<long, FeedItem>();
        foreach (var item in batch)
        {
            byId[item.ProjectId] = item;
        }

        var kept = new List<FeedItem>(batch.Count);
        var used = new HashSet<long>();
        foreach (var id in previousIds)
        {
            if (byId.TryGetValue(id, out var item) && used.Add(id))
            {
                kept.Add(item);
            }
        }

        var arrived = new List<FeedItem>();
        foreach (var item in batch)
        {
            if (!used.Contains(item.ProjectId))
            {
                arrived.Add(item);
                used.Add(item.ProjectId);
            }
        }

        arrived.AddRange(kept);
        return arrived;
    }

    /// <summary>How many rows changed rank between what was listed and what is about to be listed.</summary>
    private static int CountMoved(List<long> previousIds, List<FeedItem> next)
    {
        var previousIndex = new Dictionary<long, int>(previousIds.Count);
        for (int i = 0; i < previousIds.Count; i++)
        {
            previousIndex[previousIds[i]] = i;
        }

        int moved = 0;
        for (int i = 0; i < next.Count; i++)
        {
            if (previousIndex.TryGetValue(next[i].ProjectId, out var was) && was != i)
            {
                moved++;
            }
        }

        return moved;
    }

    /// <summary>A few "id:was→now" strings — the evidence that used to be missing from the trace.</summary>
    private static string[] SampleMoves(List<long> previousIds, List<FeedItem> next)
    {
        var previousIndex = new Dictionary<long, int>(previousIds.Count);
        for (int i = 0; i < previousIds.Count; i++)
        {
            previousIndex[previousIds[i]] = i;
        }

        var sample = new List<string>(6);
        for (int i = 0; i < next.Count && sample.Count < 6; i++)
        {
            if (previousIndex.TryGetValue(next[i].ProjectId, out var was) && was != i)
            {
                sample.Add($"{next[i].ProjectId}:{was}->{i}");
            }
        }

        return [.. sample];
    }

    /// <summary>
    /// The feed's rows. Settable (dev87) because the only repair that provably fixes the
    /// after-navigation blank feed is making the items control rebuild its item containers, and
    /// nulling the bound property in code loses the XAML binding and eventually the feed itself.
    /// Swapping in a NEW collection holding the SAME card instances rebuilds the containers cleanly
    /// and keeps every binding the page owns.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<ProjectCardViewModel> Projects { get; set; } = [];

    // FEED BUG HUNT (dev74): a per-instance id so the log answers "did back-navigation hand me a NEW
    // view-model, or the same one?" — the discriminator between a leaked-VM repaint and a real
    // data/render collapse. Temporary; remove with the hunt.
    private static int _vmSeq;
    private readonly int _vmId = Interlocked.Increment(ref _vmSeq);
    public int DiagVmId => _vmId;

    // Every feed diagnostic funnels through FeedTrace, which is on by default in Debug and
    // opt-in in Release - see Services/Diagnostics/FeedTrace.cs.
    private static void Diag(string checkpoint, string variant, object? data) =>
        FeedTrace.Diag(checkpoint, variant, data);

    /// <summary>
    /// Raised on the UI thread after a confirmed snapshot batch has been diff-applied to
    /// <see cref="Projects"/>. The Windows layout hooks this as the <c>ContentSettle</c> trigger:
    /// the mount pass re-times off the snapshot-applied paint-complete frame instead of off a
    /// data await (v3 plan Step 4).
    /// </summary>
    public event EventHandler? BatchApplied;

    /// <summary>
    /// Raised when a batch changed the list STRUCTURALLY (ids added/removed, or the order changed, so
    /// the patch took the single-reset path).
    /// <para>
    /// Why the host needs this: after a structural change the Windows items panel keeps the
    /// ScrollViewer's old offset but stops re-realizing the region that offset now points at — the
    /// owner's log read every realized container at <c>y=-1878 … -268</c> (correct pitch, all ABOVE
    /// the viewport) with <c>inViewport: 0</c>. On screen: one clipped card edge and a blank field,
    /// and a manual scroll "fixing" it because scrolling is the event that forces re-realization.
    /// The host answers this event by forcing that re-realization at the SAME offset: enrichment
    /// re-ranks this list continuously, so a scroll-to-top here would yank the reader away from the
    /// card they were looking at every few seconds.
    /// </para>
    /// </summary>
    public event EventHandler? StructuralPatchApplied;

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
        ? MostaqlK.UI.DesignSystem.Tokens.ThemeTokens.PollActive
        : MostaqlK.UI.DesignSystem.Tokens.ThemeTokens.PollInactive;

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

    /// <summary>
    /// True while this feed's platform is switched off in Settings. The feed itself stays usable —
    /// stored projects, filters, details and navigation are untouched — so this is the one place that
    /// says why nothing new is arriving. It shows in the scan-status line and, when there is nothing
    /// stored to show, in the empty state.
    /// </summary>
    public bool IsPlatformDisabled { get; private set; }

    /// <summary>Empty-state headline for a switched-off platform with nothing stored.</summary>
    public string EmptyStateTitle => IsPlatformDisabled
        ? "لا يوجد فحص لأن هذه المنصة معطّلة"
        : IsSearchActive
            ? $"لا توجد نتائج لـ \"{SearchQuery.Trim()}\""
            : "لا توجد مشاريع حالياً";

    public string EmptyStateSubtitle => IsPlatformDisabled
        ? "فعّلها من الإعدادات لإعادة تشغيل الفحص — المشاريع المحفوظة تبقى متاحة هنا."
        : IsSearchActive
            ? "جرّب كلمات بحث مختلفة أو تحقّق من الإملاء."
            : "سيتم عرض المشاريع الجديدة هنا فور توفرها.";

    public ProjectFeedViewModel(
        IProjectRepository projectRepository,
        SourceFeedStore store,
        IPollService pollService,
        TokenBucketRateLimiter rateLimiter,
        GlobalAppStatusService globalStatus,
        ISettingsRepository settings,
        PlatformEnablement enablement)
    {
        _projectRepository = projectRepository;
        _store = store;
        _pollService = pollService;
        _rateLimiter = rateLimiter;
        _globalStatus = globalStatus;
        _settings = settings;
        _enablement = enablement;

        // The VM is constructed after App's startup restore (or a parity `--source-id` launch)
        // has already re-pointed the projection, and events cannot reach a view-model that does
        // not exist yet — so start on the CURRENTLY projected source instead of always mostaql
        // (requirements §4.1: "every page opens in the remembered identity").
        _source = globalStatus.ActiveSource;

        // FEED BUG HUNT (dev74): logged BEFORE the warm paint so the log reads ctor → first apply.
        Diag("FeedDiag.VM_Ctor", "A", new
        {
            vm = _vmId,
            source = _source.Value,
            warmItems = _store.Peek(_source)?.Items.Count ?? -1,
            warmGen = _store.Peek(_source)?.Generation ?? -1,
            warmQuery = _store.Peek(_source)?.Query,
        });

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

    /// <summary>
    /// dev87: rebuild the items control's container set by handing it a fresh collection of the same
    /// cards. Called when the feed page comes back (the repro: open a project, press back, and the
    /// list shows one card over a blank field while this view-model correctly holds every row).
    /// Card instances are preserved, so no state is lost and no animation replays.
    /// </summary>
    public void RebuildItemsContainer()
    {
        var same = new ObservableCollection<ProjectCardViewModel>(Projects);
        Projects = same;
        FeedTrace.Log("Mapping", "swap-collection", new { vm = _vmId, rows = same.Count });
    }

    /// <summary>The store-scoped query for the current search state (null = the recent list).</summary>
    private string? EffectiveQuery => string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery.Trim();

    private void OnProjectDiscovered(long projectId, string title) => RequestStoreRefresh("discovered");

    private void OnProjectRemovedFromQueue(long projectId) => RequestStoreRefresh("removedFromQueue");

    private void OnWorkerStateChanged(int workerIndex, WorkerState state)
    {
        if (state is WorkerState.Completed or WorkerState.Error)
        {
            RequestStoreRefresh($"worker:{state}");
        }
    }

    /// <summary>Fire-and-forget re-enqueue of the current source's refresh; the store coalesces bursts and runs the queries off the UI path.</summary>
    private void RequestStoreRefresh(string reason)
    {
        Diag("FeedDiag.ReEnqueue", reason, new { vm = _vmId, source = _source.Value, query = EffectiveQuery });
        _ = _store.RefreshAsync(_source, EffectiveQuery);
    }

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
            Diag("FeedDiag.Repoint", e.PropertyName ?? "all", new { vm = _vmId, from = _source.Value, to = active.Value });
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
        _reorderPending = true; // a morph is a fresh page: adopt the new source's ranking
        Diag("FeedDiag.SwitchSource", "A", new
        {
            vm = _vmId,
            to = source.Value,
            rowsBefore = Projects.Count,
            warmItems = _store.Peek(source)?.Items.Count ?? -1,
            warmGen = _store.Peek(source)?.Generation ?? -1,
        });
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
            Diag("FeedDiag.BatchSkipped", "B", new
            {
                vm = _vmId,
                batchSource = snapshot.Source.Value,
                mySource = _source.Value,
                gen = snapshot.Generation,
            });
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
            Diag("FeedDiag.ApplyDropped", "A", new
            {
                vm = _vmId,
                source = snapshot.Source.Value,
                gen = snapshot.Generation,
                lastAppliedGen = _lastAppliedGeneration,
                items = snapshot.Items.Count,
            });
            return;
        }

        _lastAppliedSource = snapshot.Source;
        _lastAppliedGeneration = snapshot.Generation;

        var confirmed = snapshot.Items
            .Where(static item => item.Summary is not null
                && item.State is FeedRowState.Confirmed or FeedRowState.Stale)
            .ToList();

        // FEED BUG HUNT (dev74): the incoming batch as the renderer sees it. `rowsBefore` is the live
        // list at this instant, `paintable` is what the diff-apply below will keep — a collapse shows
        // up here as a small `paintable` against a large `rowsBefore`.
        Diag("FeedDiag.Apply", snapshot.IsProvisional ? "prov" : (snapshot.Error is null ? "ok" : "err"), new
        {
            vm = _vmId,
            source = snapshot.Source.Value,
            gen = snapshot.Generation,
            query = snapshot.Query,
            search = SearchQuery,
            items = snapshot.Items.Count,
            paintable = confirmed.Count,
            stale = confirmed.Count(static i => i.State == FeedRowState.Stale),
            skipped = snapshot.Items.Count - confirmed.Count,
            rowsBefore = Projects.Count,
            ids = confirmed.Select(static i => i.ProjectId).Take(4).ToArray(),
        });

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

        // Patch strategy (dev75 — the vanishing-cards / WinUI fail-fast class).
        //
        // The old path reconciled every publish with `RemoveAt` + `Insert` + one `Move` PER ROW that
        // changed rank. That is hostile to the Windows items host: a re-sort of 40 rows emits ~40
        // reorder notifications inside one synchronous block, and WinUI's virtualizing panel can end up
        // holding an item map that no longer matches its containers — measured on the owner's machine as
        // the list's own content extent collapsing (13614 → 10868 → 13506 px in 1.5 s) while the
        // realized-container count swung 2 ↔ 12, and finally as `E_INVALIDARG` (0x80070057) out of
        // Microsoft.UI.Xaml.dll escalating to the `0xC000027B` fail-fast in five crash dumps. Cards
        // vanishing (one left, then none) and the scroll-then-crash were the same fault, two intensities.
        //
        // Two rules now, both invisible to the user when nothing changed:
        //   1. SAME ids in the SAME order → update the cards in place and raise ZERO structural
        //      notifications. This is what the store's dim (stale-while-revalidate) and card-tap
        //      overlay publishes actually are: the same generation re-published, content at most
        //      nudged — the old code still ran a full structural patch for each of them.
        //   2. Otherwise → ONE `Clear` + in-order `Add`s (a single Reset, which MAUI's items update
        //      coordinator on Windows coalesces) reusing the existing card instances by id, instead of
        //      a storm of Moves. Identity survives, so no state is lost and no animation replays.
        var existingById = new Dictionary<long, ProjectCardViewModel>();
        var prevIds = new List<long>(Projects.Count);
        foreach (var card in Projects)
        {
            existingById.TryAdd(card.Project.ProjectId, card);
            prevIds.Add(card.Project.ProjectId);
        }

        // ORDER POLICY (dev80 — the defect the owner named): the recent page is sorted by enrichment
        // completion (`(enriched_at IS NULL), enriched_at DESC, discovered_at DESC` in
        // ProjectRepository.GetRecentAsync), so EVERY finished worker re-ranks the whole page. A
        // background batch therefore used to move rows by thousands of pixels underneath a reader who
        // was scrolled down — which is both of their observations: a viewport full of holes, and the
        // first card changing identity (Nasser Elsayd → Mohamed Ahmed) with no action of theirs.
        //
        // So a background batch no longer re-ranks rows the user can already see: existing rows keep
        // their relative order, vanished rows drop out, new arrivals land on top (what a "recent"
        // feed promises). A full re-sort happens only when the user asked for the page — cold open,
        // source morph, refresh, search.
        bool reorder = _reorderPending || prevIds.Count == 0;
        _reorderPending = false;
        var ordered = reorder ? confirmed : KeepOrderWithNewOnTop(confirmed, prevIds);

        FeedTrace.Log("Patch", "plan", new
        {
            vm = _vmId,
            gen = snapshot.Generation,
            src = snapshot.Source.Value,
            reorder,
            before = prevIds.Count,
            after = ordered.Count,
            topBefore = prevIds.Count > 0 ? prevIds[0] : -1,
            topAfter = ordered.Count > 0 ? ordered[0].ProjectId : -1,
            moved = CountMoved(prevIds, ordered),
            added = ordered.Count(i => !prevIds.Contains(i.ProjectId)),
            removed = prevIds.Count(id => ordered.All(i => i.ProjectId != id)),
            moves = SampleMoves(prevIds, ordered),
        });

        bool sameShape = prevIds.Count == ordered.Count;
        if (sameShape)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                if (prevIds[i] != ordered[i].ProjectId)
                {
                    sameShape = false;
                    break;
                }
            }
        }

        if (sameShape)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                Projects[i].UpdateFrom(ordered[i]);
            }

            Diag("FeedDiag.Patch", "content-only", new { vm = _vmId, gen = snapshot.Generation, rows = Projects.Count });
        }
        else
        {
            // Structural change: one reset, no Move notifications ever.
            Projects.Clear();
            for (int i = 0; i < ordered.Count; i++)
            {
                var item = ordered[i];
                if (existingById.TryGetValue(item.ProjectId, out var reused))
                {
                    reused.UpdateFrom(item);
                    Projects.Add(reused);
                }
                else
                {
                    Projects.Add(ProjectCardViewModel.ForFeedItem(item, card => _ = SelectProjectAsync(card)));
                }
            }

            int reusedCount = 0;
            foreach (var item in ordered)
            {
                if (existingById.ContainsKey(item.ProjectId))
                {
                    reusedCount++;
                }
            }

            Diag("FeedDiag.Patch", "reset", new
            {
                vm = _vmId,
                gen = snapshot.Generation,
                rows = Projects.Count,
                reused = reusedCount,
                fresh = ordered.Count - reusedCount,
            });

            FeedTrace.Log("Patch", "reset-applied", new
            {
                vm = _vmId,
                gen = snapshot.Generation,
                rows = Projects.Count,
                reused = reusedCount,
                fresh = ordered.Count - reusedCount,
            });

            StructuralPatchApplied?.Invoke(this, EventArgs.Empty);
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

        // FEED BUG HUNT (dev74): the list AS THE DATA SEES IT after the patch. If this reports the
        // full row count while the screen shows one card, the collapse is below the view-model (the
        // WinUI list), not in the feed data.
        Diag("FeedDiag.ApplyEnd", "A", new
        {
            vm = _vmId,
            source = _source.Value,
            gen = snapshot.Generation,
            rowsAfter = Projects.Count,
            tracked = TrackedCount,
            unread = UnreadCount,
            isEmpty = IsEmpty,
            showFeed = ShowFeed,
            ids = Projects.Take(4).Select(static c => c.Project.ProjectId).ToArray(),
        });

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
            _reorderPending = true; // the reader asked for the page again: re-rank
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
        Diag("FeedDiag.Load", "A", new
        {
            vm = _vmId,
            source = _source.Value,
            query = EffectiveQuery,
            rows = Projects.Count,
            navStack = Shell.Current?.Navigation.NavigationStack.Count ?? -1,
        });
        RefreshHeaderStatus();
        return _store.RefreshAsync(_source, EffectiveQuery);
    }

    /// <summary>Invoked by <c>SearchInputField.DebouncedCommand</c> once the debounce window elapses.</summary>
    [RelayCommand]
    public Task SearchAsync(string? query)
    {
        SearchQuery = query ?? string.Empty;
        _reorderPending = true; // a query result page is ranked by relevance
        Diag("FeedDiag.Search", "A", new { vm = _vmId, source = _source.Value, query, rows = Projects.Count });
        // The search page is the store's bounded FTS pass (hard LIMIT now — the raw MATCH set was
        // unbounded before this wave). No DB call from the renderer.
        return _store.RefreshAsync(_source, EffectiveQuery);
    }

    [RelayCommand]
    public Task ClearSearchAsync()
    {
        SearchQuery = string.Empty;
        _reorderPending = true;
        Diag("FeedDiag.ClearSearch", "A", new { vm = _vmId, source = _source.Value, rows = Projects.Count });
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

        var platformDisabled = !_enablement.IsEnabled(_source);
        if (platformDisabled != IsPlatformDisabled)
        {
            IsPlatformDisabled = platformDisabled;
            OnPropertyChanged(nameof(IsPlatformDisabled));
            OnPropertyChanged(nameof(EmptyStateTitle));
            OnPropertyChanged(nameof(EmptyStateSubtitle));
        }

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
            Diag("FeedDiag.OpenProject", "A", new
            {
                vm = _vmId,
                source = identity.Source.Value,
                projectId = identity.ProjectId,
                mySource = _source.Value,
                rows = Projects.Count,
                route = AppRoutes.ProjectDetails(identity.Source, identity.ProjectId).Path,
            });
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

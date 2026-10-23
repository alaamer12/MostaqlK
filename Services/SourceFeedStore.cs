using System.Collections.Concurrent;
using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Models;

namespace MostaqlK.Services;

/// <summary>
/// The lifecycle state of one feed row. The owner doctrine: <b>a UI item ALWAYS exists</b> —
/// it changes state, it is never blocked on a query and never missing.
/// <list type="bullet">
/// <item><description><see cref="Provisional"/> — skeleton/optimistic row painted from memory the same frame the source is opened.</description></item>
/// <item><description><see cref="Confirmed"/> — row backed by a real <see cref="ProjectSummary"/> from the store.</description></item>
/// <item><description><see cref="Stale"/> — confirmed data shown dimmed while a refine pass is in flight (stale-while-revalidate).</description></item>
/// <item><description><see cref="Broken"/> — the query failed: an honest placeholder carrying its message; retry re-enqueues the source refresh.</description></item>
/// </list>
/// </summary>
public enum FeedRowState
{
    Provisional,
    Confirmed,
    Stale,
    Broken,
}

/// <summary>
/// How usable a peeked snapshot is <i>right now</i>: <see cref="Cold"/> = provisional-only
/// (nothing confirmed has ever landed for this query), <see cref="Fresh"/> = recently stamped,
/// <see cref="Stale"/> = still shown, but past the freshness horizon and awaiting refine.
/// </summary>
public enum SnapshotFreshness
{
    Cold,
    Fresh,
    Stale,
}

/// <summary>Whole-store totals carried alongside a snapshot so the footer never queries the DB either.</summary>
public sealed record CountsSnapshot(int Tracked, int Unread, int Today)
{
    public static CountsSnapshot Zero { get; } = new(0, 0, 0);
}

/// <summary>
/// One feed row in the read model. <see cref="Summary"/> is present for the
/// <see cref="FeedRowState.Confirmed"/>/<see cref="FeedRowState.Stale"/> states; the others are
/// honest shells (never a hole in the list).
/// </summary>
public sealed record FeedItem
{
    /// <summary>Placeholder id of a Broken row — a real project id is never negative.</summary>
    public const long BrokenSentinelId = -1;

    public required SourceId Source { get; init; }
    public required long ProjectId { get; init; }
    public required FeedRowState State { get; init; }

    /// <summary>The row's data once confirmed (dimmed while <see cref="FeedRowState.Stale"/>).</summary>
    public ProjectSummary? Summary { get; init; }

    /// <summary>Honest failure copy shown by a <see cref="FeedRowState.Broken"/> placeholder.</summary>
    public string? ErrorMessage { get; init; }

    public bool IsUnread => Summary?.IsUnread ?? false;

    /// <summary>Skeleton rows get distinct negative ids so a diff-apply never collides them with real projects.</summary>
    public static FeedItem Provisional(SourceId source, int skeletonIndex) =>
        new() { Source = source, ProjectId = -100 - skeletonIndex, State = FeedRowState.Provisional };

    public static FeedItem ConfirmedProject(ProjectSummary summary) =>
        new() { Source = summary.Source, ProjectId = summary.ProjectId, State = FeedRowState.Confirmed, Summary = summary };

    public static FeedItem StaleProject(ProjectSummary summary) =>
        new() { Source = summary.Source, ProjectId = summary.ProjectId, State = FeedRowState.Stale, Summary = summary };

    public static FeedItem Broken(SourceId source, string message) =>
        new() { Source = source, ProjectId = BrokenSentinelId, State = FeedRowState.Broken, ErrorMessage = message };
}

/// <summary>
/// An IMMUTABLE per-source feed state: the only thing the UI reads. Writers (the store's ingest
/// passes) build the next snapshot completely and then swap a single pointer — readers never
/// lock, never await, never touch the database. <see cref="Generation"/> is the per-source
/// monotonic batch counter: an apply whose generation is older than the current one is dropped.
/// </summary>
public sealed record FeedSnapshot
{
    public required SourceId Source { get; init; }
    public required IReadOnlyList<FeedItem> Items { get; init; }
    public required CountsSnapshot Counts { get; init; }

    /// <summary>Monotonic per-source batch counter; 0 = provisional shell, ≥1 = a real apply.</summary>
    public required long Generation { get; init; }

    /// <summary>UTC stamp of the apply that produced this snapshot (the plan's "Instant StampedAt" — .NET has no Instant; DateTimeOffset UTC is the canonical stand-in).</summary>
    public required DateTimeOffset StampedAt { get; init; }

    public required SnapshotFreshness Freshness { get; init; }

    /// <summary>The active search term this snapshot answers (null = the recent list).</summary>
    public string? Query { get; init; }

    /// <summary>Pass-level failure copy (query failed ⇒ the snapshot is a Broken page with retry, never a blank list).</summary>
    public string? Error { get; init; }

    public bool IsProvisional => Generation == 0 && Error is null;

    /// <summary>Peek-time freshness reclassification — pure arithmetic on the stamp, never a query.</summary>
    public FeedSnapshot ReclassifiedAt(DateTimeOffset now, TimeSpan staleAfter)
    {
        if (Freshness == SnapshotFreshness.Cold)
        {
            return this;
        }

        var want = now - StampedAt > staleAfter ? SnapshotFreshness.Stale : SnapshotFreshness.Fresh;
        return want == Freshness ? this : this with { Freshness = want };
    }
}

/// <summary>
/// The bounded data seam the store runs OFF the click path. The app wires this to the EXISTING
/// repository queries (the old <c>ProjectFeedViewModel.LoadAsync</c> shape: recent page, FTS
/// search page, tracked/unread totals, added-today count); tests wire a fake. Every method takes
/// the pass cancellation token so a superseded batch stops its queries. The list methods carry a
/// hard <see cref="SourceFeedStore.RecentPageLimit"/>/<see cref="SourceFeedStore.SearchPageLimit"/>
/// — the LIMIT audit of the v3 plan, step 1.
/// </summary>
public interface ISourceFeedDataProvider
{
    public Task<Result<IReadOnlyList<ProjectSummary>>> GetRecentAsync(SourceId source, int limit, CancellationToken cancellationToken);

    public Task<Result<IReadOnlyList<ProjectSummary>>> SearchAsync(SourceId source, string query, int limit, CancellationToken cancellationToken);

    public Task<Result<(int Tracked, int Unread)>> CountTrackedAsync(SourceId source, CancellationToken cancellationToken);

    public Task<Result<int>> CountAddedTodayAsync(SourceId source, CancellationToken cancellationToken);
}

/// <summary>
/// Application-side <see cref="ISourceFeedDataProvider"/> over the real repositories. The FTS
/// implementation arrives as a delegate (not the concrete <c>FtsQueryService</c>) so the store and
/// this adapter stay free of the Dapper/Sqlite stack and remain linkable into the headless
/// ParserTests harness.
/// </summary>
public sealed class RepositorySourceFeedDataProvider : ISourceFeedDataProvider
{
    private readonly IProjectRepository _projects;
    private readonly Func<SourceId, string, int, CancellationToken, Task<Result<IReadOnlyList<ProjectSummary>>>> _search;

    public RepositorySourceFeedDataProvider(
        IProjectRepository projects,
        Func<SourceId, string, int, CancellationToken, Task<Result<IReadOnlyList<ProjectSummary>>>> search)
    {
        _projects = projects;
        _search = search;
    }

    public Task<Result<IReadOnlyList<ProjectSummary>>> GetRecentAsync(SourceId source, int limit, CancellationToken cancellationToken) =>
        _projects.GetRecentAsync(source, limit, cancellationToken);

    public Task<Result<IReadOnlyList<ProjectSummary>>> SearchAsync(SourceId source, string query, int limit, CancellationToken cancellationToken) =>
        _search(source, query, limit, cancellationToken);

    public Task<Result<(int Tracked, int Unread)>> CountTrackedAsync(SourceId source, CancellationToken cancellationToken) =>
        _projects.CountTrackedAsync(source, cancellationToken);

    public Task<Result<int>> CountAddedTodayAsync(SourceId source, CancellationToken cancellationToken) =>
        _projects.CountAddedTodayAsync(source, cancellationToken);
}

/// <summary>
/// The frame-decoupled read model (v3 plan Step 1): <b>the render loop never queries the DB, it
/// only reads memory; the DB feeds memory.</b> Local SQLite is treated as just another slow async
/// source with tail latency — a 0.5 ms query that occasionally takes 80 ms must still never drop
/// a frame. Consumers call <see cref="Peek"/> (O(1), synchronous, never awaits) to paint and
/// subscribe to <see cref="SnapshotUpdated"/> (raised on the dispatcher) for refine batches; the
/// store itself runs the existing bounded repository queries off the click path, coalesces bursts
/// through a <see cref="CoalesceWindow"/> quiet period into ONE batched apply, build-next-then-
/// pointer-swaps the per-source snapshot (no reader locks), and cancels superseded generations so
/// a stale query can never paint over a newer one.
/// <para>
/// Rows exist in every state (<see cref="FeedRowState"/>): a cold source paints Provisional
/// skeletons the instant it is requested, a failed query produces a Broken snapshot with retry
/// copy — never a blank list and never a blocked UI. After an interactive apply lands, the other
/// registered sources are prefetched in the background at lowest priority (warm-peer), so the
/// next morph is memory-hot; those prefetches are cancelled on supersession.
/// </para>
/// <para>
/// <b>Concurrency model:</b> per-source state sits behind its own <c>Gate</c> lock (writers only;
/// the lock is never taken by <see cref="Peek"/>); snapshots are immutable records swapped through
/// a <see langword="volatile"/> field reference, so a reader sees either the whole old snapshot or
/// the whole new one — the double-buffered read model, no lock on the read side.
/// </para>
/// </summary>
public sealed class SourceFeedStore
{
    /// <summary>Hard bound on the recent page the store asks the provider for (same 100 the feed always used).</summary>
    public const int RecentPageLimit = 100;

    /// <summary>Hard bound on the FTS page (the raw <c>MATCH</c> result set was unbounded before this wave).</summary>
    public const int SearchPageLimit = 50;

    /// <summary>Burst window: refresh requests arriving within this quiet period collapse into ONE query pass / apply.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(60);

    /// <summary>A snapshot older than this reads as <see cref="SnapshotFreshness.Stale"/> from <see cref="Peek"/> (dimmed, awaiting refine) instead of <see cref="SnapshotFreshness.Fresh"/>.</summary>
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromSeconds(90);

    /// <summary>Warm-peer prefetch skips a peer whose snapshot is younger than this (memory is already hot enough).</summary>
    public static readonly TimeSpan PeerPrefetchTtl = TimeSpan.FromSeconds(30);

    /// <summary>Number of Provisional skeleton rows a cold shell paints (roughly a viewport of cards).</summary>
    public const int ProvisionalSkeletonRows = 5;

    private readonly ISourceFeedDataProvider _data;
    private readonly IReadOnlyList<SourceId> _sources;
    private readonly Action<Action> _marshal;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _staleAfter;
    private readonly Action<string, Exception?>? _faultSink;
    private readonly ConcurrentDictionary<SourceId, SourceState> _states = new();
    private readonly object _prefetchGate = new();
    private CancellationTokenSource? _prefetchCts;

    /// <summary>
    /// Raised once per applied snapshot batch, marshalled onto the UI dispatcher. THE single feed
    /// trigger for renderers (kills the old double-trigger: nothing else repaints the list).
    /// </summary>
    public event Action<FeedSnapshot>? SnapshotUpdated;

    public SourceFeedStore(
        ISourceFeedDataProvider data,
        IReadOnlyList<SourceId> sources,
        Action<Action>? marshal = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? staleAfter = null,
        Action<string, Exception?>? faultSink = null)
    {
        _data = data;
        _sources = sources;
        // Default marshal is identity (headless/test hosts); the app passes the dispatcher post.
        _marshal = marshal ??= action => action();
        _clock = clock ??= () => DateTimeOffset.UtcNow;
        _staleAfter = staleAfter ?? DefaultStaleAfter;
        _faultSink = faultSink;
    }

    /// <summary>The registered source ids (for hosts that must not resolve <c>SourceRegistry</c> themselves).</summary>
    public IReadOnlyList<SourceId> Sources => _sources;

    // -----------------------------------------------------------------
    // Reader surface — O(1) memory, NEVER awaits, NEVER queries
    // -----------------------------------------------------------------

    /// <summary>
    /// The snapshot the UI paints from: a volatile pointer read of the per-source immutable
    /// snapshot plus a pure stamp comparison for freshness. Never blocks: cold memory returns the
    /// Provisional shell (or null before the first request — the renderer then paints its own
    /// skeleton, it never blanks or spins).
    /// </summary>
    public FeedSnapshot? Peek(SourceId source)
    {
        if (!_states.TryGetValue(source, out var state))
        {
            return null;
        }

        var snapshot = state.Snapshot;
        return snapshot?.ReclassifiedAt(_clock(), _staleAfter);
    }

    /// <summary>The generation currently authoritative for a source (0 before any pass applied).</summary>
    public long CurrentGeneration(SourceId source) =>
        _states.TryGetValue(source, out var state) ? Volatile.Read(ref state.Generation) : 0;

    // -----------------------------------------------------------------
    // Writer surface — off the click path, coalesced, generation-guarded
    // -----------------------------------------------------------------

    /// <summary>
    /// Requests a refresh of one source (optionally scoped to a search term). Bursts within the
    /// <see cref="CoalesceWindow"/> quiet period collapse into ONE query pass and ONE apply. A
    /// user-intent call supersedes any in-flight pass of the source (its results are then dropped
    /// even if the provider ignores cancellation) and cancels the outstanding warm-peer prefetch.
    /// Returns the task that finishes when the batch loop goes quiet again.
    /// </summary>
    public Task RefreshAsync(SourceId source, string? query = null, CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(source, query, cancellationToken, background: false);

    /// <summary>Internal form: <paramref name="background"/> marks warm-peer prefetch (never cancels the interactive chain, never chains another prefetch).</summary>
    private Task RefreshCoreAsync(SourceId source, string? query, CancellationToken cancellationToken, bool background)
    {
        var state = StateFor(source);
        lock (state.Gate)
        {
            state.RequestedQuery = query;
            state.CallerToken = cancellationToken;
            state.BatchRequested = true;
            if (!background)
            {
                state.UserRequested = true;

                // Supersession of an in-flight pass: kill its token now so the running queries
                // stop early; the batch loop (already awake through BatchRequested) starts the
                // newest pass with a fresh generation. A pass still completing after the cancel
                // is harmless — its generation check drops every result.
                if (state.RunCts is { } inflight)
                {
                    inflight.Cancel();
                    state.RunCts = null;
                }
            }

            // Cold paint: with NO snapshot at all, publish the Provisional shell synchronously so
            // the caller's very next frame renders shimmer rows instead of waiting the coalesce
            // window. (UI items always exist — this is the read side of that promise.)
            if (state.Snapshot is null)
            {
                PublishLocked(state, BuildProvisionalShell(source, query));
            }
        }

        if (!background)
        {
            // The newest interactive intent outranks every outstanding peer prefetch: cancel them.
            lock (_prefetchGate)
            {
                try
                {
                    _prefetchCts?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The chain finished (and cleaned itself) concurrently — nothing to cancel.
                }

                _prefetchCts = null;
            }
        }

        lock (state.Gate)
        {
            // A batch loop is already running for this source; it will serve the new request.
            if (state.BatchLoop is { IsCompleted: false } running)
            {
                return running;
            }

            var loop = RunBatchLoopAsync(state, source);
            state.BatchLoop = loop;
            return loop;
        }
    }

    /// <summary>
    /// Optimistic local flip (card tap): the row exists NOW as read, the DB truth arrives via the
    /// deferred write; the overlay set keeps a refine pass racing that write from resurrecting the
    /// unread flag (no flicker). Re-publishes the snapshot under the SAME generation (an overlay,
    /// not a new batch), so renderers reconcile by id without churn.
    /// </summary>
    public void ApplyOptimisticRead(SourceId source, long projectId)
    {
        var state = StateFor(source);
        lock (state.Gate)
        {
            state.OptimisticReads.Add(projectId);
            if (state.Snapshot is { } snapshot)
            {
                var wasUnread = snapshot.Items.Any(item => item.ProjectId == projectId && item.IsUnread);
                var items = ApplyOverlay(snapshot.Items, projectId);
                var counts = wasUnread
                    ? snapshot.Counts with { Unread = Math.Max(0, snapshot.Counts.Unread - 1) }
                    : snapshot.Counts;
                PublishLocked(state, snapshot with { Items = items, Counts = counts });
            }
        }
    }

    /// <summary>
    /// Lowest-priority sequential background refresh of every NON-active registered source, so the
    /// next morph is memory-hot and shows a skeleton only at a true cold start. The chain is
    /// cancelled when any newer interactive refresh supersedes it (see <see cref="RefreshAsync"/>).
    /// </summary>
    public Task PrefetchPeersAsync(SourceId activeSource)
    {
        CancellationTokenSource chain;
        lock (_prefetchGate)
        {
            _prefetchCts?.Cancel();
            chain = _prefetchCts = new CancellationTokenSource();
        }

        return RunChainAsync(chain);

        async Task RunChainAsync(CancellationTokenSource prefetchCts)
        {
            try
            {
                foreach (var peer in _sources.Where(id => id != activeSource).ToList())
                {
                    if (prefetchCts.IsCancellationRequested)
                    {
                        return;
                    }

                    var existing = Peek(peer);
                    if (existing is { Generation: > 0 } && _clock() - existing.StampedAt < PeerPrefetchTtl)
                    {
                        continue; // already hot — don't spend the shared DB budget
                    }

                    try
                    {
                        await RefreshCoreAsync(peer, query: null, prefetchCts.Token, background: true);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // superseded mid-chain
                    }
                    catch (Exception ex)
                    {
                        _faultSink?.Invoke("SourceFeedStore.PrefetchPeers", ex);
                    }
                }
            }
            finally
            {
                // Self-clean under the same gate the supersession-cancel uses, so no thread can
                // Cancel() a disposed instance (cancel site also guards, belt-for-races).
                lock (_prefetchGate)
                {
                    if (ReferenceEquals(_prefetchCts, prefetchCts))
                    {
                        _prefetchCts = null;
                    }
                }

                prefetchCts.Dispose();
            }
        }
    }

    // -----------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------

    private sealed class SourceState
    {
        /// <summary>Writers-only lock; <see cref="Peek"/> never takes it.</summary>
        public readonly object Gate = new();

        /// <summary>The swapped pointer — volatile so lock-free readers see whole immutable snapshots.</summary>
        public volatile FeedSnapshot? Snapshot;

        public long Generation;

        /// <summary>The query of the newest pending request (last-wins inside the coalesce window).</summary>
        public string? RequestedQuery;

        public CancellationToken CallerToken;

        public bool BatchRequested;

        /// <summary>True while the newest queued request is user intent (vs warm-peer prefetch).</summary>
        public bool UserRequested;

        public Task? BatchLoop;

        /// <summary>The in-flight pass token; cancelled on supersession, replaced at each pass start.</summary>
        public CancellationTokenSource? RunCts;

        /// <summary>Project ids optimistically marked read whose DB write may not have landed yet — refine passes re-apply the overlay so unread never flickers back.</summary>
        public readonly HashSet<long> OptimisticReads = [];
    }

    private SourceState StateFor(SourceId source) =>
        _states.GetOrAdd(source, static _ => new SourceState());

    /// <summary>
    /// The per-source batch loop: serve <c>BatchRequested</c> with one pass each, but only AFTER
    /// the <see cref="CoalesceWindow"/> goes quiet — requests arriving during the window merge into
    /// the same pass (last query wins). The loop exits (clearing <c>BatchLoop</c>) when nothing is
    /// pending; late requests restart it.
    /// </summary>
    private async Task RunBatchLoopAsync(SourceState state, SourceId source)
    {
        while (true)
        {
            string? query;
            long generation;
            CancellationToken passToken;
            bool userInitiated;

            lock (state.Gate)
            {
                if (!state.BatchRequested)
                {
                    state.BatchLoop = null;
                    return;
                }
            }

            // ---- coalescing quiet window (a supersession cancels it early; the burst is consumed below either way) ----
            CancellationTokenSource windowCts;
            lock (state.Gate)
            {
                windowCts = state.RunCts ??= new CancellationTokenSource();
            }

            try
            {
                await Task.Delay(CoalesceWindow, windowCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Superseded / replaced — fall through; the consume step takes the NEWEST request.
            }

            CancellationTokenSource passCts;
            lock (state.Gate)
            {
                if (!state.BatchRequested)
                {
                    state.BatchLoop = null;
                    return;
                }

                query = state.RequestedQuery;
                userInitiated = state.UserRequested;
                generation = ++state.Generation;

                // Supersession of anything still running under the previous token, then install
                // this pass's token: the next user request cancels THIS one.
                state.RunCts?.Cancel();
                passCts = state.RunCts = new CancellationTokenSource();
                passToken = CancellationTokenSource.CreateLinkedTokenSource(passCts.Token, state.CallerToken).Token;

                state.BatchRequested = false;
                state.UserRequested = false;
            }

            try
            {
                await RunPassAsync(state, source, query, generation, passToken, userInitiated);
            }
            catch (OperationCanceledException)
            {
                // Superseded mid-queries — the newer request (if any) already re-queued; loop serves it.
            }
            catch (Exception ex)
            {
                _faultSink?.Invoke("SourceFeedStore.BatchLoop", ex);
            }
        }
    }

    /// <summary>
    /// ONE query pass: run the existing bounded queries under the pass token, build the next
    /// snapshot completely, swap the pointer, fire <see cref="SnapshotUpdated"/>. Results are
    /// DROPPED (never applied) when the pass was superseded — the formal generation-checked batch
    /// apply that replaced the old <c>_loadRequestToken</c> guard.
    /// </summary>
    private async Task RunPassAsync(SourceState state, SourceId source, string? query, long generation, CancellationToken token, bool userInitiated)
    {
        // Stale-while-revalidate: a refine pass over existing confirmed rows dims them FIRST,
        // so the UI shows "waiting for refine" instead of freezing on the old paint.
        FeedSnapshot? before;
        lock (state.Gate)
        {
            before = state.Snapshot;
            if (before is { Generation: > 0, Error: null } && before.Query == query && before.Items.Count > 0)
            {
                PublishLocked(state, DimToStale(before));
            }
        }

        var listTask = query is null
            ? _data.GetRecentAsync(source, RecentPageLimit, token)
            : _data.SearchAsync(source, query, SearchPageLimit, token);
        var todayTask = _data.CountAddedTodayAsync(source, token);
        var trackedTask = query is null ? _data.CountTrackedAsync(source, token) : null;

        var list = await listTask;
        var today = await todayTask;
        Result<(int Tracked, int Unread)>? tracked = trackedTask is null ? null : await trackedTask;

        // ---- generation-checked apply: a superseded batch is dropped, never painted ----
        lock (state.Gate)
        {
            if (Volatile.Read(ref state.Generation) != generation || token.IsCancellationRequested)
            {
                return;
            }

            if (list.IsError)
            {
                // Broken page state: the LIST still exists — prior rows survive dimmed (if any),
                // plus the honest error copy a renderer shows with its retry affordance.
                var retained = before is { Generation: > 0, Items.Count: > 0 }
                    ? DimToStale(before).Items
                    : new List<FeedItem> { FeedItem.Broken(source, list.Error.ExternalMessage) };
                PublishLocked(state, new FeedSnapshot
                {
                    Source = source,
                    Items = retained,
                    Counts = before?.Counts ?? CountsSnapshot.Zero,
                    Generation = generation,
                    StampedAt = _clock(),
                    Freshness = SnapshotFreshness.Fresh,
                    Query = query,
                    Error = list.Error.ExternalMessage,
                });
            }
            else
            {
                var items = list.Value
                    .Select(static summary => FeedItem.ConfirmedProject(summary))
                    .ToList();

                // Optimistic-read overlay: a card opened a moment ago must not flicker back to
                // unread while its deferred DB write has not landed. An overlay is forgotten ONLY
                // once the raw DB page itself shows the row read (never by the overlay's own
                // output — that would re-flicker the row on the following pass).
                if (state.OptimisticReads.Count > 0)
                {
                    var dbRead = list.Value.Where(static s => !s.IsUnread).Select(static s => s.ProjectId).ToHashSet();
                    state.OptimisticReads.RemoveWhere(id => dbRead.Contains(id));

                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].Summary is { IsUnread: true } unread && state.OptimisticReads.Contains(unread.ProjectId))
                        {
                            items[i] = items[i] with { Summary = CloneRead(unread) };
                        }
                    }
                }

                var counts = before?.Counts ?? CountsSnapshot.Zero;
                if (tracked is { } trackedResult && trackedResult.IsOk)
                {
                    // The DB's unread total still counts rows whose mark-read write is deferred (or
                    // whose overlay was just applied); subtract the outstanding overlays so the
                    // badge never flickers back up while the queue drains.
                    counts = counts with
                    {
                        Tracked = trackedResult.Value.Tracked,
                        Unread = Math.Max(0, trackedResult.Value.Unread - state.OptimisticReads.Count),
                    };
                }

                if (today.IsOk)
                {
                    counts = counts with { Today = today.Value };
                }

                PublishLocked(state, new FeedSnapshot
                {
                    Source = source,
                    Items = items,
                    Counts = counts,
                    Generation = generation,
                    StampedAt = _clock(),
                    Freshness = SnapshotFreshness.Fresh,
                    Query = query,
                });
            }
        }

        // Warm-peer prefetch runs AFTER the interactive apply landed, outside any lock, chained so
        // the next interactive refresh of any source supersedes (cancels) it.
        if (userInitiated && query is null)
        {
            _ = PrefetchPeersAsync(source);
        }
    }

    private static FeedSnapshot DimToStale(FeedSnapshot snapshot) =>
        snapshot with
        {
            Items = snapshot.Items
                .Select(static item => item.State == FeedRowState.Confirmed ? item with { State = FeedRowState.Stale } : item)
                .ToList(),
            Freshness = SnapshotFreshness.Stale,
        };

    private static ProjectSummary CloneRead(ProjectSummary summary)
    {
        // Shallow copy with the one flipped field: snapshot rows stay private to their snapshot;
        // never mutate an instance another consumer could observe.
        return new ProjectSummary
        {
            Source = summary.Source,
            ProjectId = summary.ProjectId,
            Title = summary.Title,
            Url = summary.Url,
            ClientName = summary.ClientName,
            ClientMeta = summary.ClientMeta,
            PublishTimeNumber = summary.PublishTimeNumber,
            PublishTimeText = summary.PublishTimeText,
            ProposalCount = summary.ProposalCount,
            ProposalCountText = summary.ProposalCountText,
            Budget = summary.Budget,
            DeliveryDays = summary.DeliveryDays,
            SkillsText = summary.SkillsText,
            Description = summary.Description,
            ProjectStatus = summary.ProjectStatus,
            IsUnread = false,
            EnrichmentStatus = summary.EnrichmentStatus,
            DiscoveredAt = summary.DiscoveredAt,
            EnrichedAt = summary.EnrichedAt,
        };
    }

    private static IReadOnlyList<FeedItem> ApplyOverlay(IReadOnlyList<FeedItem> items, long projectId)
    {
        var changed = false;
        var next = new List<FeedItem>(items.Count);
        foreach (var item in items)
        {
            if (item.ProjectId == projectId && item.Summary is { IsUnread: true } summary)
            {
                next.Add(item with { Summary = CloneRead(summary) });
                changed = true;
            }
            else
            {
                next.Add(item);
            }
        }

        return changed ? next : items;
    }

    private static FeedSnapshot BuildProvisionalShell(SourceId source, string? query) =>
        new()
        {
            Source = source,
            Items = Enumerable.Range(0, ProvisionalSkeletonRows).Select(i => FeedItem.Provisional(source, i)).ToList(),
            Counts = CountsSnapshot.Zero,
            Generation = 0,
            StampedAt = DateTimeOffset.MinValue,
            Freshness = SnapshotFreshness.Cold,
            Query = query,
        };

    /// <summary>Build-next is complete by the time this is called: swap the pointer and fire. Caller holds <c>state.Gate</c>; the event itself is marshalled so no subscriber work runs under the lock.</summary>
    private void PublishLocked(SourceState state, FeedSnapshot snapshot)
    {
        state.Snapshot = snapshot; // volatile write — the single pointer swap
        var handler = SnapshotUpdated;
        if (handler is null)
        {
            return;
        }

        _marshal(() =>
        {
            try
            {
                handler(snapshot);
            }
            catch (Exception ex)
            {
                _faultSink?.Invoke("SourceFeedStore.SnapshotUpdated", ex);
            }
        });
    }
}

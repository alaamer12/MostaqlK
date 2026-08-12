using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Database.SearchIndex;
using MostaqlK.Models;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;
using MostaqlK.Services.Pipeline;

namespace MostaqlK.Features.Projects.ViewModels;

/// <summary>
/// View-model for the main project feed page (projects.html): holds the sidebar
/// navigation state and the scrollable collection of <see cref="ProjectCardViewModel"/>.
/// Drives all four feed states (loading/empty/error/success) and re-queries
/// <see cref="FtsQueryService"/> (or falls back to the reverse-chronological recent list) as the
/// debounced <see cref="SearchQuery"/> changes.
/// </summary>
public sealed partial class ProjectFeedViewModel : ObservableObject
{
    private const int RecentLimit = 100;
    private const string KeyPollIntervalSeconds = "settings_poll_interval_seconds";
    private const string KeyMaxRequestsPerMinute = "settings_max_requests_per_minute";

    private readonly IProjectRepository _projectRepository;
    private readonly FtsQueryService _ftsQueryService;
    private readonly IPollService _pollService;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly GlobalAppStatusService _globalStatus;

    // Guards against overlapping LoadAsync calls racing each other: if the search box's
    // debounce fires more than once in quick succession (e.g. WinAppDriver/remote SendKeys
    // delivering keystrokes slower than the 300ms debounce window, or a user typing right as a
    // background RefreshAsync/PollService-triggered reload is still in flight), a slower/older
    // query's DB round-trip could finish AFTER a newer one and overwrite the feed with stale
    // (or empty, for a shorter/no-match prefix like "C"/"CS") results even though the visible
    // search box already shows the final term "CSS". Each LoadAsync call is stamped with a
    // monotonically increasing token; only the most recent call is allowed to apply its results.
    private int _loadRequestToken;

    // The feed used to only ever reload from OnAppearing or a manual refresh - nothing in the
    // background pipeline (PollService/WorkerPool/EnrichmentWorker) ever told this view-model that
    // new rows existed, so a page that had already appeared once never showed projects discovered
    // or enriched afterwards, even though the database and the dashboard panel were both fully
    // live. These three pipeline events are debounced into a single reload so a burst of
    // discoveries doesn't hammer the database with one query per project.
    private CancellationTokenSource? _autoReloadDebounce;

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearchActive))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateSubtitle))]
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
    public partial int TrackedCount { get; set; }

    public GlobalAppStatusService GlobalStatus => _globalStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollIntervalText))]
    [NotifyPropertyChangedFor(nameof(LiveStatusText))]
    [NotifyPropertyChangedFor(nameof(PollToggleLabel))]
    public partial bool IsPollingActive { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PollIntervalText))]
    public partial int PollIntervalSeconds { get; set; } = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateLimitText))]
    public partial int RequestsPerMinute { get; set; } = 12;


    /// <summary>True once loading has finished with no error and at least one result — drives the success-state ScrollView.</summary>
    public bool ShowFeed => !IsLoading && !HasError && !IsEmpty;

    public string PollIntervalText => IsPollingActive
        ? $"يتم الفحص كل {PollIntervalSeconds} ثانية"
        : "الفحص متوقف";

    public string RateLimitText => $"{RequestsPerMinute} طلب / دقيقة";

    public string LiveStatusText => IsPollingActive ? "مباشر" : "متوقف";

    public string PollToggleLabel => IsPollingActive ? "إيقاف" : "بدء الفحص";

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
        FtsQueryService ftsQueryService,
        IPollService pollService,
        TokenBucketRateLimiter rateLimiter,
        GlobalAppStatusService globalStatus)
    {
        _projectRepository = projectRepository;
        _ftsQueryService = ftsQueryService;
        _pollService = pollService;
        _rateLimiter = rateLimiter;
        _globalStatus = globalStatus;

        RefreshHeaderStatus();

        // Auto-refresh: a discovered project lands in `projects` immediately (see
        // ProjectRepository's INSERT OR IGNORE), and a completed worker rewrites it with the
        // enriched details, so either event means GetRecentAsync's result set has actually
        // changed. MainWindowPage is created once for the app's lifetime, so this subscription
        // does not need to be torn down.
        _globalStatus.ProjectDiscovered += OnProjectDiscovered;
        _globalStatus.WorkerStateChanged += OnWorkerStateChanged;
        _globalStatus.ProjectRemovedFromQueue += OnProjectRemovedFromQueue;
    }

    private void OnProjectDiscovered(long projectId, string title) => ScheduleAutoReload();

    private void OnProjectRemovedFromQueue(long projectId) => ScheduleAutoReload();

    private void OnWorkerStateChanged(int workerIndex, WorkerState state)
    {
        if (state is WorkerState.Completed or WorkerState.Error)
        {
            ScheduleAutoReload();
        }
    }

    /// <summary>
    /// Debounces a burst of pipeline events (a discovery storm, several workers finishing near
    /// simultaneously) into a single <see cref="LoadAsync"/>, dispatched on the UI thread since
    /// these events are raised from background pipeline loops.
    /// </summary>
    private void ScheduleAutoReload()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _autoReloadDebounce, cts);
        previous?.Cancel();
        previous?.Dispose();

        _ = DebouncedReloadAsync(cts.Token);
    }

    private async Task DebouncedReloadAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(400, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(LoadAsync);
    }

    // The footer's "آخر فحص" line used to be built here, timed from the last moment this *view*
    // reloaded from the database, which has nothing to do with when the pipeline
    // last scanned. That is why it could read "منذ دقيقة" while the header advertised a 30-second
    // poll interval. The wording now lives in the shared LastScanStatus unit and is driven by
    // GlobalAppStatusService.LastScanCompletedAt, the timestamp PollService writes every cycle.

    public void RefreshHeaderStatus()
    {
        PollIntervalSeconds = Preferences.Get(KeyPollIntervalSeconds, _pollService.PollIntervalSeconds);
        RequestsPerMinute = Preferences.Get(KeyMaxRequestsPerMinute, Math.Max(1, _rateLimiter.Capacity));
        IsPollingActive = !_pollService.IsPaused;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var requestToken = ++_loadRequestToken;
        var searchQueryAtRequestTime = SearchQuery;

        IsLoading = true;
        HasError = false;
        ErrorMessage = null;
        RefreshHeaderStatus();
        try
        {
            var today = await _projectRepository.CountAddedTodayAsync();
            if (requestToken != _loadRequestToken)
            {
                // A newer LoadAsync call has already started (and will apply its own results
                // below) - abandon this stale one so it can never overwrite fresher data.
                return;
            }
            if (today.IsOk)
            {
                _globalStatus.SetProjectsAddedToday(today.Value);
            }

            var result = string.IsNullOrWhiteSpace(searchQueryAtRequestTime)
                ? await _projectRepository.GetRecentAsync(RecentLimit)
                : await _ftsQueryService.SearchAsync(searchQueryAtRequestTime.Trim());

            if (requestToken != _loadRequestToken)
            {
                // Superseded while awaiting the DB query - discard these results, whatever they
                // are, rather than letting an older/slower query clobber a newer one's output.
                return;
            }

            if (!result.IsOk)
            {
                HasError = true;
                ErrorMessage = result.Error.ExternalMessage;
                IsEmpty = false;
                return;
            }

            Projects.Clear();
            foreach (var project in result.Value)
            {
                Projects.Add(new ProjectCardViewModel(project, card => _ = SelectProjectAsync(card)));
            }

            if (IsSearchActive)
            {
                // While a search filter is active, the footer should reflect the filtered
                // (visible) results rather than the whole store's totals.
                TrackedCount = Projects.Count;
                UnreadCount = Projects.Count(p => p.IsUnread);
            }
            else
            {
                // No filter active: status-bar totals count the whole store, not just the
                // page of rows loaded above.
                var tracked = await _projectRepository.CountTrackedAsync();
                if (requestToken != _loadRequestToken)
                {
                    return;
                }
                if (tracked.IsOk)
                {
                    TrackedCount = tracked.Value.Tracked;
                    UnreadCount = tracked.Value.Unread;
                }
                else
                {
                    TrackedCount = Projects.Count;
                    UnreadCount = Projects.Count(p => p.IsUnread);
                }
            }

            IsEmpty = Projects.Count == 0;
        }
        finally
        {
            if (requestToken == _loadRequestToken)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(ShowFeed));
            }
        }
    }

    [TraceInteraction("RefreshCommand")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "RefreshCommand")]
    [RelayCommand]
    public async Task RefreshAsync()
    {
        using var _ = TraceScope.Begin("RefreshCommand");
        try
        {
            // Reload what is already stored, and ask the pipeline for a scan now: the footer's
            // "آخر فحص" reflects real scans, so the button has to cause one rather than pretend.
            _pollService.RequestCheckNow();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    /// <summary>Invoked by <c>SearchInputField.DebouncedCommand</c> once the debounce window elapses.</summary>
    [RelayCommand]
    public async Task SearchAsync(string? query)
    {
        SearchQuery = query ?? string.Empty;
        await LoadAsync();
    }

    [RelayCommand]
    public async Task ClearSearchAsync()
    {
        SearchQuery = string.Empty;
        await LoadAsync();
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
            OnPropertyChanged(nameof(PollIntervalText));
            OnPropertyChanged(nameof(LiveStatusText));
            OnPropertyChanged(nameof(PollToggleLabel));
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    [RelayCommand]
    public void MarkAllRead()
    {
        foreach (var project in Projects)
        {
            project.MarkAsRead();
        }

        UnreadCount = Projects.Count(p => p.IsUnread);
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

            card.MarkAsRead();
            UnreadCount = Projects.Count(p => p.IsUnread);

            await Shell.Current.GoToAsync($"ProjectDetailsPage?projectId={card.Project.ProjectId}");
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }
}

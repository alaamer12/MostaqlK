using CommunityToolkit.Mvvm.ComponentModel;

namespace MostaqlK.Services;

/// <summary>
/// A singleton service that holds global application state that must persist across page
/// navigations, specifically sidebar statistics like projects added today and unread
/// notification counts.
/// </summary>
public sealed partial class GlobalAppStatusService : ObservableObject
{
    /// <summary>
    /// Every property here is written from the pipeline's own background loops (`PollService`,
    /// `EnrichmentWorker`), while the bindings that read them live in the footer and the pipeline
    /// dashboard. Raising <c>PropertyChanged</c> off the UI thread is a real crash risk on WinUI, so
    /// the notification is marshalled once, centrally, rather than at every call site.
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        // FIX (fast-fail crash on window close, exit code -1073741189 / 0xC000027B): the pipeline's
        // background loops (PollService/EnrichmentWorker) keep raising property changes for a short
        // window after the user closes the app. By then WinUI has already torn down the native
        // DispatcherQueue, so `dispatcher.Dispatch` (and even `IsDispatchRequired`) can throw a WinRT
        // interop exception on a thread-pool thread that the runtime cannot recover from, which
        // Windows reports as a stack-buffer-overrun fast-fail rather than a normal .NET exception.
        // Swallowing it here keeps shutdown graceful instead of hard-crashing the process.
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.IsDispatchRequired is false)
            {
                base.OnPropertyChanged(e);
                return;
            }

            dispatcher.Dispatch(() => base.OnPropertyChanged(e));
        }
        catch (Exception ex)
        {
            MostaqlK.Services.Diagnostics.InteractionLogger.Fault("GlobalAppStatusService.OnPropertyChanged.DispatchFailed", ex);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectsAddedTodayText))]
    public partial int ProjectsAddedTodayCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadNotificationCountText))]
    public partial int UnreadNotificationCount { get; set; }

    // --- Pipeline Radar Metrics ---

    /// <summary>Discovery Tier (Outer Ring): 0 to 1.0 represents scan progress.</summary>
    [ObservableProperty]
    public partial double DiscoveryProgress { get; set; }

    /// <summary>Queue Tier (Middle Ring): 0 to 1.0 represents backlog pressure.</summary>
    [ObservableProperty]
    public partial double QueuePressure { get; set; }

    /// <summary>Enrichment Tier (Inner Ring): Bitmask or float representing worker activity.</summary>
    [ObservableProperty]
    public partial double EnrichmentActivity { get; set; }

    /// <summary>Detailed states for each worker (0-2).</summary>
    public WorkerState[] WorkerStates { get; } = new WorkerState[3];

    /// <summary>Per-worker telemetry surfaced by the radar's worker tooltip.</summary>
    public WorkerTelemetry[] Workers { get; } =
    [
        new WorkerTelemetry(0),
        new WorkerTelemetry(1),
        new WorkerTelemetry(2),
    ];

    /// <summary>Backlog capacity the queue ring/utilisation percentage is measured against.</summary>
    [ObservableProperty]
    public partial int QueueCapacity { get; set; } = 50;

    /// <summary>Number of project ids currently waiting in the discovery backlog.</summary>
    [ObservableProperty]
    public partial int QueueCount { get; set; }

    /// <summary>Total projects discovered since the app started (radar discovery tooltip).</summary>
    [ObservableProperty]
    public partial int ProjectsDiscoveredCount { get; set; }

    /// <summary>When the last listing scan completed, used for "Last scan: 1.4s ago".</summary>
    [ObservableProperty]
    public partial DateTimeOffset? LastScanCompletedAt { get; set; }

    /// <summary>Configured poll interval, mirrored from <c>PollService.PollIntervalSeconds</c>.</summary>
    [ObservableProperty]
    public partial int ScanIntervalSeconds { get; set; } = 30;

    /// <summary>True while a listing scan is in flight (drives the radar's scanning segment).</summary>
    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    // --- Scan outcome ---
    // A scan that fails, and a scan that succeeds but finds nothing new, used to look identical to
    // the user: both left every number at zero. Only `LastScanCompletedAt` moved, and only on
    // success, so a permanently failing endpoint was indistinguishable from "no new projects".
    // These properties make the outcome of the *attempt* visible.

    /// <summary>When the last scan attempt started, successful or not.</summary>
    [ObservableProperty]
    public partial DateTimeOffset? LastScanAttemptedAt { get; set; }

    /// <summary>Number of scan attempts since the app started.</summary>
    [ObservableProperty]
    public partial int ScanAttemptCount { get; set; }

    /// <summary>Projects returned by the last successful listing fetch (new + already known).</summary>
    [ObservableProperty]
    public partial int LastScanSeenCount { get; set; }

    /// <summary>Genuinely new projects enqueued by the last successful scan.</summary>
    [ObservableProperty]
    public partial int LastScanNewCount { get; set; }

    /// <summary>True when the most recent scan attempt failed; drives the panel's error line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionText))]
    [NotifyPropertyChangedFor(nameof(IsConnectionHealthy))]
    public partial bool LastScanFailed { get; set; }

    /// <summary>
    /// Footer connection line. It used to be the hard-coded string "الاتصال: متصل" next to a
    /// permanently green dot, so the app cheerfully claimed to be connected while every single scan
    /// was being rejected.
    /// </summary>
    public string ConnectionText => LastScanFailed ? "الاتصال: فشل الفحص" : "الاتصال: متصل";

    /// <summary>Drives which of the two footer dots is shown (green vs red).</summary>
    public bool IsConnectionHealthy => !LastScanFailed;

    /// <summary>The failing error's code (e.g. <c>HTTP.UNEXPECTED_STATUS</c>) for the log/diagnostics.</summary>
    [ObservableProperty]
    public partial string? LastScanErrorCode { get; set; }

    /// <summary>The user-facing half of the failing <see cref="Core.DomainError"/>.</summary>
    [ObservableProperty]
    public partial string? LastScanErrorMessage { get; set; }

    /// <summary>The failing error's remediation hint, when it carries one.</summary>
    [ObservableProperty]
    public partial string? LastScanFixMessage { get; set; }

    /// <summary>
    /// Publishes a failed scan attempt. Callers must also log through
    /// <c>InteractionLogger.Failure</c> - this is the on-screen half, not a replacement for the log.
    /// </summary>
    public void NotifyScanFailed(Core.DomainError error)
    {
        LastScanFailed = true;
        LastScanErrorCode = error.Code;
        LastScanErrorMessage = error.ExternalMessage;
        LastScanFixMessage = error.FixMessage;
    }

    /// <summary>Publishes a successful scan attempt, clearing any previous failure.</summary>
    public void NotifyScanSucceeded(int seenCount, int newCount)
    {
        LastScanFailed = false;
        LastScanErrorCode = null;
        LastScanErrorMessage = null;
        LastScanFixMessage = null;
        LastScanSeenCount = seenCount;
        LastScanNewCount = newCount;
        LastScanCompletedAt = DateTimeOffset.UtcNow;
    }

    // Enqueue timestamps, so the queue tooltip can report the oldest item and the average wait.
    private readonly Dictionary<long, DateTimeOffset> _queueEnqueuedAt = new();
    private readonly object _queueSync = new();

    /// <summary>
    /// Project id -> title, remembered from the discovery listing so every later pipeline stage can
    /// name a project semantically. Guarded by <c>_queueSync</c>.
    /// </summary>
    private readonly Dictionary<long, string> _projectTitles = new();
    private const int MaxRememberedTitles = 512;
    private double _waitSecondsTotal;
    private int _waitSamples;

    /// <summary>Age of the oldest item still waiting in the backlog, in seconds.</summary>
    public double OldestQueuedItemSeconds
    {
        get
        {
            lock (_queueSync)
            {
                if (_queueEnqueuedAt.Count == 0)
                {
                    return 0;
                }

                var oldest = DateTimeOffset.MaxValue;
                foreach (var enqueuedAt in _queueEnqueuedAt.Values)
                {
                    if (enqueuedAt < oldest)
                    {
                        oldest = enqueuedAt;
                    }
                }

                return Math.Max(0, (DateTimeOffset.UtcNow - oldest).TotalSeconds);
            }
        }
    }

    /// <summary>Rolling average time a project spent queued before a worker claimed it.</summary>
    public double AverageQueueWaitSeconds
    {
        get
        {
            lock (_queueSync)
            {
                return _waitSamples == 0 ? 0 : _waitSecondsTotal / _waitSamples;
            }
        }
    }

    public event Action<int, WorkerState>? WorkerStateChanged;

    public void UpdateWorkerState(int index, WorkerState state)
    {
        if (index < 0 || index >= 3) return;
        if (WorkerStates[index] == state) return;

        WorkerStates[index] = state;
        var worker = Workers[index];
        worker.State = state;

        switch (state)
        {
            case WorkerState.Processing:
                worker.ProcessingStartedAt = DateTimeOffset.UtcNow;
                break;

            case WorkerState.Completed:
                worker.CompletedCount++;
                worker.LastProcessingSeconds = worker.ElapsedSeconds;
                worker.ProcessingStartedAt = null;
                break;

            case WorkerState.Error:
                worker.ErrorCount++;
                worker.LastProcessingSeconds = worker.ElapsedSeconds;
                worker.ProcessingStartedAt = null;
                break;

            default:
                worker.ProcessingStartedAt = null;
                worker.CurrentProjectTitle = string.Empty;
                worker.CurrentProjectId = null;
                break;
        }

        WorkerStateChanged?.Invoke(index, state);
    }

    /// <summary>Raised when a brand new project id enters the pipeline (discovery -> backlog).</summary>
    public event Action<long, string>? ProjectDiscovered;

    /// <summary>Raised when a worker claims a queued project (backlog -> enrichment).</summary>
    public event Action<int, long, string>? ProjectAssignedToWorker;

    /// <summary>Raised when a queued project leaves the pipeline without being enriched.</summary>
    public event Action<long>? ProjectRemovedFromQueue;

    public void NotifyProjectDiscovered(long projectId, string title = "")
    {
        ProjectsDiscoveredCount++;
        lock (_queueSync)
        {
            _queueEnqueuedAt[projectId] = DateTimeOffset.UtcNow;
            RegisterTitle(projectId, title);
        }

        ProjectDiscovered?.Invoke(projectId, title);
    }

    /// <summary>
    /// Remembers the human-readable title a project was discovered with, so every later stage can
    /// name it. The pipeline only writes a project row once it has been *enriched*, so between
    /// discovery and completion the listing snapshot is the single place a title exists at all -
    /// which is why the worker cards used to show a bare `#1267826` instead of the project name.
    /// Must be called while holding <c>_queueSync</c>.
    /// </summary>
    private void RegisterTitle(long projectId, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        _projectTitles[projectId] = title.Trim();

        // Bounded: a long-running session discovers thousands of projects and only the in-flight
        // ones are ever asked about, so the oldest entries are dropped rather than kept forever.
        if (_projectTitles.Count > MaxRememberedTitles)
        {
            foreach (var stale in _projectTitles.Keys.Take(_projectTitles.Count - MaxRememberedTitles).ToList())
            {
                _projectTitles.Remove(stale);
            }
        }
    }

    /// <summary>The remembered title for a project, or an empty string when we never saw one.</summary>
    public string TitleOf(long projectId)
    {
        lock (_queueSync)
        {
            return _projectTitles.TryGetValue(projectId, out var title) ? title : string.Empty;
        }
    }

    public void NotifyProjectAssignedToWorker(int workerIndex, long projectId, string title = "")
    {
        if (workerIndex < 0 || workerIndex >= 3) return;

        lock (_queueSync)
        {
            if (_queueEnqueuedAt.Remove(projectId, out var enqueuedAt))
            {
                _waitSecondsTotal += Math.Max(0, (DateTimeOffset.UtcNow - enqueuedAt).TotalSeconds);
                _waitSamples++;
            }
        }

        var resolved = string.IsNullOrWhiteSpace(title) ? TitleOf(projectId) : title.Trim();

        var worker = Workers[workerIndex];
        worker.CurrentProjectId = projectId;
        // The id is only a last resort now: the caller's title wins, then the title remembered from
        // discovery, and `#id` only remains for a project recovered from the persisted backlog whose
        // listing row this process never saw (it is replaced by the real title the moment the detail
        // page is parsed - see UpdateWorkerProjectTitle).
        worker.CurrentProjectTitle = string.IsNullOrEmpty(resolved) ? $"#{projectId}" : resolved;

        ProjectAssignedToWorker?.Invoke(workerIndex, projectId, worker.CurrentProjectTitle);
    }

    /// <summary>
    /// Replaces a worker's current project title once a better one is known (the enriched detail
    /// page). Ignored if the worker has already moved on to another project.
    /// </summary>
    public void UpdateWorkerProjectTitle(int workerIndex, long projectId, string title)
    {
        if (workerIndex < 0 || workerIndex >= 3 || string.IsNullOrWhiteSpace(title)) return;

        lock (_queueSync)
        {
            RegisterTitle(projectId, title);
        }

        var worker = Workers[workerIndex];
        if (worker.CurrentProjectId != projectId) return;

        worker.CurrentProjectTitle = title.Trim();
        ProjectAssignedToWorker?.Invoke(workerIndex, projectId, worker.CurrentProjectTitle);
    }

    public void NotifyProjectRemovedFromQueue(long projectId)
    {
        lock (_queueSync)
        {
            _queueEnqueuedAt.Remove(projectId);
        }

        ProjectRemovedFromQueue?.Invoke(projectId);
    }

    /// <summary>Publishes the backlog size, keeping <see cref="QueuePressure"/> in sync with it.</summary>
    public void UpdateQueueCount(int count)
    {
        QueueCount = Math.Max(0, count);
        QueuePressure = QueueCapacity <= 0 ? 0 : Math.Min(1.0, QueueCount / (double)QueueCapacity);
    }

    /// <summary>Triggered whenever a snapshot is taken, driving the radar sweep.</summary>
    [ObservableProperty]
    public partial bool IsSnapshotActive { get; set; }

    public string ProjectsAddedTodayText => ProjectsAddedTodayCount.ToString();
    public string UnreadNotificationCountText => UnreadNotificationCount.ToString();

    public void IncrementProjectsAddedToday()
    {
        ProjectsAddedTodayCount++;
    }

    public void SetProjectsAddedToday(int count)
    {
        ProjectsAddedTodayCount = count;
    }

    public void IncrementUnreadNotificationCount()
    {
        UnreadNotificationCount++;
    }

    public void ResetUnreadNotificationCount()
    {
        UnreadNotificationCount = 0;
    }
}

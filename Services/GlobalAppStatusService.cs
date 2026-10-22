using CommunityToolkit.Mvvm.ComponentModel;
using MostaqlK.Core.Sources;
using MostaqlK.Services.Pipeline;

namespace MostaqlK.Services;

/// <summary>
/// Singleton UI-facing status. Per-source pipeline telemetry lives in
/// <see cref="SourcePipelineStatus"/> instances owned by each <see cref="SourcePipeline"/>;
/// this service projects the ACTIVE source's instance (property-changed events and pipeline
/// events are re-raised verbatim, so every existing XAML binding and radar/dashboard
/// subscription resolves unchanged) and keeps the genuinely global state (the unread
/// notification badge) plus cross-source aggregates for the tray.
/// </summary>
public sealed partial class GlobalAppStatusService : ObservableObject
{
    private readonly Dictionary<SourceId, SourcePipelineStatus> _statuses = new();
    private readonly Dictionary<SourcePipelineStatus, EventBridge> _bridges = new();
    private readonly object _gate = new();

    /// <summary>
    /// Before the orchestrator attaches the real pipelines, the projection targets a private
    /// detached instance so early-constructed consumers (transient VMs during startup) never
    /// null-ref; it simply shows zeros.
    /// </summary>
    private SourcePipelineStatus _active;

    /// <summary>The source the projection (and therefore the visible UI) currently follows.</summary>
    public SourceId ActiveSource
    {
        get
        {
            lock (_gate)
            {
                return _active.Source;
            }
        }
    }

    public GlobalAppStatusService(SourceRegistry registry)
    {
        _active = new SourcePipelineStatus(SourceId.Default, registry);
    }

    /// <summary>The projected source's status; use <see cref="StatusFor"/> for any other source.</summary>
    public SourcePipelineStatus Active => _active;

    public SourcePipelineStatus StatusFor(SourceId source)
    {
        lock (_gate)
        {
            return _statuses.TryGetValue(source, out var status)
                ? status
                : throw new KeyNotFoundException($"No pipeline status for source '{source}'.");
        }
    }

    public IReadOnlyCollection<SourcePipelineStatus> All
    {
        get
        {
            lock (_gate)
            {
                return _statuses.Values.ToList();
            }
        }
    }

    /// <summary>Called once by <see cref="SourcePipelineOrchestrator"/> while it builds the pipelines.</summary>
    public void Attach(IEnumerable<SourcePipelineStatus> statuses, SourceId activeSource)
    {
        lock (_gate)
        {
            foreach (var status in statuses)
            {
                _statuses[status.Source] = status;
            }

            if (!_statuses.TryGetValue(activeSource, out var active))
            {
                throw new KeyNotFoundException($"Active source '{activeSource}' has no pipeline status.");
            }

            SwitchProjection(active);
        }
    }

    /// <summary>
    /// Switches which source the UI projection follows (the v3 rail will call this). Only the
    /// active source's events are bridged, so a background source can never paint this source's
    /// radar.
    /// </summary>
    public void SetActiveProjection(SourceId source)
    {
        lock (_gate)
        {
            if (_statuses.TryGetValue(source, out var next) && !ReferenceEquals(next, _active))
            {
                SwitchProjection(next);
            }
        }
    }

    private void SwitchProjection(SourcePipelineStatus next)
    {
        _active.PropertyChanged -= OnActivePropertyChanged;
        if (_bridges.Remove(_active, out var oldBridge))
        {
            oldBridge.Detach();
        }

        _active = next;
        _active.PropertyChanged += OnActivePropertyChanged;
        _bridges[_active] = new EventBridge(this, _active);
        // Empty property name = "everything changed", so all bindings re-read from the new target.
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(string.Empty));
    }

    // --- Cross-source aggregates (tray) ---

    /// <summary>Any source scanning right now.</summary>
    public bool AnyScanning
    {
        get
        {
            lock (_gate)
            {
                return _statuses.Count == 0 ? _active.IsScanning : _statuses.Values.Any(s => s.IsScanning);
            }
        }
    }

    /// <summary>Any source whose last scan attempt failed.</summary>
    public bool AnyScanFailed
    {
        get
        {
            lock (_gate)
            {
                return _statuses.Count == 0 ? _active.LastScanFailed : _statuses.Values.Any(s => s.LastScanFailed);
            }
        }
    }

    /// <summary>
    /// Same dispatcher-marshalling + teardown guard as <see cref="SourcePipelineStatus"/>,
    /// because re-raised notifications arrive on the pipeline's background threads too.
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

    private void OnActivePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => OnPropertyChanged(e);

    // --- Global state (not per source) ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadNotificationCountText))]
    public partial int UnreadNotificationCount { get; set; }

    public string UnreadNotificationCountText => UnreadNotificationCount.ToString();

    public void IncrementUnreadNotificationCount(int by = 1)
    {
        UnreadNotificationCount += by;
    }

    public void ResetUnreadNotificationCount()
    {
        UnreadNotificationCount = 0;
    }

    // --- Active-source projection (get/set pass through; notifications re-raised) ---

    public int ProjectsAddedTodayCount
    {
        get => _active.ProjectsAddedTodayCount;
        set => _active.ProjectsAddedTodayCount = value;
    }

    public string ProjectsAddedTodayText => _active.ProjectsAddedTodayText;

    public double DiscoveryProgress
    {
        get => _active.DiscoveryProgress;
        set => _active.DiscoveryProgress = value;
    }

    public double QueuePressure
    {
        get => _active.QueuePressure;
        set => _active.QueuePressure = value;
    }

    public double EnrichmentActivity
    {
        get => _active.EnrichmentActivity;
        set => _active.EnrichmentActivity = value;
    }

    public WorkerState[] WorkerStates => _active.WorkerStates;

    public WorkerTelemetry[] Workers => _active.Workers;

    public int QueueCapacity
    {
        get => _active.QueueCapacity;
        set => _active.QueueCapacity = value;
    }

    public int QueueCount => _active.QueueCount;

    public int ProjectsDiscoveredCount => _active.ProjectsDiscoveredCount;

    public DateTimeOffset? LastScanCompletedAt => _active.LastScanCompletedAt;

    public int ScanIntervalSeconds => _active.ScanIntervalSeconds;

    public bool IsScanning => _active.IsScanning;

    public DateTimeOffset? LastScanAttemptedAt => _active.LastScanAttemptedAt;

    public int ScanAttemptCount => _active.ScanAttemptCount;

    public int LastScanSeenCount => _active.LastScanSeenCount;

    public int LastScanNewCount => _active.LastScanNewCount;

    public bool LastScanFailed => _active.LastScanFailed;

    public string ConnectionText => _active.ConnectionText;

    public bool IsConnectionHealthy => _active.IsConnectionHealthy;

    public string? LastScanErrorCode => _active.LastScanErrorCode;

    public string? LastScanErrorMessage => _active.LastScanErrorMessage;

    public string? LastScanFixMessage => _active.LastScanFixMessage;

    public double OldestQueuedItemSeconds => _active.OldestQueuedItemSeconds;

    public double AverageQueueWaitSeconds => _active.AverageQueueWaitSeconds;

    public bool IsSnapshotActive => _active.IsSnapshotActive;

    // The pipeline components write their own SourcePipelineStatus now; these remain for any
    // non-pipeline caller and forward to the active source.

    public void NotifyScanFailed(Core.DomainError error) => _active.NotifyScanFailed(error);

    public void NotifyScanSucceeded(int seenCount, int newCount) => _active.NotifyScanSucceeded(seenCount, newCount);

    public void UpdateWorkerState(int index, WorkerState state) => _active.UpdateWorkerState(index, state);

    public void NotifyProjectDiscovered(SourceProjectId projectId, string title = "") => _active.NotifyProjectDiscovered(projectId, title);

    public void NotifyProjectAssignedToWorker(int workerIndex, SourceProjectId projectId, string title = "")
        => _active.NotifyProjectAssignedToWorker(workerIndex, projectId, title);

    public void UpdateWorkerProjectTitle(int workerIndex, SourceProjectId projectId, string title)
        => _active.UpdateWorkerProjectTitle(workerIndex, projectId, title);

    public void NotifyProjectRemovedFromQueue(SourceProjectId projectId) => _active.NotifyProjectRemovedFromQueue(projectId);

    public void UpdateQueueCount(int count) => _active.UpdateQueueCount(count);

    public string TitleOf(long projectId) => _active.TitleOf(projectId);

    public void IncrementProjectsAddedToday() => _active.IncrementProjectsAddedToday();

    public void SetProjectsAddedToday(int count) => _active.SetProjectsAddedToday(count);

    /// <summary>
    /// Event pass-through: radar/dashboard components subscribe here and receive the active
    /// source's events only; the bridge re-attaches whenever the projection target changes.
    /// </summary>
    public event Action<int, WorkerState>? WorkerStateChanged;

    public event Action<long, string>? ProjectDiscovered;

    public event Action<int, long, string>? ProjectAssignedToWorker;

    public event Action<long>? ProjectRemovedFromQueue;

    private sealed class EventBridge
    {
        private readonly GlobalAppStatusService _owner;
        private readonly SourcePipelineStatus _status;

        public EventBridge(GlobalAppStatusService owner, SourcePipelineStatus status)
        {
            _owner = owner;
            _status = status;
            status.WorkerStateChanged += OnWorkerStateChanged;
            status.ProjectDiscovered += OnProjectDiscovered;
            status.ProjectAssignedToWorker += OnProjectAssignedToWorker;
            status.ProjectRemovedFromQueue += OnProjectRemovedFromQueue;
        }

        public void Detach()
        {
            _status.WorkerStateChanged -= OnWorkerStateChanged;
            _status.ProjectDiscovered -= OnProjectDiscovered;
            _status.ProjectAssignedToWorker -= OnProjectAssignedToWorker;
            _status.ProjectRemovedFromQueue -= OnProjectRemovedFromQueue;
        }

        private void OnWorkerStateChanged(int index, WorkerState state) => _owner.WorkerStateChanged?.Invoke(index, state);

        private void OnProjectDiscovered(long id, string title) => _owner.ProjectDiscovered?.Invoke(id, title);

        private void OnProjectAssignedToWorker(int workerIndex, long id, string title) => _owner.ProjectAssignedToWorker?.Invoke(workerIndex, id, title);

        private void OnProjectRemovedFromQueue(long id) => _owner.ProjectRemovedFromQueue?.Invoke(id);
    }
}

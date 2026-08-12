using CommunityToolkit.Mvvm.ComponentModel;

namespace MostaqlK.Services;

/// <summary>
/// A singleton service that holds global application state that must persist across page
/// navigations, specifically sidebar statistics like projects added today and unread
/// notification counts.
/// </summary>
public sealed partial class GlobalAppStatusService : ObservableObject
{
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

    public event Action<int, WorkerState>? WorkerStateChanged;

    public void UpdateWorkerState(int index, WorkerState state)
    {
        if (index < 0 || index >= 3) return;
        if (WorkerStates[index] == state) return;

        WorkerStates[index] = state;
        WorkerStateChanged?.Invoke(index, state);
    }

    public event Action? ProjectDiscovered;

    public void NotifyProjectDiscovered()
    {
        ProjectDiscovered?.Invoke();
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

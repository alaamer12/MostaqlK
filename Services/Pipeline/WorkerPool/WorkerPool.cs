using MostaqlK.Core;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Services;

namespace MostaqlK.Services.Pipeline.WorkerPool;

/// <summary>
/// Owns a fixed-size pool of <see cref="EnrichmentWorker"/> instances draining the shared
/// <see cref="DiscoveryQueue"/> concurrently, bounded by the configured worker count.
/// </summary>
public sealed class WorkerPool
{
    private readonly DiscoveryQueue _discoveryQueue;
    private readonly IEnrichmentService _enrichmentService;
    private readonly InFlightTracker _inFlightTracker;
    private readonly IProjectRepository _projectRepository;
    private readonly GlobalAppStatusService _globalStatus;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly List<Task> _runningWorkers = [];

    // Per system-components.md #6 (Worker Pool): `max_concurrent_detail_fetches` default 2-3.
    public int WorkerCount { get; set; } = 3;

    public WorkerPool(
        DiscoveryQueue discoveryQueue,
        IEnrichmentService enrichmentService,
        InFlightTracker inFlightTracker,
        IProjectRepository projectRepository,
        GlobalAppStatusService globalStatus,
        INotificationDispatcher notificationDispatcher)
    {
        _discoveryQueue = discoveryQueue;
        _enrichmentService = enrichmentService;
        _inFlightTracker = inFlightTracker;
        _projectRepository = projectRepository;
        _globalStatus = globalStatus;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task<Result<bool>> StartAsync(CancellationToken cancellationToken = default)
    {
        // Recovery: Load pending IDs from the discovery backlog table into the queue on startup.
        var backlogResult = await _projectRepository.GetBacklogIdsAsync(cancellationToken);
        if (backlogResult.IsOk)
        {
            foreach (var projectId in backlogResult.Value)
            {
                if (_inFlightTracker.TryMarkInFlight(projectId))
                {
                    await _discoveryQueue.EnqueueAsync(projectId, cancellationToken);
                    // Re-hydrated items are real backlog too, so the radar shows them as discovered.
                    _globalStatus.NotifyProjectDiscovered(projectId);
                }
            }

            _globalStatus.UpdateQueueCount(_discoveryQueue.Count);
        }

        // Cleanup: Prune very old backlog entries (e.g. > 30 days) to prevent bloat.
        _ = _projectRepository.CleanOldBacklogAsync(30, cancellationToken);

        for (var i = 0; i < WorkerCount; i++)
        {
            var worker = new EnrichmentWorker(
                i, _discoveryQueue, _enrichmentService, _inFlightTracker, _projectRepository, _globalStatus, _notificationDispatcher);
            _runningWorkers.Add(worker.RunAsync(cancellationToken));
        }

        return Result<bool>.Ok(true);
    }

    public Task StopAsync()
    {
        _discoveryQueue.Complete();
        return Task.WhenAll(_runningWorkers);
    }
}

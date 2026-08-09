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
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly List<Task> _runningWorkers = [];

    // Per system-components.md #6 (Worker Pool): `max_concurrent_detail_fetches` default 2-3.
    public int WorkerCount { get; set; } = 3;

    public WorkerPool(
        DiscoveryQueue discoveryQueue,
        IEnrichmentService enrichmentService,
        InFlightTracker inFlightTracker,
        IProjectRepository projectRepository,
        INotificationDispatcher notificationDispatcher)
    {
        _discoveryQueue = discoveryQueue;
        _enrichmentService = enrichmentService;
        _inFlightTracker = inFlightTracker;
        _projectRepository = projectRepository;
        _notificationDispatcher = notificationDispatcher;
    }

    public Task<Result<bool>> StartAsync(CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < WorkerCount; i++)
        {
            var worker = new EnrichmentWorker(
                i, _discoveryQueue, _enrichmentService, _inFlightTracker, _projectRepository, _notificationDispatcher);
            _runningWorkers.Add(worker.RunAsync(cancellationToken));
        }

        return Task.FromResult(Result<bool>.Ok(true));
    }

    public Task StopAsync()
    {
        _discoveryQueue.Complete();
        return Task.WhenAll(_runningWorkers);
    }
}

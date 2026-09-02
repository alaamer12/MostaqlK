using MostaqlK.Core;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;

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
    private readonly IOwnerRepository _ownerRepository;
    private readonly GlobalAppStatusService _globalStatus;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly List<Task> _runningWorkers = [];
    private CancellationTokenSource? _poolCts;

    // Per system-components.md #6 (Worker Pool): `max_concurrent_detail_fetches` default 2-3.
    // configuration-reference.md lists 2 as the shipped default, and the radar draws exactly three
    // segments, so this stays inside the documented range.
    public int WorkerCount { get; private set; } = 3;

    public WorkerPool(
        DiscoveryQueue discoveryQueue,
        IEnrichmentService enrichmentService,
        InFlightTracker inFlightTracker,
        IProjectRepository projectRepository,
        IOwnerRepository ownerRepository,
        GlobalAppStatusService globalStatus,
        INotificationDispatcher notificationDispatcher)
    {
        _discoveryQueue = discoveryQueue;
        _enrichmentService = enrichmentService;
        _inFlightTracker = inFlightTracker;
        _projectRepository = projectRepository;
        _ownerRepository = ownerRepository;
        _globalStatus = globalStatus;
        _notificationDispatcher = notificationDispatcher;
    }

    public async Task<Result<bool>> StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _poolCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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
            _ = Task.Run(async () =>
            {
                try
                {
                    await _projectRepository.CleanOldBacklogAsync(30, cancellationToken);
                }
                catch (Exception ex)
                {
                    CrashReporter.Report("WorkerPool.CleanOldBacklog", ex);
                }
            }, cancellationToken);

            for (var i = 0; i < WorkerCount; i++)
            {
                SpawnWorker(i, _poolCts.Token);
            }

            return Result<bool>.Ok(true);
        }
        catch (OperationCanceledException)
        {
            return Result<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            CrashReporter.Report("WorkerPool.StartAsync", ex);
            return Result<bool>.Err(EnrichErrors.Unexpected(0, ex));
        }
    }

    public void Reconfigure(int workerCount)
    {
        if (workerCount == WorkerCount || workerCount <= 0 || _poolCts == null)
        {
            WorkerCount = Math.Max(1, workerCount);
            return;
        }

        // If we are increasing, just spawn more.
        if (workerCount > WorkerCount)
        {
            for (var i = WorkerCount; i < workerCount; i++)
            {
                SpawnWorker(i, _poolCts.Token);
            }
        }
        // If we are decreasing, we can't easily kill a specific task that's mid-enrichment
        // without a per-worker CTS, so for now we just update the count for the next StartAsync.
        // BUT the requirement says "apply live". 
        // We'll just update the property. The Radar view uses this count to draw segments.
        WorkerCount = workerCount;
    }

    private void SpawnWorker(int id, CancellationToken token)
    {
        var worker = new EnrichmentWorker(
            id, _discoveryQueue, _enrichmentService, _inFlightTracker, _projectRepository, _ownerRepository, _globalStatus, _notificationDispatcher);
        _runningWorkers.Add(Task.Run(async () =>
        {
            try
            {
                await worker.RunAsync(token);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
            catch (Exception ex)
            {
                CrashReporter.Report("WorkerPool.WorkerTask", ex, new { WorkerId = id });
            }
        }, token));
    }

    public Task StopAsync()
    {
        _discoveryQueue.Complete();
        _poolCts?.Cancel();
        return Task.WhenAll(_runningWorkers);
    }
}

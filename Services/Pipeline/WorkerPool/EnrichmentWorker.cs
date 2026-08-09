using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Services.Pipeline.WorkerPool;

/// <summary>
/// A single long-lived loop that pulls project IDs off the <see cref="DiscoveryQueue"/>
/// and enriches them via <see cref="IEnrichmentService"/>, one at a time.
/// </summary>
public sealed class EnrichmentWorker
{
    private readonly int _workerId;
    private readonly DiscoveryQueue _discoveryQueue;
    private readonly IEnrichmentService _enrichmentService;
    private readonly InFlightTracker _inFlightTracker;

    public EnrichmentWorker(
        int workerId,
        DiscoveryQueue discoveryQueue,
        IEnrichmentService enrichmentService,
        InFlightTracker inFlightTracker)
    {
        _workerId = workerId;
        _discoveryQueue = discoveryQueue;
        _enrichmentService = enrichmentService;
        _inFlightTracker = inFlightTracker;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // TODO: loop over `_discoveryQueue.ReadAllAsync`, mark in-flight, enrich, mark complete.
        await foreach (var projectId in _discoveryQueue.ReadAllAsync(cancellationToken))
        {
            _ = projectId;
            throw new NotImplementedException();
        }
    }
}

using Microsoft.Maui.Storage;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Services.Pipeline.DiffEngine;
// The namespace MostaqlK.Services.Pipeline.WorkerPool shadows the class of the same name
// from inside this namespace, so the pool type needs an explicit alias.
using EnrichmentWorkerPool = MostaqlK.Services.Pipeline.WorkerPool.WorkerPool;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Abstract factory: given one source's descriptor + discovery/enrichment pair, assembles a
/// complete <see cref="SourcePipeline"/> with entirely private instances (queue, tracker,
/// rate budget, diff providers, poll loop, worker pool, status). Nothing here knows which
/// website it wired — every literal comes from the descriptor or the module services.
/// </summary>
public sealed class SourcePipelineFactory
{
    private readonly IProjectRepository _projectRepository;
    private readonly IOwnerRepository _ownerRepository;
    private readonly AssetDownloadService _assetDownloadService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly SourceRegistry _registry;

    public SourcePipelineFactory(
        IProjectRepository projectRepository,
        IOwnerRepository ownerRepository,
        AssetDownloadService assetDownloadService,
        INotificationDispatcher notificationDispatcher,
        SourceRegistry registry)
    {
        _projectRepository = projectRepository;
        _ownerRepository = ownerRepository;
        _assetDownloadService = assetDownloadService;
        _notificationDispatcher = notificationDispatcher;
        _registry = registry;
    }

    public SourcePipeline Create(ISourceDescriptor descriptor, ISourceDiscovery discovery, ISourceEnrichment enrichment)
    {
        var status = new SourcePipelineStatus(descriptor.Id, _registry);
        var queue = new DiscoveryQueue();
        var tracker = new InFlightTracker();

        // rpm/safe-requests are still global flat keys (per-source namespacing lands with the
        // settings step); the descriptor supplies this source's documented defaults.
        var limiter = new TokenBucketRateLimiter(
            Preferences.Get("settings_max_requests_per_minute", descriptor.DefaultRequestsPerMinute),
            Preferences.Get("settings_safe_requests", true));

        var enrichmentService = new EnrichmentService(enrichment, limiter, _assetDownloadService);
        var diffEngine = new DiffEngine.DiffEngine(new SqliteCommittedProvider(_projectRepository), new InFlightSetProvider(tracker));

        var poll = new PollService(discovery, diffEngine, queue, tracker, _projectRepository, status, limiter)
        {
            PollIntervalSeconds = descriptor.DefaultPollIntervalSeconds
        };

        var pool = new EnrichmentWorkerPool(queue, enrichmentService, tracker, _projectRepository, _ownerRepository, status, _notificationDispatcher);

        return new SourcePipeline(descriptor, status, queue, tracker, limiter, enrichmentService, poll, pool);
    }
}

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
    private readonly ISettingsRepository _settings;

    public SourcePipelineFactory(
        IProjectRepository projectRepository,
        IOwnerRepository ownerRepository,
        AssetDownloadService assetDownloadService,
        INotificationDispatcher notificationDispatcher,
        SourceRegistry registry,
        ISettingsRepository settings)
    {
        _projectRepository = projectRepository;
        _ownerRepository = ownerRepository;
        _assetDownloadService = assetDownloadService;
        _notificationDispatcher = notificationDispatcher;
        _registry = registry;
        _settings = settings;
    }

    public SourcePipeline Create(ISourceDescriptor descriptor, ISourceDiscovery discovery, ISourceEnrichment enrichment)
    {
        var status = new SourcePipelineStatus(descriptor.Id, _registry);
        var queue = new DiscoveryQueue();
        var tracker = new InFlightTracker();

        // Rate budget is a PER-PLATFORM fact (each pipeline owns a private limiter, so requests spent
        // on one site are irrelevant to another); the spacing policy is a SYSTEM fact (how strictly the
        // app behaves anywhere). See ISourceSettingsContribution's scope rulings.
        var limiter = new TokenBucketRateLimiter(
            _settings.Get(descriptor.Id, SettingsKeys.MaxRequestsPerMinute, descriptor.DefaultRequestsPerMinute),
            _settings.GetGlobal(SettingsKeys.SafeRequests, true));

        var enrichmentService = new EnrichmentService(enrichment, limiter, _assetDownloadService);
        var diffEngine = new DiffEngine.DiffEngine(new SqliteCommittedProvider(_projectRepository), new InFlightSetProvider(tracker));

        // PollService's own default (30s) is the untouched-install cadence and matches the system
        // row's DefaultLiteral; StartAll overwrites it with whatever is stored, floored by the
        // descriptor's MinSafePollIntervalSeconds.
        var poll = new PollService(discovery, diffEngine, queue, tracker, _projectRepository, status, limiter);

        var pool = new EnrichmentWorkerPool(queue, enrichmentService, tracker, _projectRepository, _ownerRepository, status, _notificationDispatcher);

        // Seed the pool from the SYSTEM row before StartAsync spawns it. WorkerCount defaulted to 3
        // and only ever changed when someone opened the settings page, so an install that never
        // touched Settings ran a concurrency the settings screen had never claimed.
        pool.Reconfigure(Math.Max(1, _settings.GetGlobal(
            SettingsKeys.MaxConcurrentDetailFetches, SourceCadence.DefaultParallelDetailFetches)));

        return new SourcePipeline(descriptor, status, queue, tracker, limiter, enrichmentService, poll, pool);
    }
}

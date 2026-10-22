namespace MostaqlK.Services.Pipeline;

using EnrichmentWorkerPool = MostaqlK.Services.Pipeline.WorkerPool.WorkerPool;

/// <summary>
/// DI-facing handle to whichever <see cref="SourcePipeline"/> the user is currently viewing.
/// The v3 rail morph switches the active source; consumers (Settings, feed, status bar, tray)
/// must always act on the ACTIVE pipeline's queue/limiter/workers, never on an instance bound
/// at startup. The live authority is <see cref="GlobalAppStatusService.ActiveSource"/> — updated
/// by the very same switch the rail performs — so this accessor introduces no second state:
/// it resolves <see cref="SourcePipelineOrchestrator.For"/> per call. A transient consumer
/// (the per-opening VMs) therefore re-points automatically on a morph, and singleton hosts
/// delegate here instead of capturing engine objects at construction (backend-ui-contract §2).
/// </summary>
public sealed class ActivePipeline
{
    private readonly SourcePipelineOrchestrator _orchestrator;
    private readonly GlobalAppStatusService _globalStatus;

    public ActivePipeline(SourcePipelineOrchestrator orchestrator, GlobalAppStatusService globalStatus)
    {
        _orchestrator = orchestrator;
        _globalStatus = globalStatus;
    }

    private SourcePipeline Current => _orchestrator.For(_globalStatus.ActiveSource);

    public IPollService Poll => Current.Poll;

    public DiscoveryQueue Queue => Current.Queue;

    public InFlightTracker Tracker => Current.Tracker;

    public TokenBucketRateLimiter Limiter => Current.Limiter;

    public IEnrichmentService Enrichment => Current.Enrichment;

    public EnrichmentWorkerPool Pool => Current.Pool;
}

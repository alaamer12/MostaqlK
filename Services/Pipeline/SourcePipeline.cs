using MostaqlK.Core.Sources;
// The namespace MostaqlK.Services.Pipeline.WorkerPool shadows the class of the same name
// from inside this namespace, so the pool type needs an explicit alias.
using EnrichmentWorkerPool = MostaqlK.Services.Pipeline.WorkerPool.WorkerPool;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// One source's complete discovery→enrichment engine: its own queue, in-flight tracker,
/// rate budget, diff providers, poll loop, worker pool, and status telemetry. The classes
/// inside are unchanged pipeline components — this only bundles the *instances*, so adding
/// a source never shares mutable pipeline state with another.
/// </summary>
public sealed class SourcePipeline
{
    public SourcePipeline(
        ISourceDescriptor descriptor,
        SourcePipelineStatus status,
        DiscoveryQueue queue,
        InFlightTracker tracker,
        TokenBucketRateLimiter limiter,
        IEnrichmentService enrichment,
        IPollService poll,
        EnrichmentWorkerPool pool)
    {
        Descriptor = descriptor;
        Status = status;
        Queue = queue;
        Tracker = tracker;
        Limiter = limiter;
        Enrichment = enrichment;
        Poll = poll;
        Pool = pool;
    }

    public ISourceDescriptor Descriptor { get; }

    public SourceId Source => Descriptor.Id;

    /// <summary>The radar/worker/queue telemetry this pipeline writes; projected by <see cref="GlobalAppStatusService"/>.</summary>
    public SourcePipelineStatus Status { get; }

    public DiscoveryQueue Queue { get; }

    public InFlightTracker Tracker { get; }

    public TokenBucketRateLimiter Limiter { get; }

    public IEnrichmentService Enrichment { get; }

    public IPollService Poll { get; }

    public EnrichmentWorkerPool Pool { get; }
}

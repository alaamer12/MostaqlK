using MostaqlK.Core.Sources;

namespace MostaqlK.Services.Pipeline;

/// <summary>What kind of work a queue item asks a source's pipeline to do.</summary>
public enum EnrichmentKind
{
    /// <summary>Fetch + parse one project's detail page (the v3 behaviour).</summary>
    Detail,

    /// <summary>Resolve every bidder of a project as one batch (v4 R4.13). Added when bidder
    /// capture lands; the queue element gains this axis so the batch rides the same per-source
    /// limiter and worker pool as ordinary detail enrichment rather than a parallel queue.</summary>
    BidderBatch,
}

/// <summary>
/// The shape <see cref="DiscoveryQueue"/> is intended to carry once v4 batch resolution exists:
/// a project plus the work to do on it. Today the queue element is a bare
/// <see cref="SourceProjectId"/> (detail work only); this type is the documented contract that
/// Phase 6 rewires the queue onto, so "which work" travels beside "which project" and the item's
/// composite SourceId still keeps two sources' numeric ids from aliasing.
/// </summary>
public readonly record struct EnrichmentWorkItem(SourceProjectId Project, EnrichmentKind Kind)
{
    public static EnrichmentWorkItem ForDetail(SourceProjectId project) => new(project, EnrichmentKind.Detail);
    public static EnrichmentWorkItem ForBidders(SourceProjectId project) => new(project, EnrichmentKind.BidderBatch);
}

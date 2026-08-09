namespace MostaqlK.Models;

/// <summary>
/// Enrichment lifecycle state of a discovered project: whether details have been
/// successfully fetched, are still queued/in-flight, or failed permanently.
/// </summary>
public enum EnrichmentStatus
{
    Pending,
    Enriched,
    Failed
}

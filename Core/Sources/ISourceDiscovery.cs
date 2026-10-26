using MostaqlK.Models;

namespace MostaqlK.Core.Sources;

/// <summary>
/// One website's feed reader: fetch + parse the project listing. The polling pipeline
/// depends exclusively on this (never on a concrete scraper), so a new source joins by
/// registering an implementation, not by changing shared code.
/// </summary>
public interface ISourceDiscovery
{
    public SourceId Source { get; }

    /// <summary><paramref name="queryParams"/> is source-shaped (its own URL query string); null/empty = default listing.</summary>
    public Task<Result<IReadOnlyList<ProjectSummary>>> DiscoverAsync(
        string? queryParams,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One website's detail reader: fetch + parse a single project page.
/// </summary>
public interface ISourceEnrichment
{
    public SourceId Source { get; }

    public Task<Result<ProjectDetails>> EnrichAsync(
        SourceProjectId projectId,
        CancellationToken cancellationToken = default);
}

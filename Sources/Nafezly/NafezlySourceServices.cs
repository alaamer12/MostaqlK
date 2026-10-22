using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Models;
using MostaqlK.Sources.Nafezly.Parsing;

namespace MostaqlK.Sources.Nafezly;

/// <summary>Parser seams so the fetch/parse split stays swappable inside the module (mirrors Mostaql).</summary>
public sealed class NafezlyListingStrategy : IListingStrategy
{
    public IReadOnlyList<ProjectSummary> ParseListing(string html) => NafezlyListingParser.Parse(html);
}

/// <inheritdoc cref="IDetailStrategy"/>
public sealed class NafezlyDetailStrategy : IDetailStrategy
{
    public ProjectDetails ParseDetail(long siteProjectId, string html) => NafezlyDetailParser.Parse(siteProjectId, html);
}

/// <inheritdoc cref="ISourceDiscovery"/>
public sealed class NafezlyDiscovery : ISourceDiscovery
{
    private readonly NafezlyFetcher _fetcher;
    // The listing/detail strategies are stateless module seams. They are constructed directly (NOT
    // resolved as the shared IListingStrategy/IDetailStrategy interface) so a second registered
    // source can never cross-wire this module's discovery onto another module's parser: with two
    // interface registrations DI's last-wins single injection would have handed Mostaql's discovery
    // Nafezly's strategy. Per-source isolation lives in the type, not the registration.
    private readonly NafezlyListingStrategy _strategy = new();

    public NafezlyDiscovery(NafezlyFetcher fetcher)
    {
        _fetcher = fetcher;
    }

    public SourceId Source => NafezlySource.Instance.Id;

    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Propagates fetch failure via htmlResult.Error")]
    [ErrorOutcome(ErrorOutcome.Handled, Label = "Parse failure wrapped as HttpErrors.ParseFailed")]
    public async Task<Result<IReadOnlyList<ProjectSummary>>> DiscoverAsync(
        string? queryParams,
        CancellationToken cancellationToken = default)
    {
        var url = NafezlySource.Instance.BuildListingUrl(queryParams);
        var htmlResult = await _fetcher.GetStringAsync(url, cancellationToken);
        if (htmlResult.IsError)
        {
            return Result<IReadOnlyList<ProjectSummary>>.Err(htmlResult.Error);
        }

        try
        {
            var summaries = _strategy.ParseListing(htmlResult.Value);
            // Stamp provenance so every downstream stage (diff, store, UI) knows the source
            // without the shared code ever naming it.
            foreach (var summary in summaries)
            {
                summary.Source = NafezlySource.Instance.Id;
            }

            return Result<IReadOnlyList<ProjectSummary>>.Ok(summaries);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ProjectSummary>>.Err(
                HttpErrors.ParseFailed(url, ex, NafezlySource.Instance.DisplayNameAr));
        }
    }
}

/// <inheritdoc cref="ISourceEnrichment"/>
public sealed class NafezlyEnrichment : ISourceEnrichment
{
    private readonly NafezlyFetcher _fetcher;
    // Stateless module seam, constructed directly (see NafezlyDiscovery for the isolation rationale).
    private readonly NafezlyDetailStrategy _strategy = new();

    public NafezlyEnrichment(NafezlyFetcher fetcher)
    {
        _fetcher = fetcher;
    }

    public SourceId Source => NafezlySource.Instance.Id;

    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Propagates fetch failure via htmlResult.Error")]
    [ErrorOutcome(ErrorOutcome.Handled, Label = "Parse failure wrapped as HttpErrors.ParseFailed")]
    public async Task<Result<ProjectDetails>> EnrichAsync(
        SourceProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        var url = NafezlySource.Instance.BuildProjectUrl(projectId.ProjectId);
        var htmlResult = await _fetcher.GetStringAsync(url, cancellationToken);
        if (htmlResult.IsError)
        {
            // A hard transport/gate failure (429-after-backoff, 403, timeout) is surfaced as an error:
            // the worker's capped retry then drains the backlog entry, so the in-memory queue is
            // never poisoned and the already-stored summary row is left untouched (not clobbered).
            return Result<ProjectDetails>.Err(htmlResult.Error);
        }

        try
        {
            var details = _strategy.ParseDetail(projectId.ProjectId, htmlResult.Value);
            details.Source = projectId.Source;
            details.Owner.Source = projectId.Source;
            details.Url = url;
            // A reachable-but-gated page (title present, core card facts absent) already carries
            // EnrichmentStatus.Failed from the parser and is returned as Ok so the summary is stored
            // with an honest failed state rather than retried forever.
            return Result<ProjectDetails>.Ok(details);
        }
        catch (Exception ex)
        {
            return Result<ProjectDetails>.Err(
                HttpErrors.ParseFailed(url, ex, NafezlySource.Instance.DisplayNameAr));
        }
    }
}

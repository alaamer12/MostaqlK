using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Models;
using MostaqlK.Sources.Mostaql.Parsing;

namespace MostaqlK.Sources.Mostaql;

/// <summary>Parser seams so the fetch/parse split stays swappable inside the module.</summary>
public sealed class MostaqlListingStrategy : IListingStrategy
{
    public IReadOnlyList<ProjectSummary> ParseListing(string html) => ListingParser.Parse(html);
}

/// <inheritdoc cref="IListingStrategy"/>
public sealed class MostaqlDiscovery : ISourceDiscovery
{
    private readonly MostaqlFetcher _fetcher;
    private readonly IListingStrategy _strategy;

    public MostaqlDiscovery(MostaqlFetcher fetcher, IListingStrategy strategy)
    {
        _fetcher = fetcher;
        _strategy = strategy;
    }

    public SourceId Source => MostaqlSource.Instance.Id;

    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Propagates fetch failure via htmlResult.Error")]
    [ErrorOutcome(ErrorOutcome.Handled, Label = "Parse failure wrapped as HttpErrors.ParseFailed")]
    public async Task<Result<IReadOnlyList<ProjectSummary>>> DiscoverAsync(
        string? queryParams,
        CancellationToken cancellationToken = default)
    {
        var url = MostaqlSource.Instance.BuildListingUrl(queryParams);
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
                summary.Source = MostaqlSource.Instance.Id;
            }

            return Result<IReadOnlyList<ProjectSummary>>.Ok(summaries);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<ProjectSummary>>.Err(
                HttpErrors.ParseFailed(url, ex, MostaqlSource.Instance.DisplayNameAr));
        }
    }
}

/// <inheritdoc cref="ISourceEnrichment"/>
public sealed class MostaqlEnrichment : ISourceEnrichment
{
    private readonly MostaqlFetcher _fetcher;
    private readonly IDetailStrategy _strategy;

    public MostaqlEnrichment(MostaqlFetcher fetcher, IDetailStrategy strategy)
    {
        _fetcher = fetcher;
        _strategy = strategy;
    }

    public SourceId Source => MostaqlSource.Instance.Id;

    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Propagates fetch failure via htmlResult.Error")]
    [ErrorOutcome(ErrorOutcome.Handled, Label = "Parse failure wrapped as HttpErrors.ParseFailed")]
    public async Task<Result<ProjectDetails>> EnrichAsync(
        SourceProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        var url = MostaqlSource.Instance.BuildProjectUrl(projectId.ProjectId);
        var htmlResult = await _fetcher.GetStringAsync(url, cancellationToken);
        if (htmlResult.IsError)
        {
            return Result<ProjectDetails>.Err(htmlResult.Error);
        }

        try
        {
            var details = _strategy.ParseDetail(projectId.ProjectId, htmlResult.Value);
            details.Source = projectId.Source;
            details.Owner.Source = projectId.Source;
            details.Url = url;
            return Result<ProjectDetails>.Ok(details);
        }
        catch (Exception ex)
        {
            return Result<ProjectDetails>.Err(
                HttpErrors.ParseFailed(url, ex, MostaqlSource.Instance.DisplayNameAr));
        }
    }
}

/// <summary>Wraps the static <see cref="DetailParser"/> behind <see cref="IDetailStrategy"/>.</summary>
public sealed class MostaqlDetailStrategy : IDetailStrategy
{
    public ProjectDetails ParseDetail(long siteProjectId, string html) => DetailParser.Parse(siteProjectId, html);
}

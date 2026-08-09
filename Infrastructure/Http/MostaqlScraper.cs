using MostaqlK.Core;
using MostaqlK.Infrastructure.Http.Parsers;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Http;

/// <inheritdoc cref="IProjectScraper"/>
public sealed class MostaqlScraper : IProjectScraper
{
    private const string ListingUrl = "https://mostaql.com/projects";

    private readonly HttpClient _httpClient;

    public MostaqlScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<Result<IReadOnlyList<ProjectSummary>>> FetchListingAsync(CancellationToken cancellationToken = default)
    {
        // TODO: GET `ListingUrl` via `_httpClient`, then parse with `ListingParser`.
        throw new NotImplementedException();
    }

    public Task<Result<ProjectDetails>> FetchProjectDetailsAsync(long projectId, CancellationToken cancellationToken = default)
    {
        // TODO: GET the project's detail URL via `_httpClient`, then parse with `DetailParser`.
        throw new NotImplementedException();
    }
}

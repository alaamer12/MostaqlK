using MostaqlK.Core;
using MostaqlK.Infrastructure.Http.Parsers;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Http;

/// <inheritdoc cref="IProjectScraper"/>
public sealed class MostaqlScraper : IProjectScraper
{
    private const string ListingUrl = "https://mostaql.com/projects";
    private const string DetailUrlFormat = "https://mostaql.com/project/{0}";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;

    public MostaqlScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Result<IReadOnlyList<ProjectSummary>>> FetchListingAsync(CancellationToken cancellationToken = default)
    {
        var htmlResult = await GetStringAsync(ListingUrl, cancellationToken);
        if (htmlResult.IsError)
        {
            return Result<IReadOnlyList<ProjectSummary>>.Err(htmlResult.Error);
        }

        try
        {
            var summaries = ListingParser.Parse(htmlResult.Value);
            return Result<IReadOnlyList<ProjectSummary>>.Ok(summaries);
        }
        catch (ParseException ex)
        {
            return Result<IReadOnlyList<ProjectSummary>>.Err(HttpErrors.ParseFailed(ListingUrl, ex));
        }
    }

    public async Task<Result<ProjectDetails>> FetchProjectDetailsAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var url = string.Format(DetailUrlFormat, projectId);
        var htmlResult = await GetStringAsync(url, cancellationToken);
        if (htmlResult.IsError)
        {
            return Result<ProjectDetails>.Err(htmlResult.Error);
        }

        try
        {
            var details = DetailParser.Parse(projectId, htmlResult.Value);
            return Result<ProjectDetails>.Ok(details);
        }
        catch (ParseException ex)
        {
            return Result<ProjectDetails>.Err(HttpErrors.ParseFailed(url, ex));
        }
    }

    private async Task<Result<string>> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            using var response = await _httpClient.GetAsync(url, timeoutCts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return Result<string>.Err(HttpErrors.NotFound(url));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result<string>.Err(HttpErrors.UnexpectedStatusCode(url, (int)response.StatusCode));
            }

            var html = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return Result<string>.Ok(html);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The linked timeout CTS fired, not the caller's own cancellation.
            return Result<string>.Err(HttpErrors.Timeout(url, new TimeoutException($"Request to '{url}' exceeded {RequestTimeout.TotalSeconds}s.")));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return Result<string>.Err(HttpErrors.RequestFailed(url, ex));
        }
        catch (Exception ex)
        {
            return Result<string>.Err(HttpErrors.Unexpected(url, ex));
        }
    }
}

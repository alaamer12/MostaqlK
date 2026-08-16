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

    /// <summary>
    /// Auth cookie header, resolved per request rather than cached, so a cookie the user uploads
    /// in Settings applies to the very next poll instead of only after an app restart (the
    /// resolution itself is an in-memory lookup in Release). Attaching it to the *page* fetch
    /// (not just to the file download) is what makes attachments usable at all: Mostaql renders
    /// anonymous visitors a "/register?..." stub in place of the real /file/{id}/... URL, so an
    /// unauthenticated scrape can only ever produce a manual-download placeholder.
    /// Null when no cookie is configured, in which case scraping behaves exactly as before.
    /// </summary>
    private static string? CookieHeader => CookieJar.Load();

    public MostaqlScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Propagates GetStringAsync failure via htmlResult.Error")]
    [ErrorOutcome(ErrorOutcome.Handled, Label = "Parse failure wrapped as HttpErrors.ParseFailed")]
    public async Task<Result<IReadOnlyList<ProjectSummary>>> FetchListingAsync(string? queryParams = null, CancellationToken cancellationToken = default)
    {
        var url = ListingUrl;
        if (!string.IsNullOrWhiteSpace(queryParams))
        {
            var normalized = queryParams.Trim();
            if (!normalized.StartsWith("?"))
            {
                normalized = "?" + normalized;
            }
            url += normalized;
        }

        var htmlResult = await GetStringAsync(url, cancellationToken);
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

    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Propagates GetStringAsync failure via htmlResult.Error")]
    [ErrorOutcome(ErrorOutcome.Handled, Label = "Parse failure wrapped as HttpErrors.ParseFailed")]
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
            details.Url = url;
            return Result<ProjectDetails>.Ok(details);
        }
        catch (ParseException ex)
        {
            return Result<ProjectDetails>.Err(HttpErrors.ParseFailed(url, ex));
        }
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Non-success/timeout/exception responses returned as Result<string>.Err")]
    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Caller-initiated cancellation rethrown, not swallowed")]
    private async Task<Result<string>> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (CookieHeader is { Length: > 0 } cookie)
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            response.EnsureSuccessStatusCode();
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

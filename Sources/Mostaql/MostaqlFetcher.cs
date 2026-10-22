using MostaqlK.Core;
using MostaqlK.Infrastructure.Http;

namespace MostaqlK.Sources.Mostaql;

/// <summary>
/// The module's single HTTP entry point: GETs a page with the shared client and the
/// Mostaql session cookie attached. Attaching the cookie to the <em>page</em> fetch
/// (not just the file download) is what makes attachments usable at all: Mostaql
/// renders anonymous visitors a "/register?..." stub in place of the real
/// /file/{id}/... URL, so an unauthenticated scrape can only ever produce a
/// manual-download placeholder. Null cookie → behaves exactly as an anonymous visit.
/// </summary>
public sealed class MostaqlFetcher
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly ICookieJar _cookieJar;

    public MostaqlFetcher(HttpClient httpClient, ICookieJarProvider cookieJarProvider)
    {
        _httpClient = httpClient;
        // The module reaches for its OWN source's jar, never a global one, so a second source's
        // session can never ride along on a Mostaql request.
        _cookieJar = cookieJarProvider.For(MostaqlSource.Instance.Id);
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Non-success/timeout/exception responses returned as Result<string>.Err")]
    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Caller-initiated cancellation rethrown, not swallowed")]
    public async Task<Result<string>> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var cookie = _cookieJar.Load();
            if (cookie is { Length: > 0 })
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
            return Result<string>.Err(HttpErrors.Timeout(url, new TimeoutException($"Request to '{url}' exceeded {RequestTimeout.TotalSeconds}s."), MostaqlSource.Instance.DisplayNameAr));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return Result<string>.Err(HttpErrors.RequestFailed(url, ex, MostaqlSource.Instance.DisplayNameAr));
        }
        catch (Exception ex)
        {
            return Result<string>.Err(HttpErrors.Unexpected(url, ex, MostaqlSource.Instance.DisplayNameAr));
        }
    }
}

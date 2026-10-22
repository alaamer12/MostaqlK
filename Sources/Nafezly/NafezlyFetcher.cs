using MostaqlK.Core;
using MostaqlK.Infrastructure.Http;

namespace MostaqlK.Sources.Nafezly;

/// <summary>
/// The nafezly module's single HTTP entry point. Unlike <c>MostaqlFetcher</c> — which injects the
/// app-wide shared <see cref="HttpClient"/> singleton (seeded from <c>SourceId.Default</c>) — this
/// fetcher builds its OWN client through <see cref="IHttpClientPolicyFactory"/> from the nafezly
/// descriptor, so nafezly's browser UA / Accept / Accept-Language never ride on the mostaql client
/// and vice-versa (the per-source client isolation the v3 contract §5 asks A3 to verify).
/// </summary>
/// <remarks>
/// Cloudflare reality (capture-manifest "Rate-limit truth"): an unpaced burst (> ~6 req/min) trips
/// a 429 / error 1015. The fetcher therefore honours <c>Retry-After</c> when present, otherwise
/// backs off exponentially with a cap, and gives up as a surfaced <see cref="DomainError"/> rather
/// than hammering. 403 (session/bot gate) is returned as an error the enrichment path turns into a
/// summary-only row — the queue is never poisoned by retrying here.
/// </remarks>
public sealed class NafezlyFetcher : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private const int Max429Retries = 3;
    private static readonly TimeSpan BackoffCap = TimeSpan.FromSeconds(45);

    private readonly HttpClient _httpClient;
    private readonly ICookieJar _cookieJar;

    public NafezlyFetcher(IHttpClientPolicyFactory httpClientPolicyFactory, ICookieJarProvider cookieJarProvider)
    {
        // The module builds a client from its OWN descriptor's headers …
        _httpClient = httpClientPolicyFactory.Create(NafezlySource.Instance);
        _httpClient.Timeout = RequestTimeout;
        // … and reaches only for its OWN source's cookie jar, so no other source's session leaks in.
        _cookieJar = cookieJarProvider.For(NafezlySource.Instance.Id);
    }

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Non-success/timeout/exception responses returned as Result<string>.Err")]
    [ErrorOutcome(ErrorOutcome.Rethrown, Label = "Caller-initiated cancellation rethrown, not swallowed")]
    public async Task<Result<string>> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        var siteName = NafezlySource.Instance.DisplayNameAr;

        for (var attempt = 0; ; attempt++)
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

                if ((int)response.StatusCode == 429 && attempt < Max429Retries)
                {
                    // Rate-limited: wait (Retry-After, else exponential) and retry within this call.
                    var delay = GetBackoffDelay(response, attempt);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                if ((int)response.StatusCode == 403)
                {
                    // Session/bot gate — do NOT retry; the caller degrades to a summary-only row.
                    return Result<string>.Err(HttpErrors.UnexpectedStatusCode(url, 403, siteName));
                }

                if ((int)response.StatusCode == 429)
                {
                    // Rate-limited and retries exhausted — surface, never loop forever.
                    return Result<string>.Err(HttpErrors.UnexpectedStatusCode(url, 429, siteName));
                }

                response.EnsureSuccessStatusCode();

                // Force UTF-8 regardless of any missing charset header (nafezly is a UTF-8 page).
                var bytes = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
                var html = System.Text.Encoding.UTF8.GetString(bytes);
                return Result<string>.Ok(html);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Result<string>.Err(HttpErrors.Timeout(
                    url, new TimeoutException($"Request to '{url}' exceeded {RequestTimeout.TotalSeconds}s."), siteName));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                return Result<string>.Err(HttpErrors.RequestFailed(url, ex, siteName));
            }
            catch (Exception ex)
            {
                return Result<string>.Err(HttpErrors.Unexpected(url, ex, siteName));
            }
        }
    }

    /// <summary>
    /// Retry delay for a 429: the server's <c>Retry-After</c> (seconds) when present and sane, else an
    /// exponential 5/10/20s ladder capped at <see cref="BackoffCap"/>.
    /// </summary>
    private static TimeSpan GetBackoffDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } retry && retry > TimeSpan.Zero)
        {
            return retry > BackoffCap ? BackoffCap : retry;
        }

        var seconds = Math.Min(BackoffCap.TotalSeconds, 5 * Math.Pow(2, attempt));
        return TimeSpan.FromSeconds(seconds);
    }

    public void Dispose() => _httpClient.Dispose();
}

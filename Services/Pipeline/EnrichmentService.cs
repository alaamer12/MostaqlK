using MostaqlK.Core;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Models;

namespace MostaqlK.Services.Pipeline;

/// <inheritdoc cref="IEnrichmentService"/>
public sealed class EnrichmentService : IEnrichmentService
{
    private readonly IProjectScraper _scraper;
    private readonly TokenBucketRateLimiter _rateLimiter;

    public EnrichmentService(IProjectScraper scraper, TokenBucketRateLimiter rateLimiter)
    {
        _scraper = scraper;
        _rateLimiter = rateLimiter;
    }

    public async Task<Result<ProjectDetails>> EnrichAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await _rateLimiter.WaitForTokenAsync(cancellationToken);
        return await _scraper.FetchProjectDetailsAsync(projectId, cancellationToken);
    }
}

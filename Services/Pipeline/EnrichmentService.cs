using MostaqlK.Core;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Models;

namespace MostaqlK.Services.Pipeline;

/// <inheritdoc cref="IEnrichmentService"/>
public sealed class EnrichmentService : IEnrichmentService
{
    private readonly IProjectScraper _scraper;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly AssetDownloadService _assetDownloadService;

    public bool DownloadAssets { get; set; }

    public EnrichmentService(IProjectScraper scraper, TokenBucketRateLimiter rateLimiter, AssetDownloadService assetDownloadService)
    {
        _scraper = scraper;
        _rateLimiter = rateLimiter;
        _assetDownloadService = assetDownloadService;
    }

    public async Task<Result<ProjectDetails>> EnrichAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await _rateLimiter.WaitForTokenAsync(cancellationToken);
        var result = await _scraper.FetchProjectDetailsAsync(projectId, cancellationToken);
        
        if (result.IsOk && DownloadAssets)
        {
            var details = result.Value;
            foreach (var asset in details.Attachments)
            {
                // We fire and forget or await? 
                // The issue description says: "زيد هذا من استهلاك القرص والوقت اللازم لكل عنصر في طابور الإثراء."
                // So it should be part of the enrichment time.
                await _assetDownloadService.ResolveAsync(asset, cancellationToken);
            }
        }

        return result;
    }
}

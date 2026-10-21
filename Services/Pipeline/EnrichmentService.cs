using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Models;

namespace MostaqlK.Services.Pipeline;

/// <inheritdoc cref="IEnrichmentService"/>
public sealed class EnrichmentService : IEnrichmentService
{
    private readonly ISourceEnrichment _enrichment;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly AssetDownloadService _assetDownloadService;

    public bool DownloadAssets { get; set; }

    public EnrichmentService(ISourceEnrichment enrichment, TokenBucketRateLimiter rateLimiter, AssetDownloadService assetDownloadService)
    {
        _enrichment = enrichment;
        _rateLimiter = rateLimiter;
        _assetDownloadService = assetDownloadService;
    }

    public async Task<Result<ProjectDetails>> EnrichAsync(SourceProjectId projectId, CancellationToken cancellationToken = default)
    {
        await _rateLimiter.WaitForTokenAsync(cancellationToken);
        var result = await _enrichment.EnrichAsync(projectId, cancellationToken);

        if (result.IsOk && DownloadAssets)
        {
            var details = result.Value;
            foreach (var asset in details.Attachments)
            {
                // We fire and forget or await? 
                // The issue description says: "زيد هذا من استهلاك القرص والوقت اللازم لكل عنصر في طابور الإثراء."
                // So it should be part of the enrichment time.
                // The project's own source selects which session/policy the download uses, so a
                // nafezly asset would use the nafezly jar — never this source's cookie.
                await _assetDownloadService.ResolveAsync(projectId.Source, asset, cancellationToken);
            }
        }

        return result;
    }
}

using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Models;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Fetches and parses a single project's detail page, turning a discovered project
/// into a fully populated <see cref="ProjectDetails"/>.
/// </summary>
public interface IEnrichmentService
{
    public bool DownloadAssets { get; set; }

    public Task<Result<ProjectDetails>> EnrichAsync(SourceProjectId projectId, CancellationToken cancellationToken = default);
}

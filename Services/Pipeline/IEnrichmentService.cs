using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Fetches and parses a single project's detail page, turning a discovered project ID
/// into a fully populated <see cref="ProjectDetails"/>.
/// </summary>
public interface IEnrichmentService
{
    Task<Result<ProjectDetails>> EnrichAsync(long projectId, CancellationToken cancellationToken = default);
}

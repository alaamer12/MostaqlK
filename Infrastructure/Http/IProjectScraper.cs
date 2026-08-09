using MostaqlK.Core;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Http;

/// <summary>
/// Raw HTTP access to the Mostaql website: fetching the listing feed and individual
/// project detail pages, without any parsing beyond returning HTML content.
/// </summary>
public interface IProjectScraper
{
    Task<Result<IReadOnlyList<ProjectSummary>>> FetchListingAsync(CancellationToken cancellationToken = default);

    Task<Result<ProjectDetails>> FetchProjectDetailsAsync(long projectId, CancellationToken cancellationToken = default);
}

using MostaqlK.Models;

namespace MostaqlK.Core.Sources;

/// <summary>
/// Parses one source's listing page into summaries. Strategy inside a source module —
/// shared code never sees it, only <see cref="ISourceDiscovery"/>.
/// </summary>
public interface IListingStrategy
{
    /// <summary>Throws <c>ParseException</c> when the page shape is unrecognized.</summary>
    IReadOnlyList<ProjectSummary> ParseListing(string html);
}

/// <summary>
/// Parses one source's project detail page. Strategy inside a source module —
/// shared code never sees it, only <see cref="ISourceEnrichment"/>.
/// </summary>
public interface IDetailStrategy
{
    /// <summary><paramref name="siteProjectId"/> is the site-local numeric id.</summary>
    ProjectDetails ParseDetail(long siteProjectId, string html);
}

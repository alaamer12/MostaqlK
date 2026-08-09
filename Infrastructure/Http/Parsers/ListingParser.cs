using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Http.Parsers;

/// <summary>
/// Parses the Mostaql projects listing page HTML into a collection of <see cref="ProjectSummary"/>.
/// </summary>
public static class ListingParser
{
    public static IReadOnlyList<ProjectSummary> Parse(string html)
    {
        // TODO: parse `html` (e.g. via HtmlAgilityPack/AngleSharp) into project summary cards.
        throw new ParseException("ListingParser.Parse is not yet implemented.");
    }
}

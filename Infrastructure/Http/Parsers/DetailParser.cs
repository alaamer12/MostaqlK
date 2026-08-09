using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Http.Parsers;

/// <summary>
/// Parses a Mostaql project detail page HTML into a fully populated <see cref="ProjectDetails"/>.
/// </summary>
public static class DetailParser
{
    public static ProjectDetails Parse(long projectId, string html)
    {
        // TODO: parse `html` into description, budget, delivery days, skills, owner, attachments.
        throw new ParseException("DetailParser.Parse is not yet implemented.");
    }
}

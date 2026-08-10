namespace MostaqlK.Infrastructure.Http.Parsers;

/// <summary>
/// Error factory for the <see cref="ParseException"/> raised by <see cref="ListingParser"/> and
/// <see cref="DetailParser"/> when the Mostaql page structure does not match the expected shape.
/// Per the module `Errors.cs` convention, no other file may construct <see cref="ParseException"/>
/// directly.
/// </summary>
internal static class ParseErrors
{
    internal static ParseException EmptyHtml(string parserName) =>
        new($"{parserName}.Parse received empty HTML.");

    internal static ParseException MissingTitle(long projectId) =>
        new($"DetailParser.Parse could not locate a title (h1) for project {projectId}.");

    internal static ParseException NoProjectRows() =>
        new("ListingParser.Parse could not locate any project rows (tr.project-row or div.project-card).");
}

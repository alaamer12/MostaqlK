namespace MostaqlK.Sources.Nafezly.Parsing;

/// <summary>
/// Error factory for the <see cref="NafezlyParseException"/> raised by <see cref="NafezlyListingParser"/>
/// and <see cref="NafezlyDetailParser"/> when a nafezly page does not match the expected shape. Per the
/// module <c>ParseErrors.cs</c> convention, no other file may construct <see cref="NafezlyParseException"/>
/// directly.
/// </summary>
internal static class NafezlyParseErrors
{
    internal static NafezlyParseException EmptyHtml(string parserName) =>
        new($"{parserName}.Parse received empty HTML.");

    internal static NafezlyParseException MissingTitle(long projectId) =>
        new($"NafezlyDetailParser.Parse could not locate a title for project {projectId}.");

    internal static NafezlyParseException NoProjectRows() =>
        new("NafezlyListingParser.Parse could not locate any project rows (div.project-box).");
}

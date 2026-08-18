namespace MostaqlK.Core.Formatting;

/// <summary>
/// Parses and formats the scraped <c>projects.skills</c> / <see cref="Models.ProjectSummary.SkillsText"/>
/// string into the presentation forms used by the project card: a capped tag list, a compact
/// chip line, and (via the view-model) bindable pill items. Storage keeps the source string
/// untouched; only the display layer splits/normalises it.
/// </summary>
public static class SkillsFormatter
{
    private static readonly char[] Separators = [',', '،', '|', ';'];

    /// <summary>Default cap matching the feed card's skill-chip row (at most 6 tags).</summary>
    public const int DefaultMaxTags = 6;

    /// <summary>
    /// Splits <paramref name="skillsText"/> on common separators (comma, Arabic comma, pipe,
    /// semicolon), trims entries, drops empties, and returns at most <paramref name="maxTags"/>
    /// tags. Returns an empty list when the input is null/whitespace.
    /// </summary>
    public static IReadOnlyList<string> ParseTags(string? skillsText, int maxTags = DefaultMaxTags)
    {
        if (string.IsNullOrWhiteSpace(skillsText))
        {
            return Array.Empty<string>();
        }

        return skillsText
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static s => s.Length > 0)
            .Take(maxTags)
            .ToArray();
    }

    /// <summary>
    /// Compact skill chip line for the feed card — each tag padded with spaces and joined by a
    /// triple-space gap (e.g. <c>"  CSS     HTML     JS  "</c>). Empty when there are no tags.
    /// </summary>
    public static string FormatDisplay(string? skillsText)
    {
        var tags = ParseTags(skillsText);
        return tags.Count == 0
            ? string.Empty
            : string.Join("   ", tags.Select(static t => $"  {t}  "));
    }
}

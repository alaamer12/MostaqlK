namespace MostaqlK.Core.Formatting;

/// <summary>
/// Authoritative single ground for string length truncation and ellipsis formatting.
/// </summary>
public static class TextTruncator
{
    public const string DefaultEllipsis = "...";

    /// <summary>
    /// Truncates <paramref name="text"/> so that the resulting string, including <paramref name="ellipsis"/>,
    /// does not exceed <paramref name="maxLength"/>.
    /// </summary>
    public static string Truncate(string? text, int maxLength, string ellipsis = DefaultEllipsis)
    {
        if (string.IsNullOrEmpty(text) || maxLength <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        ellipsis ??= string.Empty;
        if (maxLength <= ellipsis.Length)
        {
            return text[..maxLength];
        }

        var cutoff = maxLength - ellipsis.Length;
        return string.Concat(text.AsSpan(0, cutoff), ellipsis);
    }

    /// <summary>
    /// Truncates <paramref name="text"/> at the nearest whitespace boundary before <paramref name="maxLength"/>
    /// where possible, appending <paramref name="ellipsis"/>.
    /// </summary>
    public static string TruncateWords(string? text, int maxLength, string ellipsis = DefaultEllipsis)
    {
        if (string.IsNullOrEmpty(text) || maxLength <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        ellipsis ??= string.Empty;
        if (maxLength <= ellipsis.Length)
        {
            return text[..maxLength];
        }

        var maxContentLength = maxLength - ellipsis.Length;
        var lastSpace = text.LastIndexOfAny([' ', '\t', '\r', '\n'], maxContentLength);
        if (lastSpace > 0 && lastSpace >= maxContentLength / 2)
        {
            return string.Concat(text.AsSpan(0, lastSpace).TrimEnd(), ellipsis);
        }

        return string.Concat(text.AsSpan(0, maxContentLength), ellipsis);
    }
}

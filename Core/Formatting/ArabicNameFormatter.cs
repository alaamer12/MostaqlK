namespace MostaqlK.Core.Formatting;

/// <summary>
/// Authoritative single ground for Arabic client name formatting, "ال" definite article stripping,
/// and avatar initials extraction.
/// </summary>
public static class ArabicNameFormatter
{
    private const string DefiniteArticle = "ال";

    /// <summary>
    /// Strips the Arabic definite article ("ال") from the beginning of a word if the word contains
    /// letters beyond the article itself (e.g., "العتيبي" -> "عتيبي", "المطيري" -> "مطيري", "ال" -> "ال").
    /// </summary>
    public static string StripArticle(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return string.Empty;
        }

        var trimmed = word.Trim();
        return trimmed.Length > DefiniteArticle.Length && trimmed.StartsWith(DefiniteArticle, StringComparison.Ordinal)
            ? trimmed[DefiniteArticle.Length..]
            : trimmed;
    }

    /// <summary>
    /// Returns the initial letter of a single name word, skipping the Arabic definite article "ال"
    /// when another letter follows it (e.g. "العتيبي" -> "ع", "أحمد" -> "أ").
    /// </summary>
    public static string GetFirstLetter(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return string.Empty;
        }

        var stripped = StripArticle(word);
        return stripped.Length > 0 ? stripped[..1] : word.Trim()[..1];
    }

    /// <summary>
    /// Extracts avatar initials for a client name:
    /// - Multi-word name: first letter of first word + first letter of last word (skipping "ال"), e.g. "أحمد العتيبي" -> "أع", "سارة المطيري" -> "سم".
    /// - Single-word name: first 2 letters if length >= 2 (e.g. "عميل" -> "عم"), otherwise the word itself.
    /// - Null/empty/whitespace: returns <paramref name="fallback"/> ("؟").
    /// </summary>
    public static string GetInitials(string? name, string fallback = "؟")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var parts = name.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return fallback;
        }

        if (parts.Length > 1)
        {
            var first = GetFirstLetter(parts[0]);
            var last = GetFirstLetter(parts[^1]);
            return string.Concat(first, last);
        }

        var single = parts[0].Trim();
        return single.Length >= 2 ? single[..2] : single;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using MostaqlK.Core.Utilities;

namespace MostaqlK.Core.Formatting;

/// <summary>
/// Authoritative single ground for parsing and canonical Arabic pluralization of proposal counts.
/// Handles singular, dual, and plural forms ("عرض واحد", "عرضان", "3-10 عروض", "11+ عرضاً").
/// </summary>
public static class ArabicProposalParser
{
    private static readonly Regex DigitRegex = new(@"\d+", RegexOptions.Compiled);

    /// <summary>
    /// Formats a proposal count into canonical Arabic wording:
    /// 0 -> "0 عرض"
    /// 1 -> "عرض واحد"
    /// 2 -> "عرضان"
    /// 3..10 -> "{count} عروض"
    /// 11+ -> "{count} عرضاً"
    /// </summary>
    public static string Format(int count)
    {
        var num = count.ToString(CultureInfo.InvariantCulture);
        return count switch
        {
            <= 0 => "0 عرض",
            1 => "عرض واحد",
            2 => "عرضان",
            >= 3 and <= 10 => $"{num} عروض",
            _ => $"{num} عرضاً",
        };
    }

    /// <summary>
    /// Parses an Arabic proposal count string into a numeric value and the original text.
    /// Handles cases like "عرض واحد", "عرضان", "3 عروض", "أضف أول عرض".
    /// </summary>
    public static (int Number, string Text) Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (0, string.Empty);
        }

        // 0. Clean input from HTML tags and common surrounding symbols/quotes that might
        // leak from attribute-heavy or redesigned markup.
        var text = StringNormalization.CleanHtml(input);

        var normalizedText = StringNormalization.NormalizeLabel(text);
        var asciiText = StringNormalization.ToAsciiDigits(normalizedText);

        // 1. Check for specific non-numeric markers
        if (normalizedText.Contains("اضف اول عرض"))
        {
            return (0, text);
        }

        // 2. Check for singular/dual words
        if (normalizedText.Contains("عرض واحد") || normalizedText == "عرض")
        {
            return (1, text);
        }

        if (normalizedText.Contains("عرضان") || normalizedText.Contains("عرضين"))
        {
            return (2, text);
        }

        // 3. Extract digits for cases like "5 عروض" or "10 عرض"
        var match = DigitRegex.Match(asciiText);
        if (match.Success && int.TryParse(match.Value, out var number))
        {
            return (number, text);
        }

        // 4. Last resort: if it contains "عرض" but no digits/markers, it might be 1?
        // But Mostaql is usually explicit. 
        if (text.Contains("عرض"))
        {
             // If we didn't match digits, and it's not "واحد" or dual, it might just be "عرض" (1)
             // or some other form. Let's be conservative.
             return (0, text);
        }

        return (0, text);
    }
}

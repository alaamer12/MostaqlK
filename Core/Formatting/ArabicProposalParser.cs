using System.Text.RegularExpressions;

namespace MostaqlK.Core.Formatting;

/// <summary>
/// Parser for Mostaql proposal counts, handling Arabic singular, dual, and plural forms.
/// </summary>
public static class ArabicProposalParser
{
    private static readonly Regex DigitRegex = new(@"\d+", RegexOptions.Compiled);

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

        var text = input.Trim();
        
        // 1. Check for specific non-numeric markers
        if (text.Contains("أضف أول عرض"))
        {
            return (0, text);
        }

        // 2. Check for singular/dual words
        if (text.Contains("عرض واحد"))
        {
            return (1, text);
        }

        if (text.Contains("عرضان") || text.Contains("عرضين"))
        {
            return (2, text);
        }

        // 3. Extract digits for cases like "5 عروض" or "10 عرض"
        var match = DigitRegex.Match(text);
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

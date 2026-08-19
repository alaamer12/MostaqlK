using System.Globalization;
using System.Text.RegularExpressions;
using MostaqlK.Core.Utilities;

namespace MostaqlK.Core.Formatting;

/// <summary>
/// Authoritative single ground for Arabic relative-time and duration formatting and parsing.
/// Used by project cards, scrapers, and detail views ("منذ 3 دقائق", "7 أيام").
/// </summary>
public static class ArabicRelativeTime
{
    private static readonly Regex DigitRegex = new(@"\d+", RegexOptions.Compiled);

    /// <summary>Formats "time since <paramref name="timestamp"/>" the way projects.html words it.</summary>
    public static string Since(DateTimeOffset timestamp, DateTimeOffset? now = null)
    {
        var (number, text) = GetRelative(timestamp, now);
        return text;
    }

    /// <summary>Calculates both the numeric component and the full Arabic string for a relative time.</summary>
    public static (int Number, string Text) GetRelative(DateTimeOffset timestamp, DateTimeOffset? now = null)
    {
        var elapsed = (now ?? DateTimeOffset.UtcNow) - timestamp;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return (0, "منذ لحظات");
        }

        if (elapsed.TotalHours < 1)
        {
            var count = (int)elapsed.TotalMinutes;
            return (count, $"منذ {Count(count, "دقيقة", "دقيقتان", "دقائق", "دقيقة")}");
        }

        if (elapsed.TotalDays < 1)
        {
            var count = (int)elapsed.TotalHours;
            return (count, $"منذ {Count(count, "ساعة", "ساعتان", "ساعات", "ساعة")}");
        }

        var days = (int)elapsed.TotalDays;
        return (days, $"منذ {Days(days)}");
    }

    /// <summary>Formats a day count ("20 يوم", "7 أيام") the way the stats row words it.</summary>
    public static string Days(int days) => Count(days, "يوم واحد", "يومان", "أيام", "يوم");

    /// <summary>
    /// Parses the integer number from an Arabic relative time string (e.g. "منذ 7 دقائق" -> 7,
    /// "منذ ساعتين" -> 2, "منذ يوم" -> 1, "منذ لحظات" -> 0).
    /// Handles Arabic-Indic numerals, dual forms ("دقيقتين", "ساعتين", "يومين"), and singular words.
    /// </summary>
    public static int ParseRelativeNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var cleaned = StringNormalization.CleanHtml(text);
        var ascii = StringNormalization.ToAsciiDigits(cleaned);

        // 1. Check for explicit numbers first (e.g. "منذ 7 دقائق", "منذ ١٥ يوما")
        var match = DigitRegex.Match(ascii);
        if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNumber))
        {
            return parsedNumber;
        }

        var norm = StringNormalization.NormalizeLabel(cleaned);

        // 2. Check for "لحظات" (moments ago -> 0)
        if (norm.Contains("لحظات"))
        {
            return 0;
        }

        // 3. Check for dual forms ("دقيقتان", "دقيقتين", "ساعتان", "ساعتين", "يومان", "يومين", "شهران", "شهرين", "سنتان", "سنتين", "اسبوعان", "اسبوعين")
        if (norm.Contains("دقيقتان") || norm.Contains("دقيقتين") ||
            norm.Contains("ساعتان") || norm.Contains("ساعتين") ||
            norm.Contains("يومان") || norm.Contains("يومين") ||
            norm.Contains("شهران") || norm.Contains("شهرين") ||
            norm.Contains("سنتان") || norm.Contains("سنتين") ||
            norm.Contains("اسبوعان") || norm.Contains("اسبوعين"))
        {
            return 2;
        }

        // 4. Check for singular forms (1)
        if (norm.Contains("دقيقه") || norm.Contains("دقيقة") ||
            norm.Contains("ساعه") || norm.Contains("ساعة") ||
            norm.Contains("يوم") ||
            norm.Contains("شهر") ||
            norm.Contains("سنه") || norm.Contains("سنة") || norm.Contains("عام") ||
            norm.Contains("اسبوع") || norm.Contains("أسبوع"))
        {
            // Verify it is not a plural
            if (!norm.Contains("دقائق") && !norm.Contains("ساعات") &&
                !norm.Contains("ايام") && !norm.Contains("أيام") &&
                !norm.Contains("اشهر") && !norm.Contains("أشهر") && !norm.Contains("شهور") &&
                !norm.Contains("سنوات") && !norm.Contains("اعوام") && !norm.Contains("أعوام") &&
                !norm.Contains("اسابيع") && !norm.Contains("أسابيع"))
            {
                return 1;
            }
        }

        return 0;
    }

    private static string Count(int value, string one, string two, string few, string many)
    {
        var number = value.ToString(CultureInfo.InvariantCulture);
        return value switch
        {
            1 => one,
            2 => two,
            >= 3 and <= 10 => $"{number} {few}",
            _ => $"{number} {many}",
        };
    }
}

using System.Globalization;

namespace MostaqlK.Core.Formatting;

/// <summary>
/// Arabic relative-time and day-count wording used by the project cards ("منذ 3 دقائق", "7 أيام").
/// The scraper stores the source relative string in <c>projects.posted_relative</c>; when that
/// column is empty this rebuilds an equivalent phrase from the absolute discovery timestamp so
/// the card never falls back to a bare placeholder.
/// </summary>
public static class ArabicRelativeTime
{
    /// <summary>Formats "time since <paramref name="timestamp"/>" the way projects.html words it.</summary>
    public static string Since(DateTimeOffset timestamp, DateTimeOffset? now = null)
    {
        var elapsed = (now ?? DateTimeOffset.UtcNow) - timestamp;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return "منذ لحظات";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"منذ {Count((int)elapsed.TotalMinutes, "دقيقة", "دقيقتان", "دقائق", "دقيقة")}";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"منذ {Count((int)elapsed.TotalHours, "ساعة", "ساعتان", "ساعات", "ساعة")}";
        }

        return $"منذ {Days((int)elapsed.TotalDays)}";
    }

    /// <summary>Formats a day count ("20 يوم", "7 أيام") the way the stats row words it.</summary>
    public static string Days(int days) => Count(days, "يوم واحد", "يومان", "أيام", "يوم");

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

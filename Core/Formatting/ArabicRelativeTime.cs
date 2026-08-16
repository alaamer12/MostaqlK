using System.Globalization;

namespace MostaqlK.Core.Formatting;

/// <summary>
/// Arabic relative-time and day-count wording used by the project cards ("منذ 3 دقائق", "7 أيام").
/// The scraper stores the numeric value and formatted string in <c>projects.publish_time_number</c>
/// and <c>projects.publish_time_text</c>. The <c>PublishedTimeUpdateService</c> periodically
/// rebuilds these from the absolute discovery timestamp so the feed stays live.
/// </summary>
public static class ArabicRelativeTime
{
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

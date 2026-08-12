namespace MostaqlK.Core.Formatting;

/// <summary>
/// The single source of truth for the "آخر فحص" wording. It used to be written three times - once
/// in the feed view model for the footer, once in the radar tooltip and once in the pipeline
/// dashboard - each with its own phrasing and its own idea of when the last scan happened, which is
/// how the footer could claim "منذ دقيقة" while the header said the poll interval is 30 seconds.
/// Every surface now formats through here and reads the same timestamp
/// (<c>GlobalAppStatusService.LastScanCompletedAt</c>, written by the poll service on every cycle).
/// </summary>
public static class LastScanText
{
    /// <summary>The label prefix, kept here so no caller re-types it.</summary>
    public const string Label = "آخر فحص";

    /// <summary>Wording used before the first scan of the session completes.</summary>
    public const string Never = "لم يتم الفحص بعد";

    /// <summary>
    /// "منذ لحظات" / "منذ 42 ثانية" / "منذ 3 دقائق" - sub-minute precision matters here because the
    /// poll interval is measured in seconds, so falling straight to minutes reads as a stalled scan.
    /// </summary>
    public static string Elapsed(DateTimeOffset? lastScan, DateTimeOffset? now = null)
    {
        if (lastScan is null)
        {
            return Never;
        }

        var elapsed = (now ?? DateTimeOffset.UtcNow) - lastScan.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalSeconds < 5)
        {
            return "منذ لحظات";
        }

        if (elapsed.TotalMinutes < 1)
        {
            return $"منذ {Math.Floor(elapsed.TotalSeconds)} ثانية";
        }

        return ArabicRelativeTime.Since(lastScan.Value, now);
    }

    /// <summary>The full labelled line: "آخر فحص: منذ لحظات".</summary>
    public static string Labelled(DateTimeOffset? lastScan, DateTimeOffset? now = null) =>
        $"{Label}: {Elapsed(lastScan, now)}";
}

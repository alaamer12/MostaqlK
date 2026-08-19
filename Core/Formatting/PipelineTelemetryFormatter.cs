using System.Globalization;

namespace MostaqlK.Core.Formatting;

/// <summary>
/// Authoritative single ground for formatting pipeline worker states, elapsed times, and telemetry metrics.
/// </summary>
public static class PipelineTelemetryFormatter
{
    /// <summary>
    /// Formats a worker state string name into its canonical Arabic presentation text:
    /// "Processing" -> "يعالج"
    /// "Completed" -> "مكتمل"
    /// "Error" -> "خطأ"
    /// "Idle" / other -> "خامل"
    /// </summary>
    public static string FormatWorkerState(string? state) => state?.Trim() switch
    {
        "Processing" or "processing" or "يعالج" => "يعالج",
        "Completed" or "completed" or "مكتمل" => "مكتمل",
        "Error" or "error" or "خطأ" => "خطأ",
        _ => "خامل",
    };

    /// <summary>
    /// Formats a worker state enum value into its canonical Arabic presentation text.
    /// </summary>
    public static string FormatWorkerState<TEnum>(TEnum state) where TEnum : struct, Enum =>
        FormatWorkerState(state.ToString());

    /// <summary>
    /// Formats elapsed seconds to 1 decimal place (e.g. "12.4") or whole integer if >= 100 (e.g. "105").
    /// </summary>
    public static string FormatSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        return seconds.ToString(seconds >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats elapsed seconds with Arabic seconds suffix (e.g. "12.4 ث").
    /// </summary>
    public static string FormatSecondsSuffix(double seconds) =>
        $"{FormatSeconds(seconds)} ث";
}

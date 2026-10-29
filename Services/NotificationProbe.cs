using MostaqlK.Core.Sources;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Services;

/// <summary>
/// A dev-only launch flag that pushes one synthetic project through the app's real notification path.
/// <para>
/// Why this exists when <c>tools/ToastLab</c> already fires toasts: ToastLab links the same branding
/// code but supplies its own cache directory and content root as arguments. The one thing it therefore
/// cannot test is the app resolving those paths for itself — a wrong path inside the host looks exactly
/// like "branding works in the harness, nothing on screen". This flag exercises
/// <see cref="INotificationDispatcher"/>, <see cref="NotificationGrouper"/> and
/// <see cref="MostaqlK.Infrastructure.Notifications.ToastBrandingResolver"/> as the running app uses
/// them, deterministically, without waiting for a real project to be discovered on a live site.
/// </para>
/// <para>
/// It is inert outside a Debug build — <see cref="IsRequested"/> is the single gate, so there is no
/// second place for the condition to be forgotten. The synthetic project touches no database: the
/// dispatcher only buffers, counts the badge and appends in-memory history.
/// </para>
/// </summary>
public static class NotificationProbe
{
    public const string Argument = "--test-toast";

    public static bool IsRequested(string[] args) =>
#if DEBUG
        args is not null && Array.Exists(args, a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase));
#else
        false;
#endif

    /// <summary>
    /// Which source to pretend produced it: <c>--test-toast nafezly</c>, or the source the app is
    /// currently projecting when no name follows the flag.
    /// </summary>
    public static SourceId RequestedSource(string[] args, SourceId activeSource)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], Argument, StringComparison.OrdinalIgnoreCase)
                && SourceId.TryFrom(args[i + 1], out var named))
            {
                return named;
            }
        }

        return activeSource;
    }

    /// <summary>
    /// Sends one synthetic project. Returns false (and logs why) rather than throwing: a probe that
    /// faults must not take the app down with it.
    /// </summary>
    public static async Task<bool> RunAsync(
        INotificationDispatcher? dispatcher,
        NotificationGrouper? grouper,
        ISourceDescriptor? descriptor,
        SourceId source,
        CancellationToken cancellationToken = default)
    {
        if (dispatcher is null)
        {
            InteractionLogger.Mark("NotificationProbe.Run", "B", "no-dispatcher");
            return false;
        }

        var site = descriptor?.Domain ?? source.Value;
        var project = new ProjectSummary
        {
            Source = source,
            ProjectId = 1,
            Title = "إشعار تجريبي — تحقق من الشعار والنطاق العلوي",
            Url = $"https://{site}/project/1",
            ClientName = "ToastLab",
            Description = "مشروع اصطناعي يمر بمسار الإشعارات الحقيقي داخل التطبيق للتأكد من دقة المسارات.",
        };

        try
        {
            // The two branches stay apart because Result throws when Error is read on an ok value.
            var result = await dispatcher.NotifyNewProjectsAsync([project], cancellationToken);
            if (result.IsOk)
            {
                // Grouping is on by default and its mode may wait for the minute boundary, which would
                // make a dev probe a minute-long wait. FlushNow is the same path the app shutdown uses,
                // so nothing is bypassed — only the clock is.
                grouper?.FlushNow();
                InteractionLogger.Mark("NotificationProbe.Run", "A", new { Source = source.Value });
                return true;
            }

            InteractionLogger.Mark("NotificationProbe.Run", "B", new { Source = source.Value, Error = result.Error.ToString() });
            return false;
        }
        catch (Exception ex)
        {
            InteractionLogger.Fault("NotificationProbe.Run", ex, new { Source = source.Value });
            return false;
        }
    }
}

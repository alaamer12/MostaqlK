using System.Diagnostics;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// Shared "open URL in default browser" helper used by both toast variations' "عرض على مستقل"
/// button handlers (<see cref="ToastActivator"/> for <see cref="WinRtVariation"/>,
/// <see cref="WinAppSdkVariation.OnNotificationInvoked"/> for the modern SDK path).
/// <para>
/// Both variations previously delegated the actual browser launch to the OS itself
/// (<c>activationType='protocol'</c> / <c>SetInvokeUri</c>): if that silently failed, nothing in
/// <see cref="InteractionLogger"/> would ever record it, making "the button does not work" reports
/// impossible to trace. Routing the click through our own COM/event activation and launching the
/// URL here via <see cref="Process.Start"/> makes every attempt (and any failure) fully logged.
/// </para>
/// </summary>
public static class NotificationUrlLauncher
{
    /// <summary>
    /// Opens <paramref name="url"/> in the system's default browser, logging the attempt and its
    /// outcome under <paramref name="checkpoint"/> so a "button does not work" report can be traced
    /// end-to-end in <c>interaction-log.txt</c>.
    /// </summary>
    public static void OpenUrl(string url, string checkpoint)
    {
        InteractionLogger.Mark($"{checkpoint}.OpenUrl", "A", new { Url = url });

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            InteractionLogger.Mark($"{checkpoint}.OpenUrl", "B", new { Reason = "invalid-url", Url = url });
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            InteractionLogger.Mark($"{checkpoint}.OpenUrl", "C", new { Reason = "process-started", Url = url });
        }
        catch (Exception ex)
        {
            InteractionLogger.Fault($"{checkpoint}.OpenUrl", ex, new { Url = url });
        }
    }
}

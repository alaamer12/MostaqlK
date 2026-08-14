using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Services;

/// <summary>
/// Tracks the high-level lifecycle state of the application, particularly whether it's
/// currently running in the background (minimized to tray) or visible in the foreground.
/// </summary>
public sealed class AppLifecycleService
{
    private bool _isInBackground;
    private bool _isReadyToNotify;

    /// <summary>
    /// Gets whether the application window is currently hidden (minimized to tray).
    /// </summary>
    public bool IsInBackground
    {
        get => _isInBackground;
        set
        {
            if (_isInBackground == value) return;
            _isInBackground = value;
            InteractionLogger.Mark("AppLifecycle.IsInBackgroundChanged", "A", new { IsInBackground = value });
        }
    }

    /// <summary>
    /// Gets whether the application has finished its startup sequence and is ready to
    /// emit user-facing notifications. This prevents "stale" or early notifications
    /// during the initial backlog poll.
    /// </summary>
    public bool IsReadyToNotify
    {
        get => _isReadyToNotify;
        set
        {
            if (_isReadyToNotify == value) return;
            _isReadyToNotify = value;
            InteractionLogger.Mark("AppLifecycle.IsReadyToNotifyChanged", "A", new { IsReadyToNotify = value });
        }
    }
}

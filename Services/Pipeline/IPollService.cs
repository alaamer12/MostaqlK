using MostaqlK.Core;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Runs the periodic listing poll: fetches the current project listing page(s), diffs
/// them against known state, and enqueues genuinely new project IDs for enrichment.
/// </summary>
public interface IPollService
{
    /// <summary>Starts the periodic polling loop on the configured interval.</summary>
    Task<Result<bool>> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the periodic polling loop.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs a single poll cycle immediately, outside of the regular interval.</summary>
    Task<Result<int>> PollOnceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Poll interval in seconds. Settable at runtime (see <c>SettingsViewModel</c>).
    /// </summary>
    int PollIntervalSeconds { get; set; }

    /// <summary>Current observable status, mirrored to the tray icon (see <c>TrayIconService</c>).</summary>
    PollServiceStatus Status { get; }

    /// <summary>Raised whenever <see cref="Status"/> changes, so the tray icon can react live.</summary>
    event Action<PollServiceStatus>? StatusChanged;

    /// <summary>Whether the periodic loop is currently paused (manually, via the tray icon).</summary>
    bool IsPaused { get; }

    /// <summary>Toggles the paused flag (wired to the tray icon's "Pause / Resume" menu entry).</summary>
    void SetPaused(bool paused);

    /// <summary>Forces an immediate poll cycle outside of the regular timer, without waiting for the next tick.</summary>
    void RequestCheckNow();
}

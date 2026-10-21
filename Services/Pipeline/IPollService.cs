using MostaqlK.Core;

namespace MostaqlK.Services.Pipeline;

/// <summary>
/// Runs the periodic listing poll: fetches the current project listing page(s), diffs
/// them against known state, and enqueues genuinely new project IDs for enrichment.
/// </summary>
public interface IPollService
{
    /// <summary>Starts the periodic polling loop on the configured interval.</summary>
    public Task<Result<bool>> StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the periodic polling loop.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs a single poll cycle immediately, outside of the regular interval.</summary>
    public Task<Result<int>> PollOnceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Poll interval in seconds. Settable at runtime (see <c>SettingsViewModel</c>).
    /// </summary>
    public int PollIntervalSeconds { get; set; }

    /// <summary>Current observable status, mirrored to the tray icon (see <c>TrayIconService</c>).</summary>
    public PollServiceStatus Status { get; }

    /// <summary>Raised whenever <see cref="Status"/> changes, so the tray icon can react live.</summary>
    public event Action<PollServiceStatus>? StatusChanged;

    /// <summary>Whether the periodic loop is currently paused (manually, via the tray icon).</summary>
    public bool IsPaused { get; }

    /// <summary>Toggles the paused flag (wired to the tray icon's "Pause / Resume" menu entry).</summary>
    public void SetPaused(bool paused);

    /// <summary>Forces an immediate poll cycle outside of the regular timer, without waiting for the next tick.</summary>
    public void RequestCheckNow();

    /// <summary>
    /// Optional query parameters (e.g. category=development&amp;sort=latest) appended to
    /// the polled source's listing URL. Settable at runtime; source-shaped, never interpreted here.
    /// </summary>
    public string QueryParams { get; set; }
}

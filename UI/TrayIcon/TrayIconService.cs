namespace MostaqlK.UI.TrayIcon;

/// <summary>
/// The always-present system tray entry point. Reflects the pipeline's current health/activity
/// state and gives the user quick access to common actions without opening the main window.
/// See system-components.md § 13.1 Tray Icon.
/// </summary>
public enum TrayIconState
{
    /// <summary>No active polling or backlog work; pipeline is quiescent.</summary>
    Idle,

    /// <summary>A poll cycle is currently in progress.</summary>
    Polling,

    /// <summary>Workers are draining a backlog of enqueued (unseen) projects.</summary>
    BacklogDraining,

    /// <summary>The last poll or enrichment attempt failed.</summary>
    Error
}

/// <summary>
/// A single entry in the tray icon's right-click menu.
/// </summary>
/// <param name="Label">Display text for the menu entry.</param>
/// <param name="Command">Action invoked when the entry is activated.</param>
public sealed record TrayMenuItem(string Label, Action Command);

/// <summary>
/// Stub service for the system tray icon: current state + right-click menu items.
/// TODO: wire this up to the actual native tray icon (WinUI <c>NotifyIcon</c> /
/// CommunityToolkit.Maui tray support) once the platform hosting shell is in place.
/// </summary>
public class TrayIconService
{
    /// <summary>Current icon state, updated by the Poll Service / Worker Pool.</summary>
    public TrayIconState State { get; private set; } = TrayIconState.Idle;

    /// <summary>
    /// The right-click menu, in display order: Open window, Pause/Resume polling, Check now,
    /// Recent notifications, Settings, Quit.
    /// </summary>
    public List<TrayMenuItem> MenuItems { get; } = new();

    public TrayIconService()
    {
        MenuItems.Add(new TrayMenuItem("Open", OnOpen));
        MenuItems.Add(new TrayMenuItem("Pause / Resume", OnPauseResume));
        MenuItems.Add(new TrayMenuItem("Check now", OnCheckNow));
        MenuItems.Add(new TrayMenuItem("Recent notifications", OnRecentNotifications));
        MenuItems.Add(new TrayMenuItem("Settings", OnSettings));
        MenuItems.Add(new TrayMenuItem("Quit", OnQuit));
    }

    /// <summary>
    /// Updates the tray icon's reflected state. Called by the Poll Service / Worker Pool as the
    /// pipeline transitions between idle, polling, backlog-draining, and error states.
    /// </summary>
    public void SetState(TrayIconState state)
    {
        State = state;
        // TODO: push the new icon glyph to the native tray icon handle.
    }

    private static void OnOpen()
    {
        // TODO: show the main window (does not exit the process on close, per spec).
    }

    private static void OnPauseResume()
    {
        // TODO: toggle the Poll Service's paused state.
    }

    private static void OnCheckNow()
    {
        // TODO: force an immediate poll cycle, bypassing the timer.
    }

    private static void OnRecentNotifications()
    {
        // TODO: surface the last 5–10 notifications.
    }

    private static void OnSettings()
    {
        // TODO: navigate to the Settings panel.
    }

    private static void OnQuit()
    {
        // TODO: this is the only menu entry that actually terminates the process.
    }
}

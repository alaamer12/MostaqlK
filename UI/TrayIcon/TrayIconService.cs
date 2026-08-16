using MostaqlK.Features.Projects.Views;
using MostaqlK.Features.Settings.Views;
using MostaqlK.Services.Pipeline;

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
/// Windows system-tray icon: current state (mirrored live from <see cref="IPollService"/> +
/// <see cref="DiscoveryQueue"/>) and right-click menu, wired to real commands. The native
/// icon hosting itself (Shell_NotifyIcon) lives under <c>Platforms/Windows/TrayIconNativeHost.cs</c>
/// (kept there since it's Windows-only interop); this class owns the state/business logic that
/// is genuinely platform-neutral.
/// </summary>
public class TrayIconService
{
    private readonly IPollService _pollService;
    private readonly DiscoveryQueue _discoveryQueue;

    /// <summary>Current icon state, updated by the Poll Service / Worker Pool.</summary>
    public TrayIconState State { get; private set; } = TrayIconState.Idle;

    /// <summary>Raised whenever <see cref="State"/> changes, so the native host can swap the icon glyph.</summary>
    public event Action<TrayIconState>? StateChanged;

    /// <summary>
    /// Raised whenever the "Open" action runs (sidebar/tray menu/tray click), so the native
    /// window host can restore visibility if the window is currently hidden to the tray (see
    /// <c>CloseBehaviorService.CloseAction.MinimizeToTray</c>).
    /// </summary>
    public event Action? RestoreRequested;

    /// <summary>
    /// The right-click menu, in display order: Open window, Pause/Resume polling, Check now,
    /// Recent notifications, Settings, Quit.
    /// </summary>
    public List<TrayMenuItem> MenuItems { get; } = new();

    public TrayIconService(IPollService pollService, DiscoveryQueue discoveryQueue)
    {
        _pollService = pollService;
        _discoveryQueue = discoveryQueue;

        MenuItems.Add(new TrayMenuItem("Open", OnOpen));
        MenuItems.Add(new TrayMenuItem("Pause / Resume", OnPauseResume));
        MenuItems.Add(new TrayMenuItem("Check now", OnCheckNow));
        MenuItems.Add(new TrayMenuItem("Recent notifications", OnRecentNotifications));
        MenuItems.Add(new TrayMenuItem("Settings", OnSettings));
        MenuItems.Add(new TrayMenuItem("Quit", OnQuit));

        _pollService.StatusChanged += OnPollServiceStatusChanged;
    }

    private void OnPollServiceStatusChanged(PollServiceStatus status)
    {
        // BacklogDraining takes precedence over the raw poll status whenever the discovery
        // queue still has unenriched work sitting in it, per system-components.md § 13.1.
        var state = status switch
        {
            PollServiceStatus.Error => TrayIconState.Error,
            PollServiceStatus.Polling => TrayIconState.Polling,
            PollServiceStatus.BacklogDraining => TrayIconState.BacklogDraining,
            _ => _discoveryQueue.Count > 0 ? TrayIconState.BacklogDraining : TrayIconState.Idle,
        };

        SetState(state);
    }

    /// <summary>
    /// Updates the tray icon's reflected state. Called by the Poll Service / Worker Pool as the
    /// pipeline transitions between idle, polling, backlog-draining, and error states.
    /// </summary>
    public void SetState(TrayIconState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>
    /// Explicitly requests the native window host to restore visibility (e.g. when a second
    /// instance is launched and redirected here).
    /// </summary>
    public void RequestRestore()
    {
        OnOpen();
    }

    public void OnOpen()
    {
        // Bring the main window back to the foreground rather than exiting the process on
        // close, per spec (see system-components.md § 13.1). The actual native
        // activate/restore-from-tray call lives in MauiProgram's Windows lifecycle wiring
        // (subscribed to RestoreRequested); this just makes sure the app navigates back to the
        // projects feed.
        RestoreRequested?.Invoke();
        MainThread.BeginInvokeOnMainThread(() => Shell.Current?.GoToAsync($"//{nameof(MainWindowPage)}"));
    }

    /// <summary>Finds the currently displayed <see cref="MainWindowPage"/> instance, if any.</summary>
    private static MainWindowPage? FindMainWindowPage() => Shell.Current?.CurrentPage as MainWindowPage;

    private void OnPauseResume()
    {
        _pollService.SetPaused(!_pollService.IsPaused);
    }

    private void OnCheckNow()
    {
        _pollService.RequestCheckNow();
    }

    private void OnRecentNotifications()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnOpen();
            FindMainWindowPage()?.OpenNotificationsFlyout();
        });
    }

    private void OnSettings()
    {
        // Must restore the window first: if it's currently hidden to the tray (MinimizeToTray),
        // navigating alone leaves the app invisible - the same "nothing happened" symptom the
        // Open action itself works around via RestoreRequested.
        OnOpen();

        // Every other call site (AppShell, AboutPage, MainWindowPage, ProjectDetailsPage) uses
        // the absolute "//SettingsPanel" route; this one used the bare, relative route name
        // instead, which is why nothing happened - a relative GoToAsync depends on the current
        // page's own route stack, which a tray click (outside any page's context) cannot rely on.
        MainThread.BeginInvokeOnMainThread(() => Shell.Current?.GoToAsync("//SettingsPanel"));
    }

    private static void OnQuit()
    {
        MainThread.BeginInvokeOnMainThread(() => Application.Current?.Quit());
    }
}

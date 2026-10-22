using MostaqlK.Core.Navigation;
using MostaqlK.Features.Projects.Views;
using MostaqlK.Features.Settings.Views;
using MostaqlK.Services;
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
/// Windows system-tray icon: current state (mirrored live from the <b>active</b> pipeline's poll
/// status + discovery queue, resolved per use through <see cref="ActivePipeline"/>) and right-click
/// menu, wired to real commands. The native icon hosting itself (Shell_NotifyIcon) lives under
/// <c>Platforms/Windows/TrayIconNativeHost.cs</c> (kept there since it's Windows-only interop); this
/// class owns the state/business logic that is genuinely platform-neutral.
/// </summary>
public class TrayIconService
{
    // The tray is a long-lived singleton host, so it must NOT capture one source's poll service /
    // discovery queue at construction the way a transient view-model may. It injects ActivePipeline
    // and reads .Poll / .Queue on every command and status read, so pause/resume/check-now and the
    // backlog signal always act on the CURRENTLY ACTIVE source after a rail morph
    // (backend-ui-contract §2/§3).
    private readonly ActivePipeline _pipeline;
    private readonly GlobalAppStatusService? _globalStatus;
    private readonly List<SourcePipelineStatus> _hookedStatuses = [];

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

    public TrayIconService(ActivePipeline pipeline, GlobalAppStatusService? globalStatus = null)
    {
        _pipeline = pipeline;
        _globalStatus = globalStatus;

        MenuItems.Add(new TrayMenuItem("Open", OnOpen));
        MenuItems.Add(new TrayMenuItem("Pause / Resume", OnPauseResume));
        MenuItems.Add(new TrayMenuItem("Check now", OnCheckNow));
        MenuItems.Add(new TrayMenuItem("Recent notifications", OnRecentNotifications));
        MenuItems.Add(new TrayMenuItem("Settings", OnSettings));
        MenuItems.Add(new TrayMenuItem("Quit", OnQuit));

        // Subscribe to the poll service of the source that is active at construction (the default
        // source at startup — identical to the historical single-source tray). Live cross-source
        // state is still tracked below via GlobalAppStatusService.All, so a later morph keeps the
        // icon correct; the menu commands and status reads resolve .Poll/.Queue through ActivePipeline
        // so they act on the ACTIVE source regardless.
        _pipeline.Poll.StatusChanged += OnPollServiceStatusChanged;

        if (_globalStatus is not null)
        {
            // Cross-source aggregation: every source's pipeline telemetry is watched, so one
            // source failing or scanning shows in the tray no matter which one the UI projects.
            // With a single registered source this mirrors the historical Active-only behavior.
            foreach (var status in _globalStatus.All)
            {
                status.PropertyChanged += OnAnyPipelineStatusChanged;
                _hookedStatuses.Add(status);
            }
        }
    }

    private void OnAnyPipelineStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            or nameof(SourcePipelineStatus.IsScanning)
            or nameof(SourcePipelineStatus.LastScanFailed)
            or nameof(SourcePipelineStatus.QueueCount))
        {
            RecomputeAggregatedState();
        }
    }

    private void RecomputeAggregatedState()
    {
        if (_globalStatus is null || _hookedStatuses.Count == 0)
        {
            return;
        }

        var state = TrayIconState.Idle;
        if (_hookedStatuses.Any(s => s.LastScanFailed))
        {
            state = TrayIconState.Error;
        }
        else if (_hookedStatuses.Any(s => s.IsScanning))
        {
            state = TrayIconState.Polling;
        }
        else if (_hookedStatuses.Any(s => s.QueueCount > 0))
        {
            state = TrayIconState.BacklogDraining;
        }
        else if (_pipeline.Poll.Status == PollServiceStatus.BacklogDraining)
        {
            state = TrayIconState.BacklogDraining;
        }

        SetState(state);
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
            _ => _pipeline.Queue.Count > 0 ? TrayIconState.BacklogDraining : TrayIconState.Idle,
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
        _ = AppRoutes.NavigateAsync(AppRoutes.Projects);
    }

    /// <summary>Finds the currently displayed <see cref="MainWindowPage"/> instance, if any.</summary>
    private static MainWindowPage? FindMainWindowPage() => Shell.Current?.CurrentPage as MainWindowPage;

    private void OnPauseResume()
    {
        var poll = _pipeline.Poll;
        poll.SetPaused(!poll.IsPaused);
    }

    private void OnCheckNow()
    {
        _pipeline.Poll.RequestCheckNow();
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

        _ = AppRoutes.NavigateAsync(AppRoutes.Settings);
    }

    private static void OnQuit()
    {
        // Intentional exit: the out-of-process supervisor must not relaunch the app.
        MostaqlK.Core.Diagnostics.ExitIntent.MarkIntentional();
        MainThread.BeginInvokeOnMainThread(() => Application.Current?.Quit());
    }
}

using MostaqlK.Core.Platform;

namespace MostaqlK.Core.Diagnostics;

/// <summary>
/// Pure decision rules for the out-of-process supervisor (<c>Platforms/Windows/AppWatchdog.cs</c>)
/// that brings the tray app back after a native fail-fast. Isolated (no Win32, no MAUI) so the
/// crash-path tests can prove the relaunch/throttle behaviour without killing real processes.
/// <para>
/// Why a supervisor at all: WinUI's XAML runtime terminates the process with
/// <c>RoFailFastWithErrorContext</c> (exit code <c>0xC000027B</c>/<c>0xC0000409</c>) from threads
/// such as the CLR finalizer thread. No managed handler — <c>Application.UnhandledException</c>,
/// <c>AppDomain.UnhandledException</c>, or a native SEH filter — can veto that, so the only way to
/// guarantee "the tray icon is still there hours later" is a second process that relaunches it.
/// </para>
/// </summary>
public static class WatchdogPolicy
{
    /// <summary>Argument that turns a <c>MostaqlK.exe</c> process into the supervisor: <c>--watchdog &lt;pid&gt;</c>.</summary>
    public const string WatchdogArg = "--watchdog";

    /// <summary>Passed to an app instance that was (re)launched by the supervisor, so it does not spawn a second one.</summary>
    public const string SupervisedArg = "--supervised";

    /// <summary>Set to <c>1</c> to disable the supervisor entirely (developer/debug escape hatch).</summary>
    public const string DisableEnvVar = "MOSTAQLK_NO_WATCHDOG";

    /// <summary>Restarts allowed inside <see cref="RestartWindow"/> before the supervisor gives up (crash loop guard).</summary>
    public const int MaxRestartsInWindow = 5;

    /// <summary>Sliding window used by the crash-loop guard.</summary>
    public static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(30);

    /// <summary>Settle delay before relaunching, so WER can finish writing the dump and the singleton mutex is released.</summary>
    public static readonly TimeSpan RelaunchDelay = TimeSpan.FromSeconds(4);

    /// <summary>Name of the per-process manual-reset event the app signals when it is exiting on purpose.</summary>
    public static string CleanExitEventName(int processId) => $@"Local\{ChannelIdentity.AppName}.App.CleanExit.{processId}";

    /// <summary>
    /// Parses <c>--watchdog &lt;pid&gt;</c>. Returns <c>false</c> when this process is not a supervisor.
    /// </summary>
    public static bool TryParseWatchdogArgs(IReadOnlyList<string> args, out int targetPid)
    {
        targetPid = 0;
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], WatchdogArg, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out var pid) && pid > 0)
            {
                targetPid = pid;
                return true;
            }
        }

        return false;
    }

    /// <summary>True when this app instance was launched by the supervisor (so it must not spawn another one).</summary>
    public static bool IsSupervised(IReadOnlyList<string> args) =>
        args.Any(a => string.Equals(a, SupervisedArg, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the primary app instance should spawn a supervisor for itself.
    /// </summary>
    public static bool ShouldSpawnWatchdog(IReadOnlyList<string> args, bool isDebuggerAttached, string? disableEnvValue) =>
        !isDebuggerAttached &&
        !IsSupervised(args) &&
        !string.Equals(disableEnvValue?.Trim(), "1", StringComparison.Ordinal);

    /// <summary>
    /// Decides what the supervisor does once the watched process has exited.
    /// </summary>
    /// <param name="cleanExitSignaled">The app signalled its clean-exit event (user chose Exit / normal shutdown).</param>
    /// <param name="isSystemShuttingDown">Windows is logging off / shutting down (<c>SM_SHUTTINGDOWN</c>).</param>
    /// <param name="recentRestartsUtc">Timestamps of relaunches this supervisor already performed.</param>
    /// <param name="nowUtc">Current time.</param>
    public static WatchdogDecision Decide(
        bool cleanExitSignaled,
        bool isSystemShuttingDown,
        IReadOnlyCollection<DateTime> recentRestartsUtc,
        DateTime nowUtc)
    {
        if (cleanExitSignaled)
        {
            return WatchdogDecision.StopCleanExit;
        }

        if (isSystemShuttingDown)
        {
            return WatchdogDecision.StopSystemShutdown;
        }

        var inWindow = recentRestartsUtc.Count(t => nowUtc - t <= RestartWindow);
        return inWindow >= MaxRestartsInWindow
            ? WatchdogDecision.StopCrashLoop
            : WatchdogDecision.Relaunch;
    }

    /// <summary>
    /// Arguments for a supervisor-driven relaunch: always back to the tray, never a second supervisor.
    /// </summary>
    public static string[] RelaunchArgs() => new[] { "--silent-start", SupervisedArg };
}

/// <summary>Outcome of <see cref="WatchdogPolicy.Decide"/>.</summary>
public enum WatchdogDecision
{
    Relaunch,
    StopCleanExit,
    StopSystemShutdown,
    StopCrashLoop,
}

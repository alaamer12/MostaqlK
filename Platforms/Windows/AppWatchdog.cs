using System.Diagnostics;
using System.Runtime.InteropServices;
using MostaqlK.Core.Diagnostics;
using MostaqlK.Core.Platform;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// Out-of-process supervisor that keeps the tray app alive across native fail-fasts.
/// <para>
/// Root cause it backstops (from the 2026-10-19 dump, <c>%LocalAppData%\CrashDumps\MostaqlK.exe.7056.dmp</c>):
/// the CLR <b>finalizer thread</b> released a WinUI XAML object and <c>Microsoft.UI.Xaml.dll!ctl::ComBase::ReleaseImpl</c>
/// called <c>FailFastWithStowedExceptions</c> (<c>0xC000027B</c>, stowed <c>0x8000000E</c>) after ~1h20m hidden in
/// the tray. The 2026-10-14 dump died the same way in <c>UIAffinityReleaseQueue::DoCleanup</c>. A fail-fast never
/// reaches <c>Application.UnhandledException</c>, so no in-process handler can stop it. The in-process fix is
/// <see cref="TransformReuseMapping"/> (removes the per-frame XAML transform garbage that fed the finalizer); this
/// class guarantees that if WinUI ever fail-fasts again anyway, the app is back in the tray within seconds.
/// </para>
/// <para>
/// Protocol: the primary instance creates a per-PID manual-reset event (<see cref="WatchdogPolicy.CleanExitEventName"/>)
/// and spawns <c>MostaqlK.exe --watchdog &lt;pid&gt;</c>. The event is set on intentional exit (<see cref="ExitIntent"/>,
/// plus <c>AppDomain.ProcessExit</c>, which never runs for a fail-fast). When the watched process dies without the
/// event set, the supervisor relaunches it with <c>--silent-start --supervised</c> and keeps watching the new PID.
/// Everything is logged to <c>log/&lt;date&gt;/watchdog.log</c>.
/// </para>
/// </summary>
internal static class AppWatchdog
{
    private static readonly string WatchdogMutexName = ChannelIdentity.WatchdogMutexName;
    private static readonly string SingletonMutexName = ChannelIdentity.SingletonMutexName;
    private const int SmShuttingDown = 0x2000;

    private static EventWaitHandle? _cleanExitEvent;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    // ------------------------------------------------------------------ app side

    /// <summary>
    /// Called by the primary app instance (after it owns the singleton mutex, before WinUI starts).
    /// Creates the clean-exit event and, unless supervised already, spawns the supervisor.
    /// </summary>
    public static void InitializeForPrimaryInstance(string[] args, Action<string> log)
    {
        var pid = Environment.ProcessId;
        try
        {
            _cleanExitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, WatchdogPolicy.CleanExitEventName(pid));
            ExitIntent.Handler = () => _cleanExitEvent?.Set();
            // Runs for every orderly managed exit (Application.Exit -> Main returns, Environment.Exit),
            // never for RoFailFast / RtlFailFast / unhandled exceptions - exactly the split we need.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ExitIntent.MarkIntentional();
        }
        catch (Exception ex)
        {
            log($"[Watchdog] clean-exit event setup failed: {ex.Message}");
        }

        var disable = Environment.GetEnvironmentVariable(WatchdogPolicy.DisableEnvVar);
        if (!WatchdogPolicy.ShouldSpawnWatchdog(args, Debugger.IsAttached, disable))
        {
            log($"[Watchdog] not spawning (supervised={WatchdogPolicy.IsSupervised(args)}, debugger={Debugger.IsAttached}, {WatchdogPolicy.DisableEnvVar}={disable ?? "<unset>"})");
            return;
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                log("[Watchdog] cannot spawn: ProcessPath missing");
                return;
            }

            var psi = CreateStartInfo(exe);
            psi.ArgumentList.Add(WatchdogPolicy.WatchdogArg);
            psi.ArgumentList.Add(pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var watchdog = Process.Start(psi);
            log($"[Watchdog] supervisor spawned pid={watchdog?.Id} for app pid={pid}");
        }
        catch (Exception ex)
        {
            log($"[Watchdog] spawn failed: {ex}");
        }
    }

    // ------------------------------------------------------------- supervisor side

    /// <summary>
    /// If this process was started as <c>--watchdog &lt;pid&gt;</c>, runs the supervisor loop to completion and
    /// returns <c>true</c> (the caller must then return from <c>Main</c> without touching WinUI).
    /// </summary>
    public static bool TryRunAsWatchdog(string[] args)
    {
        if (!WatchdogPolicy.TryParseWatchdogArgs(args, out var targetPid))
        {
            return false;
        }

        try
        {
            RunSupervisor(targetPid);
        }
        catch (Exception ex)
        {
            Log($"supervisor crashed: {ex}");
        }

        return true;
    }

    private static void RunSupervisor(int targetPid)
    {
        using var mutex = new Mutex(initiallyOwned: false, WatchdogMutexName);
        var owns = false;
        try
        {
            try
            {
                owns = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                owns = true; // previous supervisor died; we take over
            }

            if (!owns)
            {
                Log($"another supervisor is already active; exiting (target pid={targetPid})");
                return;
            }

            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch
            {
                // cosmetic
            }

            Log($"supervisor started pid={Environment.ProcessId}, watching pid={targetPid}, exe={Environment.ProcessPath}");
            Supervise(targetPid);
        }
        finally
        {
            if (owns)
            {
                try { mutex.ReleaseMutex(); } catch { }
            }
        }
    }

    private static void Supervise(int pid)
    {
        var restarts = new List<DateTime>();

        while (true)
        {
            var watched = Watch(pid);

            var cleanExit = watched.CleanExitSignaled;
            var shuttingDown = IsSystemShuttingDown();
            var exitName = watched.ExitCode is int code ? NativeExceptionPolicy.NameFor(unchecked((uint)code)) : "<unknown>";
            Log($"app pid={pid} exited: code={exitName}, cleanExit={cleanExit}, uptime={watched.Uptime?.ToString(@"d\.hh\:mm\:ss") ?? "<unknown>"}, systemShuttingDown={shuttingDown}");

            var decision = WatchdogPolicy.Decide(cleanExit, shuttingDown, restarts, DateTime.UtcNow);
            if (decision != WatchdogDecision.Relaunch)
            {
                Log($"not relaunching: {decision}");
                if (decision == WatchdogDecision.StopCrashLoop)
                {
                    NativeCrashForwarder.WritePendingCrash(pid, watched.ExitCode, exitName, watched.Uptime, LogLatestCrashDump(pid), Log);
                }

                return;
            }

            // Give WER time to finish the dump and the OS time to release the dead process' mutex/AppInstance key.
            Thread.Sleep(WatchdogPolicy.RelaunchDelay);
            var dumpPath = LogLatestCrashDump(pid);
            // Hand the crash to the next app launch, which forwards it to Sentry (this process has no Sentry).
            NativeCrashForwarder.WritePendingCrash(pid, watched.ExitCode, exitName, watched.Uptime, dumpPath, Log);

            if (IsSystemShuttingDown())
            {
                Log("system started shutting down during relaunch delay; not relaunching");
                return;
            }

            // The user (or a volatile-extract relocation) may already have brought a primary back: adopt it.
            if (TryFindRunningPrimary(out var adopted))
            {
                Log($"a primary instance is already running (pid={adopted}); adopting it instead of relaunching");
                pid = adopted;
                continue;
            }

            var relaunched = Relaunch();
            if (relaunched is null)
            {
                Log("relaunch failed; supervisor exiting");
                return;
            }

            restarts.Add(DateTime.UtcNow);
            Log($"relaunched app as pid={relaunched} (restart {restarts.Count}, max {WatchdogPolicy.MaxRestartsInWindow} per {WatchdogPolicy.RestartWindow.TotalMinutes:0} min)");
            pid = relaunched.Value;
        }
    }

    private readonly record struct WatchResult(int? ExitCode, bool CleanExitSignaled, TimeSpan? Uptime);

    private static WatchResult Watch(int pid)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
            // Open the handle *now* so the exit code survives the process' death.
            _ = process.SafeHandle;
        }
        catch (Exception ex)
        {
            Log($"cannot open pid={pid}: {ex.Message}");
            process?.Dispose();
            process = null;
        }

        using (process)
        {
            DateTime? startedAt = null;
            try { startedAt = process?.StartTime; } catch { }

            // A freshly relaunched child needs a moment to reach Main and create its event.
            EventWaitHandle? cleanEvent = null;
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (cleanEvent is null && DateTime.UtcNow < deadline)
            {
                if (EventWaitHandle.TryOpenExisting(WatchdogPolicy.CleanExitEventName(pid), out var opened))
                {
                    cleanEvent = opened;
                    break;
                }

                if (process is null || process.WaitForExit(500))
                {
                    break;
                }
            }

            if (cleanEvent is null)
            {
                Log($"clean-exit event for pid={pid} not found (process exited early or is not a primary instance)");
            }

            using (cleanEvent)
            {
                process?.WaitForExit();

                int? exitCode = null;
                try { exitCode = process?.ExitCode; } catch { }

                TimeSpan? uptime = null;
                try
                {
                    if (startedAt is { } s && process is not null)
                    {
                        uptime = process.ExitTime - s;
                    }
                }
                catch { }

                var clean = false;
                try { clean = cleanEvent?.WaitOne(0) == true; } catch { }

                return new WatchResult(exitCode, clean, uptime);
            }
        }
    }

    private static int? Relaunch()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Log("relaunch impossible: ProcessPath missing");
                return null;
            }

            var psi = CreateStartInfo(exe);
            foreach (var arg in WatchdogPolicy.RelaunchArgs())
            {
                psi.ArgumentList.Add(arg);
            }

            using var child = Process.Start(psi);
            return child?.Id;
        }
        catch (Exception ex)
        {
            Log($"relaunch threw: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Finds a running primary app instance: only primaries create their clean-exit event, so a sibling
    /// <c>MostaqlK</c> process with an existing event is the one to watch.
    /// </summary>
    private static bool TryFindRunningPrimary(out int pid)
    {
        pid = 0;
        if (!Mutex.TryOpenExisting(SingletonMutexName, out var singleton))
        {
            return false;
        }

        singleton.Dispose();
        try
        {
            using var self = Process.GetCurrentProcess();
            foreach (var candidate in Process.GetProcessesByName(self.ProcessName))
            {
                using (candidate)
                {
                    if (candidate.Id == self.Id)
                    {
                        continue;
                    }

                    if (EventWaitHandle.TryOpenExisting(WatchdogPolicy.CleanExitEventName(candidate.Id), out var evt))
                    {
                        evt.Dispose();
                        pid = candidate.Id;
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"primary lookup failed: {ex.Message}");
        }

        return false;
    }

    private static ProcessStartInfo CreateStartInfo(string exe)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
        };

        try
        {
            // Keep the single-file extraction out of %TEMP% for every process we start.
            psi.Environment[BundleExtractPolicy.ExtractBaseEnvVar] = MostaqlK.Core.Platform.AppPaths.BundleCacheDirectory;
        }
        catch
        {
            // inherit whatever the parent had
        }

        return psi;
    }

    private static bool IsSystemShuttingDown()
    {
        try
        {
            return GetSystemMetrics(SmShuttingDown) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? LogLatestCrashDump(int pid)
    {
        try
        {
            var dumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            var candidate = Path.Combine(dumps, $"{Path.GetFileName(Environment.ProcessPath)}.{pid}.dmp");
            var exists = File.Exists(candidate);
            Log(exists
                ? $"WER dump for pid={pid}: {candidate} ({new FileInfo(candidate).Length / 1024} KB)"
                : $"no WER dump found for pid={pid} at {candidate}");
            return exists ? candidate : null;
        }
        catch
        {
            // diagnostics only
            return null;
        }
    }

    private static readonly object LogLock = new();

    private static void Log(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [pid {Environment.ProcessId}] {message}{Environment.NewLine}";
            lock (LogLock)
            {
                File.AppendAllText(Path.Combine(MostaqlK.Core.Platform.AppPaths.LogsDirectory, "watchdog.log"), line);
            }
        }
        catch
        {
            // never let logging kill the supervisor
        }
    }
}

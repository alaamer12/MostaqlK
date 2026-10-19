using System.IO.Compression;
using System.Text.Json;
using MostaqlK.Core.Platform;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// Gets native fail-fast crashes into Sentry.
/// <para>
/// Why Sentry never saw the long-idle crashes: WinUI's <c>RoFailFastWithErrorContext</c> terminates the process
/// instantly - no managed code (Sentry's handlers included) ever runs, and the .NET SDK only captures managed
/// exceptions. On top of that, the old post-mortem path (<c>CrashReporter.CheckAndReportPreviousCrashes</c>) runs
/// in <c>Program.Main</c> <i>before</i> <c>UseSentry</c> initialises the SDK, and only looks for <c>log/crash.dmp</c>,
/// which a fail-fast never writes - so it reported nothing.
/// </para>
/// <para>
/// Now: the out-of-process <see cref="AppWatchdog"/> (which witnesses the death and its exit code) writes a
/// pending-crash record; this forwarder runs in the next app instance once Sentry is enabled, and sends one Fatal
/// event per crash with the gzipped WER minidump (<c>%LocalAppData%\CrashDumps</c>) plus the watchdog / heartbeat /
/// startup / interaction logs of that day attached. WER dumps not covered by a record (crash while no watchdog was
/// running) are picked up too. Each dump is reported once.
/// </para>
/// </summary>
internal static class NativeCrashForwarder
{
    private const int MaxDumpsPerLaunch = 3;
    private const long MaxLogAttachmentBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan MaxDumpAge = TimeSpan.FromDays(14);
    private static int _scheduled;

    private static string PendingDirectory => Path.Combine(AppPaths.AppDirectory, "pending-crashes");
    private static string ReportedListPath => Path.Combine(AppPaths.AppDirectory, "reported-crash-dumps.txt");

    private sealed class PendingCrash
    {
        public int Pid { get; set; }
        public int? ExitCode { get; set; }
        public string ExitCodeName { get; set; } = "";
        public double? UptimeSeconds { get; set; }
        public string? DumpPath { get; set; }
        public DateTime DetectedAtUtc { get; set; }
    }

    // ------------------------------------------------------------ watchdog side

    public static void WritePendingCrash(int pid, int? exitCode, string exitCodeName, TimeSpan? uptime, string? dumpPath, Action<string> log)
    {
        try
        {
            Directory.CreateDirectory(PendingDirectory);
            var record = new PendingCrash
            {
                Pid = pid,
                ExitCode = exitCode,
                ExitCodeName = exitCodeName,
                UptimeSeconds = uptime?.TotalSeconds,
                DumpPath = dumpPath,
                DetectedAtUtc = DateTime.UtcNow,
            };
            var path = Path.Combine(PendingDirectory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{pid}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(record));
            log($"pending crash record written for Sentry: {path}");
        }
        catch (Exception ex)
        {
            log($"pending crash record failed: {ex.Message}");
        }
    }

    // ----------------------------------------------------------------- app side

    /// <summary>Forwards pending crashes on a background thread once Sentry is enabled (idempotent, never throws).</summary>
    public static void ScheduleForward()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Sentry.Maui finishes initialising during app start; give it time, then give up quietly.
                for (var i = 0; i < 60 && !Sentry.SentrySdk.IsEnabled; i++)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                }

                if (!Sentry.SentrySdk.IsEnabled)
                {
                    return;
                }

                // Let the UI finish starting before compressing ~20 MB dumps.
                await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                Forward();
            }
            catch (Exception ex)
            {
                MostaqlK.Services.Diagnostics.CrashReporter.Report("NativeCrashForwarder", ex, isFatal: false);
            }
        });
    }

    private static void Forward()
    {
        var reported = LoadReported();
        var sent = 0;
        var tempFiles = new List<string>();

        // 1. Watchdog-witnessed crashes (exact exit code + uptime).
        if (Directory.Exists(PendingDirectory))
        {
            foreach (var file in Directory.GetFiles(PendingDirectory, "crash-*.json").OrderBy(f => f))
            {
                PendingCrash? record = null;
                try { record = JsonSerializer.Deserialize<PendingCrash>(File.ReadAllText(file)); } catch { }

                if (record is not null && sent < MaxDumpsPerLaunch)
                {
                    var key = record.DumpPath is not null ? DumpKey(record.DumpPath) : $"pid-{record.Pid}-{record.DetectedAtUtc:o}";
                    if (!reported.Contains(key) && Send(record, tempFiles))
                    {
                        reported.Add(key);
                        sent++;
                    }
                }

                try { File.Delete(file); } catch { }
            }
        }

        // 2. WER dumps no watchdog saw (first run after upgrade, watchdog disabled, crash loop, ...).
        try
        {
            var dumpsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            var exeName = Path.GetFileName(Environment.ProcessPath) ?? "MostaqlK.exe";
            if (Directory.Exists(dumpsDir))
            {
                foreach (var dump in new DirectoryInfo(dumpsDir).GetFiles($"{exeName}.*.dmp")
                             .Where(f => DateTime.UtcNow - f.LastWriteTimeUtc <= MaxDumpAge)
                             .OrderByDescending(f => f.LastWriteTimeUtc))
                {
                    if (sent >= MaxDumpsPerLaunch)
                    {
                        break;
                    }

                    var key = DumpKey(dump.FullName);
                    if (reported.Contains(key))
                    {
                        continue;
                    }

                    var pidText = Path.GetFileNameWithoutExtension(dump.Name).Split('.').LastOrDefault();
                    var record = new PendingCrash
                    {
                        Pid = int.TryParse(pidText, out var p) ? p : 0,
                        ExitCodeName = "<unknown: not witnessed by watchdog>",
                        DumpPath = dump.FullName,
                        DetectedAtUtc = dump.LastWriteTimeUtc,
                    };

                    if (Send(record, tempFiles))
                    {
                        reported.Add(key);
                        sent++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MostaqlK.Services.Diagnostics.CrashReporter.Report("NativeCrashForwarder.ScanWer", ex, isFatal: false);
        }

        if (sent > 0)
        {
            SaveReported(reported);
            Sentry.SentrySdk.Flush(TimeSpan.FromSeconds(60));
        }

        foreach (var temp in tempFiles)
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static bool Send(PendingCrash record, List<string> tempFiles)
    {
        try
        {
            var crashLocal = record.DetectedAtUtc.ToLocalTime();
            var uptime = record.UptimeSeconds is double s ? TimeSpan.FromSeconds(s).ToString(@"d\.hh\:mm\:ss") : "unknown";
            var message = $"Native crash (fail-fast) of previous session: {record.ExitCodeName}, uptime {uptime}";

            string? gzDump = null;
            if (record.DumpPath is not null && File.Exists(record.DumpPath))
            {
                gzDump = Path.Combine(Path.GetTempPath(), $"mostaqlk-{Path.GetFileName(record.DumpPath)}.gz");
                using (var input = File.OpenRead(record.DumpPath))
                using (var output = File.Create(gzDump))
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                {
                    input.CopyTo(gzip);
                }

                tempFiles.Add(gzDump);
            }

            var evt = new Sentry.SentryEvent
            {
                Message = message,
                Level = Sentry.SentryLevel.Fatal,
            };
            evt.SetTag("crash_type", "native_fail_fast");
            evt.SetTag("post_mortem", "true");
            evt.SetTag("exit_code", record.ExitCodeName);
            evt.SetTag("witnessed_by_watchdog", record.ExitCode is null ? "false" : "true");
            evt.SetExtra("pid", record.Pid);
            evt.SetExtra("uptime", uptime);
            evt.SetExtra("crash_time_local", crashLocal.ToString("yyyy-MM-dd HH:mm:ss"));
            evt.SetExtra("dump_path", record.DumpPath ?? "<none>");
            evt.SetFingerprint("native-fail-fast", record.ExitCodeName);

            var id = Sentry.SentrySdk.CaptureEvent(evt, scope =>
            {
                if (gzDump is not null)
                {
                    scope.AddAttachment(gzDump, Sentry.AttachmentType.Default, "application/gzip");
                }

                var dayDir = Path.Combine(AppPaths.BaseLogsDirectory, crashLocal.ToString("yyyy-MM-dd"));
                foreach (var name in new[] { "watchdog.log", "heartbeat.log", "startup-debug.log", "interaction-log.txt", "crash.log" })
                {
                    var path = Path.Combine(dayDir, name);
                    try
                    {
                        if (File.Exists(path) && new FileInfo(path).Length <= MaxLogAttachmentBytes)
                        {
                            scope.AddAttachment(path, Sentry.AttachmentType.Default, "text/plain");
                        }
                    }
                    catch
                    {
                        // attachment is best-effort
                    }
                }
            });

            return id != Sentry.SentryId.Empty;
        }
        catch (Exception ex)
        {
            MostaqlK.Services.Diagnostics.CrashReporter.Report("NativeCrashForwarder.Send", ex, isFatal: false);
            return false;
        }
    }

    private static string DumpKey(string dumpPath)
    {
        try
        {
            var info = new FileInfo(dumpPath);
            return $"{info.Name}|{info.Length}|{info.LastWriteTimeUtc:o}";
        }
        catch
        {
            return dumpPath;
        }
    }

    private static HashSet<string> LoadReported()
    {
        try
        {
            if (File.Exists(ReportedListPath))
            {
                return new HashSet<string>(File.ReadAllLines(ReportedListPath).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // start fresh
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveReported(HashSet<string> reported)
    {
        try
        {
            File.WriteAllLines(ReportedListPath, reported.TakeLast(200));
        }
        catch
        {
            // worst case a dump is reported twice
        }
    }
}

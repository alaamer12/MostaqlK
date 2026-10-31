using System.IO.Compression;
using System.Text.Json;
using MostaqlK.Core.Diagnostics;
using MostaqlK.Core.Platform;
using MostaqlK.Services.Diagnostics;

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
/// pending-crash record; this forwarder runs in the next app instance once Sentry is enabled. Dev71 rebuilt the
/// intake after the dev69 crash loop proved three failures of the old shape: records were DELETED whether or not
/// they were ever sent (5 of 8 witnessed deaths silently vanished behind a launch cap), nothing proved a send
/// succeeded, and a pile of identical per-death messages never reads as "crash loop". Now, per launch:
/// a clustered burst of ≥3 deaths becomes ONE small, attachment-free loop event FIRST (it survives a process that
/// dies 48 s later — no gzip race), then up to <see cref="NativeCrashIntake.MaxDumpsPerLaunch"/> gzipped WER
/// dumps go as individual events with the DEAD build's own logs and a fault digest (the last distinct FAULT lines
/// of that pid around the death, so the CAUSE is readable in the Sentry UI without opening attachments). A pending
/// record is deleted only after a proven send, a duplicate ledger hit, or age-out past log retention; a
/// budget-deferred record survives to the next launch. Every decision is logged as a SentryCrashForward MARK.
/// </para>
/// </summary>
internal static class NativeCrashForwarder
{
    private const long MaxLogAttachmentBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan MaxOrphanDumpAge = TimeSpan.FromDays(14);
    private static int _scheduled;

    private static string PendingDirectory => Path.Combine(AppPaths.AppDirectory, "pending-crashes");
    private static string ReportedListPath => Path.Combine(AppPaths.AppDirectory, "reported-crash-dumps.txt");

    // ------------------------------------------------------------ watchdog side

    public static void WritePendingCrash(int pid, int? exitCode, string exitCodeName, TimeSpan? uptime, string? dumpPath, Action<string> log)
    {
        try
        {
            Directory.CreateDirectory(PendingDirectory);
            var record = new PendingCrashRecord
            {
                Pid = pid,
                ExitCode = exitCode,
                ExitCodeName = exitCodeName,
                UptimeSeconds = uptime?.TotalSeconds,
                DumpPath = dumpPath,
                DetectedAtUtc = DateTime.UtcNow,
                // The watchdog is a process of the SAME build as the one it watches, so this tag
                // names the dead build's per-version log folder — where its day logs live.
                VersionTag = AppPaths.VersionTag,
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

                // Phase A runs IMMEDIATELY after enablement: grouped loop events are small and
                // attachment-free, so they land even when this instance is itself minutes from a
                // repeat death. The old design buried everything behind a 15 s settle + gzip work
                // that a crash-looping successor never lived to finish.
                ForwardGroupEvents();

                // Phase B waits for the UI to finish starting before compressing ~12 MB dumps.
                await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                ForwardDumpEvents();
            }
            catch (Exception ex)
            {
                CrashReporter.Report("NativeCrashForwarder", ex, isFatal: false);
            }
        });
    }

    private sealed record Loaded(PendingCrashRecord Record, string? FilePath);

    /// <summary>Reads every pending record + orphan WER dumps, newest-first. Unreadable files are quarantined, never deleted blind.</summary>
    private static IReadOnlyList<Loaded> LoadBacklog()
    {
        var loaded = new List<Loaded>();
        try
        {
            if (Directory.Exists(PendingDirectory))
            {
                foreach (var file in Directory.GetFiles(PendingDirectory, "crash-*.json").OrderBy(f => f, StringComparer.Ordinal))
                {
                    try
                    {
                        var record = JsonSerializer.Deserialize<PendingCrashRecord>(File.ReadAllText(file));
                        if (record is not null)
                        {
                            loaded.Add(new Loaded(record, file));
                        }
                        else
                        {
                            Quarantine(file);
                        }
                    }
                    catch
                    {
                        Quarantine(file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Report("NativeCrashForwarder.ScanPending", ex, isFatal: false);
        }

        try
        {
            var dumpsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            var exeName = Path.GetFileName(Environment.ProcessPath) ?? "MostaqlK.exe";
            if (Directory.Exists(dumpsDir))
            {
                var covered = loaded
                    .Where(l => l.Record.DumpPath is not null)
                    .Select(l => l.Record.DumpPath!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var dump in new DirectoryInfo(dumpsDir).GetFiles($"{exeName}.*.dmp")
                             .Where(f => DateTime.UtcNow - f.LastWriteTimeUtc <= MaxOrphanDumpAge)
                             .Where(f => !covered.Contains(f.FullName)))
                {
                    loaded.Add(new Loaded(new PendingCrashRecord
                    {
                        Pid = int.TryParse(Path.GetFileNameWithoutExtension(dump.Name).Split('.').LastOrDefault(), out var p) ? p : 0,
                        ExitCodeName = "<unknown: not witnessed by watchdog>",
                        DumpPath = dump.FullName,
                        DetectedAtUtc = dump.LastWriteTimeUtc,
                    }, null));
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Report("NativeCrashForwarder.ScanWer", ex, isFatal: false);
        }

        return loaded
            .OrderByDescending(l => l.Record.DetectedAtUtc)
            .ToList();
    }

    private static void Quarantine(string file)
    {
        try
        {
            File.Move(file, file + ".bad", overwrite: true);
            InteractionLogger.Mark("SentryCrashForward", "quarantined-corrupt", new { file = Path.GetFileName(file) });
        }
        catch
        {
            // A file we can neither parse nor move is not worth dying over.
        }
    }

    /// <summary>Cluster events first: cheap, and they survive a quick repeat death.</summary>
    private static void ForwardGroupEvents()
    {
        var backlog = LoadBacklog();
        if (backlog.Count == 0)
        {
            return;
        }

        var reported = LoadReported();
        var clusters = NativeCrashIntake.Cluster(
            [.. backlog.Select(l => l.Record)],
            NativeCrashIntake.ClusterWindow,
            NativeCrashIntake.LoopThreshold);

        var anythingSent = false;
        foreach (var cluster in clusters)
        {
            if (!cluster.IsLoop || reported.Contains(NativeCrashIntake.ClusterKey(cluster)))
            {
                continue;
            }

            if (SendClusterEvent(cluster))
            {
                reported.Add(NativeCrashIntake.ClusterKey(cluster));
                SaveReported(reported);
                anythingSent = true;
            }
        }

        // Small, attachment-free events: flush cheaply so the loop count survives even if this
        // instance dies before the gzip phase.
        if (anythingSent)
        {
            try { Sentry.SentrySdk.Flush(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    private static void ForwardDumpEvents()
    {
        var reported = LoadReported();
        var backlog = LoadBacklog();
        var sent = 0;
        var tempFiles = new List<string>();
        try
        {
            foreach (var item in backlog)
            {
                var action = NativeCrashIntake.Decide(item.Record, DateTime.UtcNow, reported, sent);
                switch (action)
                {
                    case IntakeAction.Send:
                        if (SendDumpEvent(item, tempFiles))
                        {
                            reported.Add(NativeCrashIntake.KeyFor(item.Record));
                            SaveReported(reported);
                            if (item.Record.DumpPath is not null)
                            {
                                sent++;
                            }

                            DeletePendingFile(item);
                        }
                        else
                        {
                            // Send failed: KEEP the record — the old code deleted it anyway, which
                            // is how five of eight witnessed deaths vanished unreported. Stop
                            // spending budget this launch; next launch retries from the top.
                            InteractionLogger.Mark("SentryCrashForward", "send-failed-kept",
                                new { pid = item.Record.Pid, exit = item.Record.ExitCodeName });
                            return;
                        }

                        break;

                    case IntakeAction.DeferBudget:
                        InteractionLogger.Mark("SentryCrashForward", "deferred", new { pid = item.Record.Pid });
                        break;

                    case IntakeAction.DropDuplicate:
                        DeletePendingFile(item);
                        InteractionLogger.Mark("SentryCrashForward", "duplicate", new { pid = item.Record.Pid });
                        break;

                    case IntakeAction.DropTooOld:
                        DeletePendingFile(item);
                        InteractionLogger.Mark("SentryCrashForward", "aged-out",
                            new { pid = item.Record.Pid, detected = item.Record.DetectedAtUtc });
                        break;
                }
            }

            if (sent > 0)
            {
                Sentry.SentrySdk.Flush(TimeSpan.FromSeconds(60));
            }
        }
        finally
        {
            foreach (var temp in tempFiles)
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static void DeletePendingFile(Loaded item)
    {
        if (item.FilePath is not null)
        {
            try { File.Delete(item.FilePath); } catch { }
        }
    }

    private static bool SendClusterEvent(CrashCluster cluster)
    {
        try
        {
            var evt = new Sentry.SentryEvent
            {
                Message = NativeCrashIntake.BuildMessage(cluster),
                Level = Sentry.SentryLevel.Fatal,
            };
            evt.SetTag("crash_type", "native_fail_fast_loop");
            evt.SetTag("post_mortem", "true");
            evt.SetTag("exit_code", cluster.DominantExitCode);
            evt.SetFingerprint(NativeCrashIntake.BuildFingerprint(cluster));
            evt.SetExtra("deaths", cluster.Count);
            evt.SetExtra("window_start_utc", cluster.WindowStartUtc.ToString("o"));
            evt.SetExtra("window_end_utc", cluster.WindowEndUtc.ToString("o"));
            evt.SetExtra("exit_code_histogram", JsonSerializer.Serialize(cluster.ExitCodeHistogram));
            evt.SetExtra("uptimes_seconds", string.Join(',', cluster.Records
                .Where(r => r.UptimeSeconds is not null).Select(r => r.UptimeSeconds!.Value.ToString("0"))));
            evt.SetExtra("pids", string.Join(',', cluster.Records.Select(r => r.Pid)));
            if (cluster.VersionTag is not null)
            {
                evt.SetExtra("dead_build", cluster.VersionTag);
            }

            var newest = cluster.Records[0];

            // The watchdog's own account of the burst is plain text and tiny — attach it when found;
            // never a dump here (that is what makes this event cheap enough to send FIRST).
            var dayDir = CrashCauseDigest.ResolveLogPath(AppPaths.AppDirectory, cluster.VersionTag, cluster.WindowEndUtc.ToLocalTime());

            var id = Sentry.SentrySdk.CaptureEvent(evt, scope =>
            {
                if (dayDir is not null)
                {
                    TryAttachText(scope, Path.Combine(dayDir, "watchdog.log"));
                }

                AttachCause(scope, cluster.VersionTag, newest.DetectedAtUtc, newest.Pid, "fault_digest_loop");
            });
            InteractionLogger.Mark("SentryCrashForward", id != Sentry.SentryId.Empty ? "group-sent" : "group-failed",
                new { id = id.ToString(), deaths = cluster.Count, exit = cluster.DominantExitCode });
            return id != Sentry.SentryId.Empty;
        }
        catch (Exception ex)
        {
            CrashReporter.Report("NativeCrashForwarder.SendCluster", ex, isFatal: false);
            return false;
        }
    }

    private static bool SendDumpEvent(Loaded item, List<string> tempFiles)
    {
        var record = item.Record;
        try
        {
            var uptime = record.UptimeSeconds is double s
                ? TimeSpan.FromSeconds(s).ToString(@"d\.hh\:mm\:ss")
                : "unknown";

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
                Message = NativeCrashIntake.BuildSingleMessage(record),
                Level = Sentry.SentryLevel.Fatal,
            };
            evt.SetTag("crash_type", "native_fail_fast");
            evt.SetTag("post_mortem", "true");
            evt.SetTag("exit_code", record.ExitCodeName);
            evt.SetTag("witnessed_by_watchdog", record.ExitCode is null ? "false" : "true");
            evt.SetFingerprint(NativeCrashIntake.BuildSingleFingerprint(record));
            evt.SetExtra("pid", record.Pid);
            evt.SetExtra("uptime", uptime);
            evt.SetExtra("crash_time_utc", record.DetectedAtUtc.ToString("o"));
            evt.SetExtra("dump_path", record.DumpPath ?? "<none>");
            var crashLocal = record.DetectedAtUtc.ToLocalTime();

            // Prefer the DEAD build's own day dir (its logs describe its death); fall back to the
            // running build's for pre-dev71 records that carry no version tag.
            var dayDir = CrashCauseDigest.ResolveLogPath(AppPaths.AppDirectory, record.VersionTag, crashLocal)
                ?? Path.Combine(AppPaths.BaseLogsDirectory, crashLocal.ToString("yyyy-MM-dd"));
            var gzCopy = gzDump;
            var id = Sentry.SentrySdk.CaptureEvent(evt, scope =>
            {
                if (gzCopy is not null)
                {
                    try { scope.AddAttachment(gzCopy, Sentry.AttachmentType.Default, "application/gzip"); } catch { }
                }

                foreach (var name in new[] { "watchdog.log", "heartbeat.log", "startup-debug.log", "interaction-log.txt", "crash.log" })
                {
                    TryAttachText(scope, Path.Combine(dayDir, name), MaxLogAttachmentBytes);
                }

                AttachCause(scope, record.VersionTag, record.DetectedAtUtc, record.Pid, "fault_digest");
            });

            InteractionLogger.Mark("SentryCrashForward", id != Sentry.SentryId.Empty ? "sent" : "send-failed",
                new { id = id.ToString(), pid = record.Pid, exit = record.ExitCodeName, dump = gzCopy is not null });
            return id != Sentry.SentryId.Empty;
        }
        catch (Exception ex)
        {
            CrashReporter.Report("NativeCrashForwarder.Send", ex, isFatal: false);
            return false;
        }
    }

    /// <summary>
    /// Embeds the dead process's last distinct FAULT lines (the CAUSE) into the scope extras, so
    /// "ArgumentException: An invalid IterationCount is specified" is readable in the Sentry UI
    /// instead of buried in a 12 MB attachment. Best-effort: no dir/pid match simply says so.
    /// </summary>
    private static void AttachCause(Sentry.Scope scope, string? versionTag, DateTime deathUtc, int pid, string extraKey)
    {
        try
        {
            var dayDir = CrashCauseDigest.ResolveLogPath(AppPaths.AppDirectory, versionTag, deathUtc.ToLocalTime());
            if (dayDir is null)
            {
                scope.SetExtra(extraKey, "<no log dir for the dead build>");
                return;
            }

            var lines = CrashCauseDigest.ReadBoundedTail(
                Path.Combine(dayDir, "interaction-log.txt"), CrashCauseDigest.DefaultTailBytes);
            var digest = CrashCauseDigest.Extract(lines, pid, deathUtc - TimeSpan.FromMinutes(2), deathUtc + TimeSpan.FromMinutes(2));
            if (digest.Count == 0)
            {
                scope.SetExtra(extraKey, "<no FAULT lines for this pid in ±2 min of death>");
                return;
            }

            scope.SetExtra(extraKey, string.Join(" || ", digest.Select(f =>
                $"{f.Checkpoint}: {f.ExceptionType}: {f.Message}" + (f.Count > 1 ? $" (x{f.Count})" : ""))));
            scope.SetExtra("digest_source_dir", dayDir);
        }
        catch
        {
            // The digest is legibility, never a requirement for the event.
        }
    }

    private static void TryAttachText(Sentry.Scope scope, string path, long maxBytes = 5 * 1024 * 1024)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= maxBytes)
            {
                scope.AddAttachment(path, Sentry.AttachmentType.Default, "text/plain");
            }
        }
        catch
        {
            // attachment is best-effort
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

    /// <summary>
    /// Ledger of forwarded keys, saved after EVERY success (the old once-at-the-end save lost the
    /// whole batch when the launch died mid-phase). Bounded — worst case a very old crash is
    /// reported twice, never silently dropped.
    /// </summary>
    private static void SaveReported(HashSet<string> reported)
    {
        try
        {
            File.WriteAllLines(ReportedListPath, reported.OrderBy(k => k, StringComparer.Ordinal).TakeLast(400));
        }
        catch
        {
            // worst case a dump is reported twice
        }
    }
}

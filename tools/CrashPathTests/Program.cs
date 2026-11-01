using MostaqlK.Core.Diagnostics;
using MostaqlK.Core.Platform;

var failures = 0;

void Check(string name, bool ok)
{
    if (ok)
    {
        Console.WriteLine($"PASS  {name}");
        return;
    }

    failures++;
    Console.WriteLine($"FAIL  {name}");
}

// --- Retention / midnight UTC (the ~20h crash) --------------------------------

var now = new DateTime(2026, 9, 16, 0, 0, 5, DateTimeKind.Utc);
var root = Path.Combine(Path.GetTempPath(), "mostaqlk-crash-path-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var today = Path.Combine(root, "2026-09-16");
    var yesterday = Path.Combine(root, "2026-09-15");
    var old = Path.Combine(root, "2026-09-01");
    Directory.CreateDirectory(today);
    Directory.CreateDirectory(yesterday);
    Directory.CreateDirectory(old);
    File.WriteAllText(Path.Combine(today, "interaction-log.txt"), "active");
    File.WriteAllText(Path.Combine(yesterday, "old.txt"), "keep 7d");
    File.WriteAllText(Path.Combine(old, "gone.txt"), "purge");

    using (var locked = new FileStream(
               Path.Combine(today, "locked.log"),
               FileMode.Create,
               FileAccess.ReadWrite,
               FileShare.None))
    {
        locked.WriteByte(1);
        locked.Flush();

        LogDirectoryPurger.Purge(root, retentionDays: 7, utcNow: now);

        Check("today dir survives midnight UTC purge", Directory.Exists(today));
        Check("today active log not deleted", File.Exists(Path.Combine(today, "interaction-log.txt")));
        Check("locked today file does not throw", File.Exists(Path.Combine(today, "locked.log")));
        Check("yesterday within 7d kept", Directory.Exists(yesterday));
        Check("8+ day dir purged", !Directory.Exists(old));
    }

    // Soak: thousands of concurrent-ish purges with a locked current-day file.
    var soakNow = now;
    var soakToday = Path.Combine(root, soakNow.ToString("yyyy-MM-dd"));
    Directory.CreateDirectory(soakToday);
    using (var locked = new FileStream(
               Path.Combine(soakToday, "soak.lock"),
               FileMode.Create,
               FileAccess.ReadWrite,
               FileShare.None))
    {
        var threw = 0;
        Parallel.For(0, 2000, _ =>
        {
            try
            {
                LogDirectoryPurger.Purge(root, 7, soakNow);
            }
            catch
            {
                Interlocked.Exchange(ref threw, 1);
            }
        });
        Check("2000 parallel purges with locked today file do not throw", threw == 0);
        Check("today dir still present after soak", Directory.Exists(soakToday));
    }

    // Date rollover: clock jumps to next UTC day while "previous today" still has a lock.
    var nextDay = now.AddDays(1);
    var previousToday = Path.Combine(root, now.ToString("yyyy-MM-dd"));
    var newToday = Path.Combine(root, nextDay.ToString("yyyy-MM-dd"));
    Directory.CreateDirectory(previousToday);
    Directory.CreateDirectory(newToday);
    File.WriteAllText(Path.Combine(newToday, "new.log"), "ok");
    using (var locked = new FileStream(
               Path.Combine(previousToday, "rollover.lock"),
               FileMode.Create,
               FileAccess.ReadWrite,
               FileShare.None))
    {
        var threw = false;
        try
        {
            LogDirectoryPurger.Purge(root, retentionDays: 1, utcNow: nextDay);
        }
        catch
        {
            threw = true;
        }

        Check("UTC day rollover with locked previous-day file does not throw", !threw);
        Check("new today dir never deleted on rollover", Directory.Exists(newToday));
    }
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

// --- Radar / DXGI (the ~2h crash) --------------------------------------------

Check("wake blocked in background",
    !BackgroundCanvasPolicy.AllowWake(isFrozen: false, isTicking: false, hasHandler: true, isInBackground: true));
Check("wake allowed in foreground",
    BackgroundCanvasPolicy.AllowWake(isFrozen: false, isTicking: false, hasHandler: true, isInBackground: false));
Check("wake blocked while frozen",
    !BackgroundCanvasPolicy.AllowWake(isFrozen: true, isTicking: false, hasHandler: true, isInBackground: false));
Check("frame blocked in background (no swapchain invalidate)",
    !BackgroundCanvasPolicy.AllowFrame(isInBackground: true));
Check("frame allowed in foreground",
    BackgroundCanvasPolicy.AllowFrame(isInBackground: false));

var framesWouldInvalidate = 0;
for (var i = 0; i < 60 * 60 * 2; i++) // 2 hours at 1 "would-be" fps check
{
    if (BackgroundCanvasPolicy.AllowFrame(isInBackground: true))
    {
        framesWouldInvalidate++;
    }
}
Check("2h simulated background ticks never invalidate canvas", framesWouldInvalidate == 0);

// --- Native filter policy ----------------------------------------------------

Check("CLR exception skips minidump",
    !NativeExceptionPolicy.ShouldWriteMinidump(NativeExceptionPolicy.ClrManagedException));
Check("AV still dumps",
    NativeExceptionPolicy.ShouldWriteMinidump(NativeExceptionPolicy.StatusAccessViolation));
Check("DXGI device-removed is named",
    NativeExceptionPolicy.NameFor(NativeExceptionPolicy.DxgiErrorDeviceRemoved).Contains("DXGI", StringComparison.Ordinal));
Check("stowed exception is named",
    NativeExceptionPolicy.NameFor(NativeExceptionPolicy.StatusStowedException).Contains("STOWED", StringComparison.Ordinal));

// --- Watchdog (native fail-fast relaunch) ------------------------------------

Check("watchdog args parsed",
    WatchdogPolicy.TryParseWatchdogArgs(new[] { WatchdogPolicy.WatchdogArg, "1234" }, out var wpid) && wpid == 1234);
Check("normal launch is not a watchdog",
    !WatchdogPolicy.TryParseWatchdogArgs(new[] { "--silent-start" }, out _));
Check("bad pid is not a watchdog",
    !WatchdogPolicy.TryParseWatchdogArgs(new[] { WatchdogPolicy.WatchdogArg, "abc" }, out _));
Check("primary spawns watchdog",
    WatchdogPolicy.ShouldSpawnWatchdog(Array.Empty<string>(), isDebuggerAttached: false, disableEnvValue: null));
Check("supervised relaunch does not spawn a second watchdog",
    !WatchdogPolicy.ShouldSpawnWatchdog(WatchdogPolicy.RelaunchArgs(), false, null));
Check("debugger suppresses watchdog",
    !WatchdogPolicy.ShouldSpawnWatchdog(Array.Empty<string>(), true, null));
Check("env var suppresses watchdog",
    !WatchdogPolicy.ShouldSpawnWatchdog(Array.Empty<string>(), false, "1"));
var wnow = new DateTime(2026, 10, 19, 13, 49, 35, DateTimeKind.Utc);
Check("fail-fast is relaunched",
    WatchdogPolicy.Decide(false, false, Array.Empty<DateTime>(), wnow) == WatchdogDecision.Relaunch);
Check("intentional exit is not relaunched",
    WatchdogPolicy.Decide(true, false, Array.Empty<DateTime>(), wnow) == WatchdogDecision.StopCleanExit);
Check("system shutdown is not relaunched",
    WatchdogPolicy.Decide(false, true, Array.Empty<DateTime>(), wnow) == WatchdogDecision.StopSystemShutdown);
Check("crash loop stops relaunching",
    WatchdogPolicy.Decide(false, false, Enumerable.Range(1, WatchdogPolicy.MaxRestartsInWindow).Select(i => wnow.AddMinutes(-i)).ToArray(), wnow) == WatchdogDecision.StopCrashLoop);
Check("old restarts outside window do not count",
    WatchdogPolicy.Decide(false, false, Enumerable.Range(1, 10).Select(i => wnow.AddHours(-i)).ToArray(), wnow) == WatchdogDecision.Relaunch);
Check("relaunch goes back to tray, supervised",
    WatchdogPolicy.RelaunchArgs() is ["--silent-start", WatchdogPolicy.SupervisedArg]);

// --- Portable single-file extract (TEMP eviction) ----------------------------

var tempRoot = Path.Combine(Path.GetTempPath(), "mostaqlk-extract-policy-" + Guid.NewGuid().ToString("N"));
var volatileExtract = Path.Combine(tempRoot, ".net", "MostaqlK", "9fUI4XvOp5uG");
var persistentExtract = Path.Combine(tempRoot, "Local", "MostaqlK", "bundle-cache");
Directory.CreateDirectory(volatileExtract);
Directory.CreateDirectory(persistentExtract);
try
{
    Check("TEMP\\.net extract is volatile",
        BundleExtractPolicy.IsVolatileExtractDirectory(volatileExtract, tempRoot));
    Check("LocalAppData extract is not volatile",
        !BundleExtractPolicy.IsVolatileExtractDirectory(persistentExtract, tempRoot));
    Check("portable first launch must relaunch out of TEMP",
        BundleExtractPolicy.ShouldRelaunch(volatileExtract, tempRoot, persistentExtract, alreadyRelocated: false));
    Check("second process after relocate flag does not loop",
        !BundleExtractPolicy.ShouldRelaunch(volatileExtract, tempRoot, persistentExtract, alreadyRelocated: true));
    Check("already-persistent extract does not relaunch",
        !BundleExtractPolicy.ShouldRelaunch(Path.Combine(persistentExtract, "token"), tempRoot, persistentExtract, alreadyRelocated: false));
    Check("relocate arg is detected",
        BundleExtractPolicy.HasRelocateArg(new[] { "--silent-start", BundleExtractPolicy.RelocateArg }));
    Check("relocate arg stripped for child argv",
        BundleExtractPolicy.ArgsWithoutRelocate(new[] { BundleExtractPolicy.RelocateArg, "--silent-start" }) is ["--silent-start"]);
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

// --- Handled-exception reporting policy (the swallowed-throw funnel) ---------

HandledReportingPolicy.ResetForTests();
var hEx = new InvalidOperationException("synthetic radar throw");
var t0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
Check("policy: first three per key report",
    HandledReportingPolicy.Decide("Radar.Start", hEx, t0) == HandledReportDecision.Report &&
    HandledReportingPolicy.Decide("Radar.Start", hEx, t0.AddSeconds(1)) == HandledReportDecision.Report &&
    HandledReportingPolicy.Decide("Radar.Start", hEx, t0.AddSeconds(2)) == HandledReportDecision.Report);
Check("policy: fourth within window suppresses",
    HandledReportingPolicy.Decide("Radar.Start", hEx, t0.AddSeconds(3)) == HandledReportDecision.Suppress);
Check("policy: re-report after quiet interval carries suppressed count",
    HandledReportingPolicy.Decide("Radar.Start", hEx, t0.AddMinutes(11)) == HandledReportDecision.ReportWithSuppressed &&
    HandledReportingPolicy.TakeSuppressedCount("Radar.Start", hEx) == 1);
Check("policy: first-chance firehose is exempt",
    HandledReportingPolicy.Decide("FirstChance", hEx, t0) == HandledReportDecision.Exempt &&
    HandledReportingPolicy.IsExempt("AppDomain.UnhandledException"));
Check("policy: same exception object forwards once",
    HandledReportingPolicy.TryMarkExceptionForwarded(hEx) &&
    !HandledReportingPolicy.TryMarkExceptionForwarded(hEx));
Check("policy: distinct equal exceptions both forward",
    HandledReportingPolicy.TryMarkExceptionForwarded(new InvalidOperationException("synthetic radar throw")) &&
    HandledReportingPolicy.TryMarkExceptionForwarded(new InvalidOperationException("synthetic radar throw")));
HandledReportingPolicy.ResetForTests();
var ceilingKeyOk = true;
for (var i = 0; i < HandledReportingPolicy.PerProcessReportCeiling; i++)
{
    ceilingKeyOk &= HandledReportingPolicy.Decide("Storm" + i, new Exception("x"), t0) == HandledReportDecision.Report;
}
Check("policy: per-process ceiling evicts nothing further",
    ceilingKeyOk && HandledReportingPolicy.Decide("StormAfterCeiling", new Exception("y"), t0) == HandledReportDecision.Suppress);
HandledReportingPolicy.ResetForTests();

// --- Native crash intake: the REAL Oct-31 crash-loop backlog ------------------
// 8 witnessed deaths 20:49–21:13Z, uptimes ~48–143 s, codes 0xFFFFFFFF/0x00000001.
PendingCrashRecord[] Oct31Backlog()
{
    var pids = new[] { 26524, 13248, 22820, 5360, 14552, 24792, 15036, 14420 };
    var codes = new[] { "0xFFFFFFFF", "0xFFFFFFFF", "0x00000001", "0xFFFFFFFF", "0xFFFFFFFF", "0xFFFFFFFF", "0x00000001", "0x00000001" };
    var uptimes = new double[] { 60, 55, 143, 50, 65, 82, 21, 90 };
    var list = new List<PendingCrashRecord>();
    for (var i = 0; i < pids.Length; i++)
    {
        list.Add(new PendingCrashRecord
        {
            Pid = pids[i],
            ExitCode = codes[i] == "0xFFFFFFFF" ? -1 : 1,
            ExitCodeName = codes[i],
            UptimeSeconds = uptimes[i],
            DumpPath = $"C:\\dumps\\MostaqlK.exe.{pids[i]}.dmp",
            DetectedAtUtc = new DateTime(2026, 10, 31, 20, 49, 0, DateTimeKind.Utc).AddMinutes(i * 3 + 1),
            VersionTag = "1.0.4.dev69+06374f3",
        });
    }

    return list.ToArray();
}

var backlog = Oct31Backlog();
var launchNow = new DateTime(2026, 10, 31, 21, 18, 0, DateTimeKind.Utc);
var clusters = NativeCrashIntake.Cluster(backlog, NativeCrashIntake.ClusterWindow, NativeCrashIntake.LoopThreshold);
Check("intake: 8 deaths in 25 min form ONE cluster", clusters.Count == 1 && clusters[0].Count == 8);
Check("intake: cluster is a LOOP at threshold 3", clusters[0].IsLoop);
Check("intake: exit-code histogram counts both codes",
    clusters[0].ExitCodeHistogram["0xFFFFFFFF"] == 5 && clusters[0].ExitCodeHistogram["0x00000001"] == 3);
Check("intake: uptime spread 21-143 s", clusters[0].MinUptimeSeconds == 21 && clusters[0].MaxUptimeSeconds == 143);
var loopMessage = NativeCrashIntake.BuildMessage(clusters[0]);
Check("intake: loop message is legible alone",
    loopMessage.Contains("LOOP", StringComparison.Ordinal) &&
    loopMessage.Contains("8 deaths", StringComparison.Ordinal) &&
    loopMessage.Contains("0xFFFFFFFF x5", StringComparison.Ordinal));
Check("intake: loop fingerprint is version-scoped",
    NativeCrashIntake.BuildFingerprint(clusters[0]) is ["native-fail-fast-loop", "0xFFFFFFFF", "1.0.4.dev69+06374f3"]);
Check("intake: window split at 30 min makes two clusters",
    NativeCrashIntake.Cluster(backlog.Append(new PendingCrashRecord
    {
        Pid = 1,
        ExitCodeName = "0x1",
        DumpPath = "C:\\dumps\\other.dmp",
        DetectedAtUtc = launchNow.AddHours(-2),
    }).ToArray(), NativeCrashIntake.ClusterWindow, NativeCrashIntake.LoopThreshold).Count == 2);

var reported = new List<string>();
// Decide is pure: the CALLER spends the budget (the forwarder's loop shape, pinned here).
var backlogActions = new List<IntakeAction>();
var budgetSpent = 0;
foreach (var r in backlog)
{
    var a = NativeCrashIntake.Decide(r, launchNow, reported, budgetSpent);
    if (a == IntakeAction.Send && r.DumpPath is not null)
    {
        budgetSpent++;
    }

    backlogActions.Add(a);
}
Check("intake: first three dump-carrying deaths send", backlogActions.Count(a => a == IntakeAction.Send) == 3);
Check("intake: the other FIVE defer — NEVER drop (the bug that ate 5 of 8 records)",
    backlogActions.Count(a => a == IntakeAction.DeferBudget) == 5 &&
    backlogActions.All(a => a is not IntakeAction.DropDuplicate and not IntakeAction.DropTooOld));
Check("intake: reported key dedupes to delete-safe DropDuplicate",
    NativeCrashIntake.Decide(backlog[0], launchNow, new[] { NativeCrashIntake.KeyFor(backlog[0]) }, 0) == IntakeAction.DropDuplicate);
Check("intake: 8-day-old record ages out (its log attachments are already purged)",
    NativeCrashIntake.Decide(backlog[0], backlog[0].DetectedAtUtc.AddDays(8), reported, 0) == IntakeAction.DropTooOld);
Check("intake: dumpless witness record still sends (message-only event survives)",
    NativeCrashIntake.Decide(new PendingCrashRecord { Pid = 23368, ExitCodeName = "0xFFFFFFFF", DetectedAtUtc = launchNow },
        launchNow, reported, NativeCrashIntake.MaxDumpsPerLaunch) == IntakeAction.Send);
Check("intake: dumpless key falls back to pid+time",
    NativeCrashIntake.KeyFor(new PendingCrashRecord { Pid = 7 }).StartsWith("pid-7-", StringComparison.Ordinal));

// --- Crash cause digest: reading the CAUSE out of the dead build's own log ----

var digestRoot = Path.Combine(Path.GetTempPath(), "mostaqlk-digest-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(digestRoot);
    var sample = new[]
    {
        // real line shape (InteractionLogger.Write): local timestamp WITH offset, pid-tagged
        "2026-10-31T22:55:43.8187640+02:00 | pid=15036 | FAULT | FirstChance | data={\"Type\":\"System.ArgumentException\"} | exception=ArgumentException: The parameter is incorrect.",
        "2026-10-31T22:55:44.0000001+02:00 | pid=15036 | MARK | EnrichmentWorker.AttemptStart | variant=D",
        "2026-10-31T22:55:45.0000000+02:00 | pid=99999 | FAULT | Radar.Start | exception=ArgumentException: different pid line",
        "2026-10-31T20:00:00.0000000+02:00 | pid=15036 | FAULT | Radar.Start | exception=ArgumentException: outside window",
        "continuation of a multi-line message, must be skipped not guessed",
        "2026-10-31T22:56:00.0000000+02:00 | pid=15036 | FAULT | FirstChance | data={\"Type\":\"System.ArgumentException\"} | exception=ArgumentException: The parameter is incorrect.",
    };

    // 22:55:43+02:00 == 20:55:43 UTC; window 20:53–20:57 UTC must include it (kills a naive
    // parse-As-UTC bug: without ToUniversalTime the +02:00 stamp looks 2 hours out of range).
    var found = CrashCauseDigest.Extract(
        sample, 15036,
        new DateTime(2026, 10, 31, 20, 53, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 31, 20, 57, 0, DateTimeKind.Utc));
    Check("digest: +02:00 stamp inside UTC window is found", found.Count >= 1);
    Check("digest: counts identical faults",
        found.Any(f => f.Count == 2 && f.ExceptionType == "ArgumentException"));
    Check("digest: foreign pid excluded", found.All(f => f.Checkpoint != "Radar.Start" || f.Message != "different pid line"));
    Check("digest: out-of-window fault excluded", found.All(f => f.Message != "outside window"));
    Check("digest: unparseable continuation line skipped", found.All(f => f.Message.Length < 300));
    Check("digest: maxDistinct caps the list",
        CrashCauseDigest.Extract(sample, 15036,
            new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), 1).Count == 1);

    var big = Path.Combine(digestRoot, "big.log");
    using (var w = new StreamWriter(big))
    {
        var filler = new string('x', 200);
        for (var i = 0; i < 12_000; i++)
        {
            w.WriteLine(filler);
        }
    }

    var tail = CrashCauseDigest.ReadBoundedTail(big, CrashCauseDigest.DefaultTailBytes);
    Check("digest: bounded tail never slurps the whole file", tail.Count > 0 && tail.Count <= CrashCauseDigest.MaxLines);
    Check("digest: missing file returns empty, never throws", CrashCauseDigest.ReadBoundedTail(Path.Combine(digestRoot, "nope.log"), 1024).Count == 0);

    // ResolveLogPath: version dir wins, sibling fallback, none → null.
    var chanRoot = Path.Combine(digestRoot, "channel");
    Directory.CreateDirectory(Path.Combine(chanRoot, "1.0.4.dev70+abc", "log", "2026-10-31"));
    Directory.CreateDirectory(Path.Combine(chanRoot, "Settings"));
    Check("digest: version-tag day dir resolves",
        CrashCauseDigest.ResolveLogPath(chanRoot, "1.0.4.dev70+abc", new DateTime(2026, 10, 31)) is not null);
    Check("digest: stale version tag falls back to a sibling that has the day",
        CrashCauseDigest.ResolveLogPath(chanRoot, "1.0.4.dev99+zzz", new DateTime(2026, 10, 31)) is not null);
    Check("digest: no day anywhere resolves to null (digest skipped, not faked)",
        CrashCauseDigest.ResolveLogPath(chanRoot, "1.0.4.dev70+abc", new DateTime(2025, 1, 1)) is null);
}
finally
{
    try { Directory.Delete(digestRoot, recursive: true); } catch { }
}

// --- Finalizer-flood alarm (postmortem A3): replay the incident's REAL beats --

Check("flood: first spike above threshold is not yet an alarm",
    !FinalizerFloodPolicy.IsSustaining(2277, 6138));
Check("flood: a frozen 6,138 (the dev69 death pattern) sustains",
    FinalizerFloodPolicy.IsSustaining(6138, 6138));
Check("flood: the dev70 soak drain (9,350 -> 214) never alarms",
    !FinalizerFloodPolicy.IsSustaining(9350, 214));
Check("flood: boundary — half exactly is drained, just above is not",
    !FinalizerFloodPolicy.IsSustaining(4000, 2000) && FinalizerFloodPolicy.IsSustaining(4000, 2001));
Check("flood: below threshold never sustains",
    !FinalizerFloodPolicy.IsSustaining(3999, 3999));
Check("flood: episode resets only under half the threshold (hysteresis)",
    FinalizerFloodPolicy.EpisodeOver(1999) && !FinalizerFloodPolicy.EpisodeOver(3000));

// --- First-chance storm detector (the net under the catch audit) --------------

FirstChanceStormPolicy.ResetForTests();
var stormKey = FirstChanceStormPolicy.KeyFor(new InvalidOperationException("repeated swallowed setter throw"));
Check("storm: key is type + trimmed message prefix",
    stormKey.StartsWith("System.InvalidOperationException|", StringComparison.Ordinal));
var promotions = 0;
var sNow = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
for (var i = 0; i < 60; i++)
{
    if (FirstChanceStormPolicy.NoteThrow(stormKey, sNow.AddSeconds(i)))
    {
        promotions++;
    }
}
Check("storm: 60 repeats in one window promote exactly once", promotions == 1);
// Window expiry resets the episode: one throw past the window is count #1 again, and the
// threshold-th throw of the NEW window promotes a second time.
var t = sNow.AddSeconds(300);
var secondEpisode = false;
for (var i = 0; i <= FirstChanceStormPolicy.StormCountThreshold; i++)
{
    secondEpisode |= FirstChanceStormPolicy.NoteThrow(stormKey, t.AddSeconds(i));
}
Check("storm: a fresh window lets the same key storm again", secondEpisode);
FirstChanceStormPolicy.ResetForTests();
// Fill the tracker to the cap with near-threshold keys (none promote), then verify an entirely
// new key is ignored once the cap is reached — the memory bound is the assertion.
for (var i = 0; i < FirstChanceStormPolicy.MaxTrackedKeys; i++)
{
    for (var r = 0; r < FirstChanceStormPolicy.StormCountThreshold - 1; r++)
    {
        if (FirstChanceStormPolicy.NoteThrow("Flood" + i, sNow))
        {
            Check("storm: filler keys must not promote", false);
        }
    }
}
var overCapPromoted = false;
for (var r = 0; r < FirstChanceStormPolicy.StormCountThreshold; r++)
{
    overCapPromoted |= FirstChanceStormPolicy.NoteThrow("BeyondCap", sNow);
}
Check("storm: keys beyond the tracked cap never promote (bounded memory)", !overCapPromoted);
FirstChanceStormPolicy.ResetForTests();

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine("ALL CRASH-PATH CHECKS PASSED");
    return 0;
}

Console.WriteLine($"{failures} CHECK(S) FAILED");
return 1;

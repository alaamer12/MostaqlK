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

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine("ALL CRASH-PATH CHECKS PASSED");
    return 0;
}

Console.WriteLine($"{failures} CHECK(S) FAILED");
return 1;

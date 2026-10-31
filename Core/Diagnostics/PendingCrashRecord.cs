namespace MostaqlK.Core.Diagnostics;

/// <summary>
/// One witnessed native death, persisted as JSON by the watchdog in the channel's
/// <c>pending-crashes</c> folder and consumed post-mortem by the crash forwarder. Lives in Core
/// (not the Windows platform folder) so the intake decisions around it are testable headlessly.
/// Serialization uses <see cref="System.Text.Json.JsonSerializer"/> DEFAULT options and property
/// names must never be renamed — records written by older builds stay on disk across upgrades.
/// </summary>
public sealed class PendingCrashRecord
{
    public int Pid { get; set; }

    /// <summary>Raw process exit code as the OS reported it; null when the witness could not read it.</summary>
    public int? ExitCode { get; set; }

    /// <summary><see cref="NativeExceptionPolicy.NameFor"/> rendering of the exit code (or a fallback marker).</summary>
    public string ExitCodeName { get; set; } = "";

    public double? UptimeSeconds { get; set; }

    /// <summary>WER minidump path, or null when no dump was found for the dead pid.</summary>
    public string? DumpPath { get; set; }

    public DateTime DetectedAtUtc { get; set; }

    /// <summary>
    /// Version tag of the DEAD build (its per-version log folder is where the day's interaction /
    /// heartbeat logs live). Nullable: records written before dev71 lack it, and the forwarder
    /// falls back to the running build's log directory for those.
    /// </summary>
    public string? VersionTag { get; set; }
}

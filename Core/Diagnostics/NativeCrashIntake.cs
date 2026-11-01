namespace MostaqlK.Core.Diagnostics;

using System.Globalization;
using System.Text;

/// <summary>What the crash forwarder may do with one pending record on a given launch.</summary>
public enum IntakeAction
{
    /// <summary>Forward it now (this path is the ONLY one that may delete the record after success).</summary>
    Send,

    /// <summary>Launch budget spent — keep the file, try again next launch (the fix for silent backlog loss).</summary>
    DeferBudget,

    /// <summary>Proven already forwarded (key in the reported ledger); safe to delete.</summary>
    DropDuplicate,

    /// <summary>Older than the log retention it would cite; the attachments would be empty shells.</summary>
    DropTooOld,
}

/// <summary>
/// A burst of native deaths inside <see cref="NativeCrashIntake.ClusterWindow"/>. Three or more
/// (<see cref="NativeCrashIntake.LoopThreshold"/>) is a crash LOOP and becomes ONE legible event —
/// a per-dump pile of identical messages is what made the October loop look like a bad day instead
/// of a broken build.
/// </summary>
public readonly record struct CrashCluster(
    IReadOnlyList<PendingCrashRecord> Records,
    IReadOnlyDictionary<string, int> ExitCodeHistogram,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    double? MinUptimeSeconds,
    double? MaxUptimeSeconds,
    string? VersionTag)
{
    public int Count => Records.Count;

    public bool IsLoop => Records.Count >= NativeCrashIntake.LoopThreshold;

    /// <summary>Exit-code name carried by the most deaths (tie: first seen in the newest-first order).</summary>
    public string DominantExitCode
    {
        get
        {
            string best = "";
            int bestCount = 0;
            foreach (var pair in ExitCodeHistogram)
            {
                if (pair.Value > bestCount)
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }

            return best;
        }
    }
}

/// <summary>
/// Pure intake decisions for post-mortem crash forwarding: clustering, the per-launch dump budget,
/// dedupe, and the event text/fingerprints. Isolated from Windows (no WER, no Sentry, no IO) so
/// CrashPathTests can replay a real crash-loop backlog and prove nothing is silently deleted —
/// exactly the failure the October incident showed (5 of 8 witnessed deaths were dropped).
/// </summary>
public static class NativeCrashIntake
{
    /// <summary>Deaths inside the window that make it a loop worth one grouped event.</summary>
    public const int LoopThreshold = 3;

    /// <summary>Sliding window for clustering — deliberately the same span as the watchdog's crash-loop guard.</summary>
    public static readonly TimeSpan ClusterWindow = TimeSpan.FromMinutes(30);

    /// <summary>Gzipped dump ATTACHMENTS forwarded per launch (dumps are ~12 MB each, minigun-proofing).</summary>
    public const int MaxDumpsPerLaunch = 3;

    /// <summary>Pending records older than this are dropped: the day-log attachments are already purged.</summary>
    public static readonly TimeSpan MaxRecordAge = TimeSpan.FromDays(7);

    /// <summary>
    /// Dedupe key, format-compatible with the pre-dev71 reported ledger on disk (name|bytes|mtime
    /// for dumps — byte-identity makes a moved/renamed dump a NEW event, which is the old, proven
    /// rule; pid+time fallback for deaths WER never dumped).
    /// </summary>
    public static string KeyFor(PendingCrashRecord record)
    {
        if (record.DumpPath is null)
        {
            return $"pid-{record.Pid}-{record.DetectedAtUtc:o}";
        }

        try
        {
            var info = new FileInfo(record.DumpPath);
            return $"{info.Name}|{info.Length}|{info.LastWriteTimeUtc:o}";
        }
        catch
        {
            return record.DumpPath;
        }
    }

    /// <summary>Identity of a whole cluster in the reported ledger (a loop is one event, sent once).</summary>
    public static string ClusterKey(CrashCluster cluster) =>
        $"cluster|{cluster.WindowStartUtc:o}|{cluster.WindowEndUtc:o}|{cluster.Count}";

    /// <summary>
    /// Groups records into bursts: records sorted newest-first, split wherever consecutive deaths
    /// are more than <paramref name="window"/> apart. Clusters come back newest-first.
    /// </summary>
    public static IReadOnlyList<CrashCluster> Cluster(
        IReadOnlyList<PendingCrashRecord> records,
        TimeSpan window,
        int loopThreshold)
    {
        ArgumentNullException.ThrowIfNull(records);

        var ordered = new List<PendingCrashRecord>(records);
        ordered.Sort(static (a, b) => b.DetectedAtUtc.CompareTo(a.DetectedAtUtc));

        var clusters = new List<CrashCluster>();
        var run = new List<PendingCrashRecord>();
        foreach (var record in ordered)
        {
            if (run.Count > 0 && run[^1].DetectedAtUtc - record.DetectedAtUtc > window)
            {
                clusters.Add(Build(run, loopThreshold));
                run.Clear();
            }

            run.Add(record);
        }

        if (run.Count > 0)
        {
            clusters.Add(Build(run, loopThreshold));
        }

        return clusters;
    }

    private static CrashCluster Build(List<PendingCrashRecord> run, int loopThreshold)
    {
        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
        double? min = null;
        double? max = null;
        string? version = null;
        foreach (var r in run)
        {
            var name = string.IsNullOrEmpty(r.ExitCodeName) ? "<unknown>" : r.ExitCodeName;
            histogram[name] = histogram.GetValueOrDefault(name) + 1;
            if (r.UptimeSeconds is double s)
            {
                min = min is null || s < min ? s : min;
                max = max is null || s > max ? s : max;
            }

            version ??= r.VersionTag;
        }

        return new CrashCluster(
            run.AsReadOnly(),
            histogram,
            run[^1].DetectedAtUtc,
            run[0].DetectedAtUtc,
            min,
            max,
            version);
    }

    /// <summary>
    /// Decides one record's fate on this launch. Order is load-bearing: age-out and duplicate are
    /// proven facts (delete-safe); budget is uncertainty (keep for later); only
    /// <see cref="IntakeAction.Send"/> followed by a successful forward may delete.
    /// </summary>
    public static IntakeAction Decide(
        PendingCrashRecord record,
        DateTime nowUtc,
        IReadOnlyCollection<string> alreadyReported,
        int dumpAttachmentsSentSoFar)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (nowUtc - record.DetectedAtUtc > MaxRecordAge)
        {
            return IntakeAction.DropTooOld;
        }

        if (alreadyReported.Contains(KeyFor(record)))
        {
            return IntakeAction.DropDuplicate;
        }

        var carriesDump = record.DumpPath is not null;
        if (carriesDump && dumpAttachmentsSentSoFar >= MaxDumpsPerLaunch)
        {
            return IntakeAction.DeferBudget;
        }

        return IntakeAction.Send;
    }

    /// <summary>Grouped-event text; contains the loop count so the issue title alone says "crash loop".</summary>
    public static string BuildMessage(CrashCluster cluster)
    {
        var span = cluster.WindowEndUtc - cluster.WindowStartUtc;
        var sb = new StringBuilder();
        if (cluster.Count >= NativeCrashIntake.LoopThreshold)
        {
            sb.Append("Native fail-fast LOOP: ").Append(cluster.Count).Append(" deaths in ")
              .Append((int)Math.Ceiling(span.TotalMinutes)).Append(" min");
        }
        else
        {
            sb.Append("Native fail-fast burst: ").Append(cluster.Count).Append(" deaths in ")
              .Append((int)Math.Ceiling(span.TotalMinutes)).Append(" min");
        }

        sb.Append(" [crash-loop] codes:");
        foreach (var pair in cluster.ExitCodeHistogram.OrderByDescending(p => p.Value))
        {
            sb.Append(' ').Append(pair.Key).Append(" x").Append(pair.Value);
        }

        if (cluster.MinUptimeSeconds is double min && cluster.MaxUptimeSeconds is double max)
        {
            sb.Append(", uptimes ")
              .Append(min.ToString("0", CultureInfo.InvariantCulture)).Append('-')
              .Append(max.ToString("0", CultureInfo.InvariantCulture)).Append(" s");
        }

        return sb.ToString();
    }

    /// <summary>Single-death event text (same shape the forwarder used before, kept stable for history).</summary>
    public static string BuildSingleMessage(PendingCrashRecord record)
    {
        var uptime = record.UptimeSeconds is double s
            ? TimeSpan.FromSeconds(s).ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture)
            : "unknown";
        return $"Native crash (fail-fast) of previous session: {record.ExitCodeName}, uptime {uptime}";
    }

    /// <summary>
    /// Grouped fingerprint. Carries the version tag on purpose: a NEW loop on a NEW build must not
    /// hide inside the old issue's history — that is how "the app is crash-looping" becomes visible
    /// the same hour it starts. Deliberately NOT carrying the window start: one loop = one issue.
    /// </summary>
    public static string[] BuildFingerprint(CrashCluster cluster) =>
        cluster.IsLoop
            ? ["native-fail-fast-loop", cluster.DominantExitCode, cluster.VersionTag ?? "unknown-version"]
            : ["native-fail-fast-burst", cluster.DominantExitCode, cluster.VersionTag ?? "unknown-version"];

    /// <summary>Per-death fingerprint: same grouping rule — version-scoped, code-scoped, time-agnostic.</summary>
    public static string[] BuildSingleFingerprint(PendingCrashRecord record) =>
        ["native-fail-fast", record.ExitCodeName, record.VersionTag ?? "unknown-version"];
}

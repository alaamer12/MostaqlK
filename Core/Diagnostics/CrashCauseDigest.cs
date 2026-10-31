namespace MostaqlK.Core.Diagnostics;

using System.Globalization;

/// <summary>One distinct fault line, as the dead process recorded it.</summary>
public readonly record struct FaultSummary(string Checkpoint, string ExceptionType, string Message, int Count);

/// <summary>
/// Pulls the CAUSE out of a dead instance's own interaction log: the last distinct
/// <c>FAULT</c> lines around the moment of death. The point is legibility — a Sentry event whose
/// only content is "native crash, uptime 82 s" makes the reader open a 12 MB dump; an event whose
/// details show "ArgumentException: An invalid IterationCount is specified" names the defect in
/// the UI. Pure parsing over caller-supplied lines/paths (no Sentry, no folder discovery) so the
/// format contract is testable — and pinned by a ParserTests cross-file check, because a log
/// format change would otherwise silently rot the digest to zero findings forever.
/// </summary>
public static class CrashCauseDigest
{
    public const int DefaultTailBytes = 256 * 1024;
    public const int MaxTailBytes = 1024 * 1024;
    public const int MaxLines = 4000;
    public const int MessageTrim = 200;

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> from the END of the file (a shared log can be
    /// hundreds of KB and every instance of the channel appends to it — never read it whole).
    /// The first possibly-partial line is discarded. Returns "" for a missing/unreadable file.
    /// </summary>
    public static IReadOnlyList<string> ReadBoundedTail(string path, int maxBytes)
    {
        var cap = Math.Clamp(maxBytes, 1, MaxTailBytes);
        try
        {
            if (!File.Exists(path))
            {
                return Array.Empty<string>();
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var take = (long)Math.Min(cap, fs.Length);
            fs.Seek(fs.Length - take, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            var text = reader.ReadToEnd();
            var cut = text.IndexOf('\n');
            if (take < fs.Length && cut >= 0)
            {
                text = text[(cut + 1)..];
            }

            var lines = text.Split('\n');
            var end = lines.Length;
            if (end > 0 && lines[^1].Length == 0)
            {
                end--;
            }

            var kept = Math.Max(0, end - MaxLines);
            var result = new List<string>(end - kept);
            for (var i = kept; i < end; i++)
            {
                result.Add(lines[i].TrimEnd('\r'));
            }

            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Selects FAULT lines of one pid inside a UTC window and folds identical (checkpoint, type,
    /// first message line) entries into counted summaries, newest first, capped at
    /// <paramref name="maxDistinct"/>. Lines that do not parse (embed newlines from messages,
    /// foreign formats) are skipped, never guessed at.
    /// </summary>
    public static IReadOnlyList<FaultSummary> Extract(
        IEnumerable<string> lines,
        int pid,
        DateTime fromUtc,
        DateTime toUtc,
        int maxDistinct = 3)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var found = new List<FaultSummary>();
        foreach (var line in lines.Reverse())
        {
            if (!TryParse(line, out var ts, out var linePid, out var kind, out var checkpoint, out var exceptionPart))
            {
                continue;
            }

            if (kind != "FAULT" || linePid != pid)
            {
                continue;
            }

            var utc = ts.ToUniversalTime();
            if (utc < fromUtc || utc > toUtc)
            {
                continue;
            }

            string type = "";
            string message = exceptionPart;
            var colon = exceptionPart.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0)
            {
                type = exceptionPart[..colon];
                message = exceptionPart[(colon + 2)..];
            }

            if (message.Length > MessageTrim)
            {
                message = message[..MessageTrim] + "…";
            }

            var index = found.FindIndex(f => f.Checkpoint == checkpoint && f.ExceptionType == type && f.Message == message);
            if (index >= 0)
            {
                var f = found[index];
                found[index] = f with { Count = f.Count + 1 };
                continue;
            }

            found.Add(new FaultSummary(checkpoint, type, message, 1));
            if (found.Count >= maxDistinct)
            {
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// Parses the exact <c>InteractionLogger</c> line shape:
    /// <c>{ DateTimeOffset:O } | pid=N | KIND | checkpoint [| variant=V | data=… | exception=Type: msg]</c>.
    /// Timestamps are LOCAL-with-offset in the file; comparisons stay in UTC via <see cref="DateTimeOffset.ToUniversalTime"/>.
    /// </summary>
    public static bool TryParse(
        string line,
        out DateTimeOffset timestamp,
        out int pid,
        out string kind,
        out string checkpoint,
        out string exceptionPart)
    {
        timestamp = default;
        pid = 0;
        kind = "";
        checkpoint = "";
        exceptionPart = "";

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var parts = line.Split(" | ", StringSplitOptions.None);
        if (parts.Length < 4)
        {
            return false;
        }

        // The timestamp must parse strictly: a continuation line (an embedded newline from a
        // multi-line exception message) starts with arbitrary text and must fall out here, not
        // be guessed at. The "O" round-trip format never contains " | ", so field splitting is safe.
        if (!DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp))
        {
            return false;
        }

        if (!parts[1].StartsWith("pid=", StringComparison.Ordinal) || !int.TryParse(parts[1].AsSpan(4), out pid))
        {
            return false;
        }

        kind = parts[2];
        checkpoint = parts[3];
        foreach (var part in parts)
        {
            if (part.StartsWith("exception=", StringComparison.Ordinal))
            {
                exceptionPart = part["exception=".Length..];
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves the DEAD build's day-log directory: <c>{channelRoot}\{versionTag}\log\{yyyy-MM-dd}</c>,
    /// falling back to the newest sibling version folder that has that day, then null (caller skips
    /// the digest). Version folders are the digit-prefixed entries of the channel root — the same
    /// convention <c>LogDirectoryPurger.PruneSiblingVersions</c> relies on.
    /// </summary>
    public static string? ResolveLogPath(string channelRoot, string? versionTag, DateTime crashLocalDate)
    {
        try
        {
            var day = crashLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(versionTag))
            {
                var direct = Path.Combine(channelRoot, versionTag, "log", day);
                if (Directory.Exists(direct))
                {
                    return direct;
                }
            }

            if (!Directory.Exists(channelRoot))
            {
                return null;
            }

            // Newest version folder that has this day's dir (version tags are MAJOR.MINOR.PATCH.devN —
            // ordinal on the name is not a reliable sort; write time of the log dir is).
            string? best = null;
            DateTime bestWrite = default;
            foreach (var dir in Directory.GetDirectories(channelRoot))
            {
                var name = Path.GetFileName(dir);
                if (name.Length == 0 || !char.IsAsciiDigit(name[0]))
                {
                    continue;
                }

                var candidate = Path.Combine(dir, "log", day);
                if (!Directory.Exists(candidate))
                {
                    continue;
                }

                var write = Directory.GetLastWriteTimeUtc(candidate);
                if (best is null || write > bestWrite)
                {
                    best = candidate;
                    bestWrite = write;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }
}

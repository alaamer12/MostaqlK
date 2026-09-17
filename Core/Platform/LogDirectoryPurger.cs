using System.Globalization;

namespace MostaqlK.Core.Platform;

/// <summary>
/// Pure log-retention rules used by <see cref="AppPaths.PurgeOldLogs"/>.
/// Isolated so crash-path tests can run without MAUI / WinUI.
/// </summary>
public static class LogDirectoryPurger
{
    public static void Purge(string baseLogsDir, int retentionDays, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(baseLogsDir) || !Directory.Exists(baseLogsDir))
        {
            return;
        }

        var todayUtc = utcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var cutoffDate = utcNow.Date.AddDays(-Math.Max(1, retentionDays));

        foreach (var subDir in Directory.GetDirectories(baseLogsDir))
        {
            try
            {
                var dirName = Path.GetFileName(subDir);
                if (string.Equals(dirName, todayUtc, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (DateTime.TryParseExact(
                        dirName,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dirDate))
                {
                    if (dirDate < cutoffDate)
                    {
                        TryDeleteDirectorySafely(subDir);
                    }
                }
                else
                {
                    var lastWrite = Directory.GetLastWriteTimeUtc(subDir);
                    if (lastWrite < cutoffDate)
                    {
                        TryDeleteDirectorySafely(subDir);
                    }
                }
            }
            catch
            {
                // Best-effort cleanup per directory
            }
        }

        foreach (var file in Directory.GetFiles(baseLogsDir))
        {
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (lastWrite < cutoffDate)
                {
                    TryDeleteFileSafely(file);
                }
            }
            catch
            {
                // Best-effort cleanup per file
            }
        }
    }

    public static void TryDeleteDirectorySafely(string dirPath)
    {
        try
        {
            foreach (var file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                TryDeleteFileSafely(file);
            }

            Directory.Delete(dirPath, recursive: true);
        }
        catch
        {
            // Locked files: leave for a later run
        }
    }

    public static void TryDeleteFileSafely(string filePath)
    {
        try
        {
            var attrs = File.GetAttributes(filePath);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
            }

            File.Delete(filePath);
        }
        catch
        {
            // File might be open/locked
        }
    }
}

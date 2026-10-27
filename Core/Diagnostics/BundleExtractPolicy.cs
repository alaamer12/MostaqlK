using MostaqlK.Core.Platform;

namespace MostaqlK.Core.Diagnostics;

/// <summary>
/// Rules for .NET single-file extraction. The apphost unpacks native assets before
/// managed <c>Main</c> runs, so a portable exe started from Explorer always lands in
/// <c>%TEMP%\.net\...</c> unless the <em>launcher</em> already had
/// <c>DOTNET_BUNDLE_EXTRACT_BASE_DIR</c>. These helpers decide when that happened
/// and whether this process must re-exec itself with the persistent directory.
/// </summary>
public static class BundleExtractPolicy
{
    public const string RelocateArg = "--bundle-extract-relocated";
    public const string ExtractBaseEnvVar = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";

    public static bool IsVolatileExtractDirectory(string? extractDirectory, string tempDirectory)
    {
        if (string.IsNullOrWhiteSpace(extractDirectory) || string.IsNullOrWhiteSpace(tempDirectory))
        {
            return false;
        }

        var extract = Path.GetFullPath(extractDirectory);
        var temp = Path.GetFullPath(tempDirectory);
        if (!temp.EndsWith(Path.DirectorySeparatorChar) && !temp.EndsWith(Path.AltDirectorySeparatorChar))
        {
            temp += Path.DirectorySeparatorChar;
        }

        if (!extract.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = extract[temp.Length..];
        return relative.StartsWith(".net", StringComparison.OrdinalIgnoreCase)
               || relative.StartsWith(Path.Combine(".net"), StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldRelaunch(
        string? extractDirectory,
        string tempDirectory,
        string persistentExtractBase,
        bool alreadyRelocated)
    {
        if (alreadyRelocated)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(persistentExtractBase))
        {
            return false;
        }

        if (!IsVolatileExtractDirectory(extractDirectory, tempDirectory))
        {
            return false;
        }

        var extract = Path.GetFullPath(extractDirectory!);
        var persistent = Path.GetFullPath(persistentExtractBase);
        return !extract.StartsWith(persistent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when this process landed in another channel's bundle cache. The single-file apphost
    /// extracts before managed code runs, using the inherited user-scope
    /// <c>DOTNET_BUNDLE_EXTRACT_BASE_DIR</c> that both channels share, so switching channels once
    /// leaves the current launch running out of the other channel's folder. Relaunching with this
    /// channel's persistent directory is the only way to move the process out.
    /// </summary>
    public static bool ShouldRelaunchFromForeignChannelRoot(
        string? extractDirectory,
        string persistentExtractBase,
        string localAppDataDirectory,
        bool alreadyRelocated)
    {
        if (alreadyRelocated)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(extractDirectory) ||
            string.IsNullOrWhiteSpace(persistentExtractBase) ||
            string.IsNullOrWhiteSpace(localAppDataDirectory))
        {
            return false;
        }

        var extract = Path.GetFullPath(extractDirectory);
        var persistent = Path.GetFullPath(persistentExtractBase);
        if (extract.StartsWith(persistent, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var localAppData = Path.GetFullPath(localAppDataDirectory);
        if (!extract.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = extract[localAppData.Length..]
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var firstSegment = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, 2)[0];

        return firstSegment.StartsWith(ChannelIdentity.BaseAppName, StringComparison.OrdinalIgnoreCase)
               && relative.Contains("bundle-cache", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasRelocateArg(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], RelocateArg, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string[] ArgsWithoutRelocate(IReadOnlyList<string> args)
    {
        var result = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], RelocateArg, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(args[i]);
            }
        }

        return result.ToArray();
    }
}

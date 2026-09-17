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

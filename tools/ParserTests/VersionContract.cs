using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ParserTests;

/// <summary>
/// The four version numbers in <c>MostaqlK.csproj</c> and the changelog heading that documents them
/// have to agree, because every one of them is consumed by a different mechanism and only a human
/// keeps them in step.
/// <para>
/// This is not hypothetical: <c>InformationalVersion</c> sat pinned at <c>1.0.4.dev59</c> while the
/// display version had reached dev62, so three builds wrote their per-version log folders under a
/// dev59 name. That broke "each build keeps its own evidence" and the copy-forward-on-new-tag
/// migration, and nothing failed — the bug surfaced later as missing logs.
/// </para>
/// </summary>
internal static class VersionContract
{
    private static readonly string[] Properties =
    [
        "ApplicationDisplayVersion",
        "ApplicationVersion",
        "Version",
        "InformationalVersion",
    ];

    public static void Verify(Action<string, bool, string?> check, string root)
    {
        var project = Read(Path.Combine(root, "MostaqlK.csproj"));
        var values = new Dictionary<string, string>();
        foreach (var name in Properties)
        {
            values[name] = MatchSingle(project, name);
        }

        check("version: all four version properties are present in MostaqlK.csproj",
            ValuesFound(values) == Properties.Length,
            $"{ValuesFound(values)} of {Properties.Length}: {string.Join(", ", System.Linq.Enumerable.Select(Properties, p => $"{p}={values[p]}"))}");

        var display = values["ApplicationDisplayVersion"];
        var informational = values["InformationalVersion"];

        check("version: InformationalVersion equals ApplicationDisplayVersion",
            informational.Length > 0 && informational == display,
            $"display={display} informational={informational}");

        var baseVersion = values["Version"];
        check("version: the base version is the display version's prefix",
            baseVersion.Length > 0 && display.StartsWith(baseVersion + ".", StringComparison.Ordinal),
            $"version={baseVersion} display={display}");

        check("version: the base version is clean SemVer (the release script tags main with it)",
            Regex.IsMatch(baseVersion, @"^\d+\.\d+\.\d+$"), baseVersion);

        check("version: ApplicationVersion is a plain integer",
            int.TryParse(values["ApplicationVersion"], out var buildCode) && buildCode > 0,
            values["ApplicationVersion"]);

        var tag = Regex.Match(display, @"^(\d+\.\d+\.\d+)\.dev(\d+)$");
        check("version: the display version carries the MAJOR.MINOR.PATCH.TAG{n} form",
            tag.Success, display);

        // "newest" is the highest dev tag any entry names, not the first heading in the file: the
        // long-running Unreleased section at the top carries older dev numbers. Taking the maximum
        // catches a bump with no entry (the maximum lags) and an entry with no bump (it runs ahead).
        var changelog = Read(Path.Combine(root, "CHANGELOG.md"));
        var documented = 0;
        foreach (Match entry in Regex.Matches(changelog, @"^###.*\bdev(\d+)", RegexOptions.Multiline))
        {
            documented = Math.Max(documented, int.Parse(entry.Groups[1].Value));
        }

        check("version: the changelog documents the current dev tag",
            tag.Success && documented == int.Parse(tag.Groups[2].Value),
            $"csproj=dev{tag.Groups[2].Value} changelog=dev{documented}");
    }

    private static int ValuesFound(IDictionary<string, string> values)
    {
        var found = 0;
        foreach (var value in values.Values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                found++;
            }
        }

        return found;
    }

    private static string MatchSingle(string text, string property)
    {
        var match = Regex.Match(text, "<" + property + ">([^<]*)</" + property + ">");
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string Read(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
}

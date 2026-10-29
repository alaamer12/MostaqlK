using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MostaqlK.Core.Notifications;
using MostaqlK.Core.Sources;
using MostaqlK.Sources.Mostaql;
using MostaqlK.Sources.Nafezly;

namespace ParserTests;

/// <summary>
/// Every source module's brand contract: the artwork a toast is built from is a requirement of the
/// module, not an optional extra the notification code happens to find.
/// </summary>
internal static class BrandIconContract
{
    private static readonly ISourceDescriptor[] Modules = [MostaqlSource.Instance, NafezlySource.Instance];

    /// <summary>
    /// Checks that each registered module names its own icon, that the name follows the
    /// <c>icon_{sourceId}</c> convention, that the vector exists in <c>Resources/Images</c>, and that
    /// nothing else in the app hard-codes a per-module toast image.
    /// <para>
    /// The floor is the point of the whole check: a scan that inspects no module passes silently, and
    /// this repo has been bitten by a vacuous-green assertion before.
    /// </para>
    /// </summary>
    public static void Verify(Action<string, bool, string?> check, string root)
    {
        var images = Path.Combine(root, "Resources", "Images");

        check("brand: at least two source modules are registered for the icon contract",
            Modules.Length >= 2, $"{Modules.Length} modules");

        foreach (var module in Modules)
        {
            var name = module.RailIconImageName;
            check($"brand: {module.Id.Value} names the icon its toast art is built from",
                !string.IsNullOrWhiteSpace(name), name ?? "null");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            check($"brand: {module.Id.Value} follows the icon_{{source}} convention",
                name == $"icon_{module.Id.Value}", name);

            var svg = Path.Combine(images, $"{name}.svg");
            check($"brand: {module.Id.Value} ships Resources/Images/{name}.svg",
                File.Exists(svg), svg);
        }
    }

    /// <summary>
    /// The shared notification code must reach a module's art through its descriptor, never by naming
    /// the module. A literal here would be a second, quieter source of truth that stops following a
    /// rename the moment one happens.
    /// </summary>
    public static void VerifyNoHardcodedToastImages(Action<string, bool, string?> check, string root)
    {
        var declared = Modules
            .Select(m => m.RailIconImageName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in NotificationCodeFiles(root))
        {
            scanned++;
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, "\"(icon_[a-z0-9]+)\""))
            {
                if (declared.Contains(match.Groups[1].Value))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}: {match.Groups[1].Value}");
                }
            }
        }

        check("brand: no notification file names a module's icon as a literal",
            scanned >= 4 && offenders.Count == 0,
            offenders.Count == 0 ? $"{scanned} files scanned" : string.Join("; ", offenders));
    }

    private static IEnumerable<string> NotificationCodeFiles(string root)
    {
        var directories = new[]
        {
            Path.Combine(root, "Core", "Notifications"),
            Path.Combine(root, "Infrastructure", "Notifications"),
        };

        return directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories));
    }
}

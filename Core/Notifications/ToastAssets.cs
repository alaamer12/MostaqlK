using System;
using System.Globalization;
using System.IO;
using System.Text;
using MostaqlK.Core.Sources;

namespace MostaqlK.Core.Notifications;

/// <summary>
/// Turns a source's own brand data into the imagery a toast may carry, writing any generated file
/// into a directory the caller owns.
/// <para>
/// It lives in Core and takes <paramref name="cacheDirectory"/> as an argument on purpose: the app
/// passes <c>AppPaths.CacheDirectory</c> (channel-rooted, so stable and canary never read each
/// other's brand art), a test passes a temp folder, and <c>tools/ToastLab</c> passes one too. The
/// alternative — reaching for <c>AppPaths</c> from here — would tie the toast's whole visual
/// identity to the MAUI host process and make it untestable, which is how several rules in this repo
/// were originally discovered by crash dump instead of by assertion.
/// </para>
/// <para>
/// Nothing here throws. Branding is decoration; a decoration that cannot be produced must cost the
/// user nothing, so every failure path returns <see cref="ToastBrandVisual.Neutral"/> and lets the
/// caller log the reason.
/// </para>
/// </summary>
public static class ToastAssets
{
    /// <summary>Wide and short: the shell crops a hero image to the toast width, and a flat band has
    /// no composition worth ruining with a particular aspect ratio.</summary>
    public const int HeroWidthPx = 1080;

    public const int HeroHeightPx = 48;

    /// <summary>
    /// Resolve the visual for one source.
    /// </summary>
    /// <param name="descriptor">The producing source, or null when it cannot be resolved.</param>
    /// <param name="theme">Which palette to read (each set carries explicit dark counterparts).</param>
    /// <param name="cacheDirectory">Where generated PNGs live; created on demand.</param>
    /// <param name="baseLogoFileUri">The app's own logo URI, already escaped — the neutral fallback.</param>
    /// <param name="glyphDirectory">
    /// Folder holding the source's baked tile (<c>{RailIconImageName}.scale-200.png</c>), if any.
    /// A source icon is identity data the module already ships, so it is preferred over the app logo.
    /// </param>
    public static ToastBrandVisual For(
        ISourceDescriptor? descriptor,
        ToastVisualTheme theme,
        string? cacheDirectory,
        string? baseLogoFileUri,
        string? glyphDirectory = null)
    {
        if (descriptor is null)
        {
            return ToastBrandVisual.Neutral(baseLogoFileUri);
        }

        try
        {
            var logo = ResolveLogo(descriptor, baseLogoFileUri, glyphDirectory);

            if (string.IsNullOrWhiteSpace(cacheDirectory))
            {
                return new ToastBrandVisual(logo, null, CircleLogo: logo != baseLogoFileUri);
            }

            var brands = descriptor.Brands;
            var inkHex = theme == ToastVisualTheme.Dark ? brands.BrandInkDark : brands.BrandInk;
            var coreHex = theme == ToastVisualTheme.Dark ? brands.BrandDark : brands.Brand;
            var softHex = theme == ToastVisualTheme.Dark ? brands.BrandSoftDark : brands.BrandSoft;

            if (!BrandImageEncoder.TryParseColorHex(inkHex, out var ink)
                || !BrandImageEncoder.TryParseColorHex(coreHex, out var core)
                || !BrandImageEncoder.TryParseColorHex(softHex, out var soft))
            {
                // A palette that does not parse is a data bug worth seeing, but not worth a lost toast.
                return new ToastBrandVisual(logo, null, CircleLogo: logo != baseLogoFileUri);
            }

            var token = Fnv1aHex($"{inkHex}|{coreHex}|{softHex}");
            var fileName = $"hero_{descriptor.Id.Value}_{(theme == ToastVisualTheme.Dark ? "dark" : "light")}_{token}.png";
            var path = Path.Combine(cacheDirectory, fileName);

            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                var bytes = BrandImageEncoder.EncodeBanner(
                    [ink, core, core, soft, core, core, ink], HeroWidthPx, HeroHeightPx);
                if (bytes.Length == 0)
                {
                    return new ToastBrandVisual(logo, null, CircleLogo: logo != baseLogoFileUri);
                }

                WriteAtomically(path, bytes);
            }

            return new ToastBrandVisual(logo, ToastBranding.ToFileUri(path), CircleLogo: logo != baseLogoFileUri);
        }
        catch (IOException)
        {
            return ToastBrandVisual.Neutral(baseLogoFileUri);
        }
        catch (UnauthorizedAccessException)
        {
            return ToastBrandVisual.Neutral(baseLogoFileUri);
        }
        catch (ArgumentException)
        {
            return ToastBrandVisual.Neutral(baseLogoFileUri);
        }
    }

    private static string? ResolveLogo(ISourceDescriptor descriptor, string? baseLogoFileUri, string? glyphDirectory)
    {
        if (string.IsNullOrWhiteSpace(glyphDirectory) || string.IsNullOrWhiteSpace(descriptor.RailIconImageName))
        {
            return baseLogoFileUri;
        }

        // The tile the module ships for the rail is the same identity the toast should carry; it is
        // baked to {name}.scale-200.png by the Resizetizer pass and copied into the output folder.
        foreach (var candidate in new[]
                 {
                     Path.Combine(glyphDirectory, $"{descriptor.RailIconImageName}.scale-200.png"),
                     Path.Combine(glyphDirectory, $"{descriptor.RailIconImageName}.png"),
                 })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return ToastBranding.ToFileUri(candidate);
                }
            }
            catch (ArgumentException)
            {
                return baseLogoFileUri;
            }
        }

        return baseLogoFileUri;
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write-then-move, so a toast never renders from a half-written file and a crash mid-write
        // cannot leave a corrupt PNG that every later send would reuse.
        var temp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    private static string Fnv1aHex(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}

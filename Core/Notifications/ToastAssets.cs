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
    /// <summary>
    /// 16:9-ish, and small on purpose. The shell scales a hero image to the toast width and takes a
    /// proportionate slice of height from it: the first attempt shipped a 1080x48 strip, which the
    /// renderer stretched into a slab that dominated the notification. A quarter-size source costs a
    /// couple of hundred bytes, and a smooth ramp survives upscaling without visible artifacts.
    /// </summary>
    public const int HeroWidthPx = 336;

    public const int HeroHeightPx = 180;

    /// <summary>
    /// Resolve the visual for one source. There is one artwork per source, not one per theme: the
    /// notification is shown on whatever surface the user's shell has, and a second variant could only
    /// be picked by guessing at that surface.
    /// </summary>
    /// <param name="descriptor">The producing source, or null when it cannot be resolved.</param>
    /// <param name="cacheDirectory">Where generated PNGs live; created on demand.</param>
    /// <param name="baseLogoFileUri">The app's own logo URI, already escaped — the neutral fallback.</param>
    /// <param name="glyphDirectory">
    /// Folder holding the source's baked tile (<c>{RailIconImageName}.scale-200.png</c>), if any. On
    /// Windows that is the app's content root: MAUI bakes each module's <c>.svg</c> into scaled PNGs
    /// there at build time, so the source format is SVG and the runtime format is PNG.
    /// </param>
    /// <param name="appLogoPath">
    /// The app's own logo as a file path, drawn as the small mark inside the badge so a notification
    /// still says which product it came from once the platform's colors are on it.
    /// </param>
    public static ToastBrandVisual For(
        ISourceDescriptor? descriptor,
        string? cacheDirectory,
        string? baseLogoFileUri,
        string? glyphDirectory = null,
        string? appLogoPath = null)
    {
        if (descriptor is null)
        {
            return ToastBrandVisual.Neutral(baseLogoFileUri);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(cacheDirectory))
            {
                return ToastBrandVisual.Neutral(baseLogoFileUri);
            }

            if (!TryResolveRamp(descriptor, out var rampTop, out var rampBottom))
            {
                // A palette that does not parse is a data bug worth seeing, but not worth a lost toast.
                return ToastBrandVisual.Neutral(baseLogoFileUri);
            }

            var hero = TryBuildHero(cacheDirectory, descriptor, rampTop, rampBottom);
            var badge = TryBuildBadge(cacheDirectory, descriptor, rampTop, rampBottom, glyphDirectory, appLogoPath);

            // CircleLogo stays false for our own art: the badge already draws its plate as a circle and
            // lets the app's mark hang off the edge, which is precisely what cropping to a circle would
            // slice away. The flag is still what the shell would do to a logo we did not compose.
            return new ToastBrandVisual(badge ?? baseLogoFileUri, hero, CircleLogo: false);
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

    /// <summary>
    /// The two tones a source's art is built from: its deep brand ink easing into its primary color.
    /// <c>BrandSoft</c> is deliberately not used — it is near-white, which read as a stripe through the
    /// middle of the band.
    /// </summary>
    public static bool TryResolveRamp(
        ISourceDescriptor descriptor,
        out BrandImageEncoder.Rgb top,
        out BrandImageEncoder.Rgb bottom)
    {
        var brands = descriptor.Brands;
        top = default;
        bottom = default;
        return BrandImageEncoder.TryParseColorHex(brands.BrandInkDark, out top)
            && BrandImageEncoder.TryParseColorHex(brands.BrandDark, out bottom);
    }

    /// <summary>The one definition of what a source's badge is made of, shared by the app and by the harness a human uses to judge the art.</summary>
    public static BrandBadgeArt? BuildBadgeArt(
        ISourceDescriptor descriptor,
        BrandRaster? glyph,
        BrandRaster? mark) =>
        TryResolveRamp(descriptor, out var top, out var bottom)
            ? new BrandBadgeArt(top, bottom, glyph, mark)
            : null;

    /// <summary>
    /// Decodes the platform mark a module ships, or null. Public so the harness that lets a human
    /// judge these images composes them with the same rules the app runs, rather than a copy of them.
    /// </summary>
    public static BrandRaster? ResolveGlyph(ISourceDescriptor descriptor, string? glyphDirectory) =>
        LoadRaster(FindTile(glyphDirectory, descriptor.RailIconImageName));

    /// <summary>Reads and decodes one PNG file, tolerating anything wrong with either.</summary>
    public static BrandRaster? LoadRaster(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return File.Exists(path) ? BrandImageDecoder.TryDecode(File.ReadAllBytes(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The baked tile path for a module's mark, or null when it is not on disk.</summary>
    public static string? ResolveGlyphPath(ISourceDescriptor descriptor, string? glyphDirectory) =>
        FindTile(glyphDirectory, descriptor.RailIconImageName);

    private static string? TryBuildHero(
        string cacheDirectory,
        ISourceDescriptor descriptor,
        BrandImageEncoder.Rgb rampTop,
        BrandImageEncoder.Rgb rampBottom)
    {
        var token = Fnv1aHex($"hero|{BrandBadge.ArtRecipeVersion}|{rampTop}|{rampBottom}|{HeroWidthPx}x{HeroHeightPx}");
        var path = Path.Combine(cacheDirectory, $"hero_{descriptor.Id.Value}_{token}.png");
        if (IsCached(path))
        {
            return ToastBranding.ToFileUri(path);
        }

        var bytes = BrandImageEncoder.EncodeGradient(rampTop, rampBottom, HeroWidthPx, HeroHeightPx);
        return bytes.Length == 0 ? null : WriteAndLocate(path, bytes);
    }

    private static string? TryBuildBadge(
        string cacheDirectory,
        ISourceDescriptor descriptor,
        BrandImageEncoder.Rgb rampTop,
        BrandImageEncoder.Rgb rampBottom,
        string? glyphDirectory,
        string? appLogoPath)
    {
        var glyphFile = FindTile(glyphDirectory, descriptor.RailIconImageName);
        if (glyphFile is null)
        {
            // No platform mark to place means no reason to override the app's own logo.
            return null;
        }

        var glyph = LoadRaster(glyphFile);
        if (glyph is null)
        {
            return null;
        }

        var mark = LoadRaster(appLogoPath);

        // The fingerprint covers the inputs, not just the colors: re-baking a module's icon changes
        // the file's size or timestamp and therefore the name, so a stale badge cannot outlive it.
        var token = Fnv1aHex(string.Concat(
            "badge|", BrandBadge.ArtRecipeVersion.ToString(CultureInfo.InvariantCulture), "|",
            rampTop.ToString(), "|", rampBottom.ToString(),
            "|", Stamp(glyphFile), "|", Stamp(appLogoPath), "|", BrandBadge.DefaultSizePx.ToString(CultureInfo.InvariantCulture)));
        var path = Path.Combine(cacheDirectory, $"badge_{descriptor.Id.Value}_{token}.png");
        if (IsCached(path))
        {
            return ToastBranding.ToFileUri(path);
        }

        var rgba = BrandBadge.TryBuild(new BrandBadgeArt(rampTop, rampBottom, glyph, mark));
        if (rgba is null)
        {
            return null;
        }

        var bytes = BrandImageEncoder.EncodeRgba(BrandBadge.DefaultSizePx, BrandBadge.DefaultSizePx, rgba);
        return bytes.Length == 0 ? null : WriteAndLocate(path, bytes);
    }

    /// <summary>
    /// The baked tile MAUI leaves for a module's vector icon. A source's own artwork is identity data
    /// it already ships, so the toast reads it rather than drawing a substitute.
    /// </summary>
    private static string? FindTile(string? glyphDirectory, string iconName)
    {
        if (string.IsNullOrWhiteSpace(glyphDirectory))
        {
            return null;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(glyphDirectory, $"{iconName}.scale-200.png"),
                     Path.Combine(glyphDirectory, $"{iconName}.scale-150.png"),
                     Path.Combine(glyphDirectory, $"{iconName}.png"),
                 })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        return null;
    }

    private static string Stamp(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "none";
            }

            var info = new FileInfo(path);
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }
        catch (IOException)
        {
            return "none";
        }
        catch (UnauthorizedAccessException)
        {
            return "none";
        }
    }

    private static bool IsCached(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string? WriteAndLocate(string path, byte[] bytes)
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
        return ToastBranding.ToFileUri(path);
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

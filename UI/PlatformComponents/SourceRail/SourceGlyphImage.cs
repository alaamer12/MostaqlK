using MostaqlK.Core.Sources;

namespace MostaqlK.UI.PlatformComponents.SourceRail;

/// <summary>
/// Resolves a source's tile icon (<see cref="ISourceDescriptor.RailIconImageName"/>) to a
/// loadable <see cref="ImageSource"/>, or <c>null</c> when the source ships no art or the baked
/// file is absent — callers then fall back to the descriptor's letter <c>RailGlyph</c>.
/// The name stays source-blind: this unit never knows which site it is looking at.
/// </summary>
/// <remarks>
/// Loads through <see cref="ImageSource.FromFile"/> with an absolute path under
/// <see cref="AppContext.BaseDirectory"/> ({baseName}.scale-200.png, the Resizetizer output) —
/// the plain resource-name string form silently fails on this unpackaged Windows build, the
/// same constraint <c>AppIconGlyphExtensions</c> documents; this is that convention lifted off
/// the AppIcon enum so descriptor-driven hosts can share it.
/// </remarks>
public static class SourceGlyphImage
{
    /// <summary>The descriptor's baked tile icon, or <c>null</c> (no name set / file missing).</summary>
    public static ImageSource? TryLoad(ISourceDescriptor? descriptor) =>
        descriptor?.RailIconImageName is { Length: > 0 } baseName ? TryLoad(baseName) : null;

    /// <summary>Baked raster for a resource base name (e.g. <c>"icon_mostaql"</c>), or <c>null</c>.</summary>
    public static ImageSource? TryLoad(string imageBaseName)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, $"{imageBaseName}.scale-200.png");
        return File.Exists(fullPath) ? ImageSource.FromFile(fullPath) : null;
    }
}

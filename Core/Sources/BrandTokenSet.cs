namespace MostaqlK.Core.Sources;

/// <summary>
/// One source's palette as hex strings, kept platform-neutral (Core must not produce
/// MAUI <c>Color</c>s). The UI-side BrandTokens provider converts and swaps these;
/// every value here is seeded to the exact color the app renders today.
/// </summary>
/// <param name="Brand">Fills, edges, focus rings, selection.</param>
/// <param name="BrandInk">Readable brand color for small text on light surfaces.</param>
/// <param name="BrandSoft">Chip/tile backgrounds and the unread wash base.</param>
/// <param name="OnBrand">Text drawn on a solid <paramref name="Brand"/> fill.</param>
/// <param name="LinkLight">Source-link chip ink on the light theme (historically a link blue, not the brand hex).</param>
/// <param name="LinkDark">Source-link chip ink on the dark theme.</param>
public sealed record BrandTokenSet(
    string Brand,
    string BrandInk,
    string BrandSoft,
    string OnBrand,
    string LinkLight,
    string LinkDark);

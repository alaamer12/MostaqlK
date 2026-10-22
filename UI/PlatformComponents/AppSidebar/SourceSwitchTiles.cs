using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;
using MostaqlK.Core.Sources;
using MostaqlK.Services;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.UI.PlatformComponents.AppSidebar;

/// <summary>
/// The v3 source-morph tiles (contract §3/§4; the interactive core of the future
/// <c>SourceRail</c>). Renders ONE tile per <see cref="SourceRegistry.All"/> entry — glyph and
/// Arabic label come straight from each <see cref="ISourceDescriptor"/> — so a third source
/// appears here with zero code change. The tile is <em>always</em> painted in its own source's
/// brand (identity preview, per the mockups), and the ACTIVE source is distinguished by full
/// opacity plus a <c>{DynamicResource Brand}</c> inline-start indicator bar; inactive tiles drop
/// to the mockup's 45% opacity.
/// </summary>
/// <remarks>
/// Hard rules honored here: shared UI never branches on a source id — every fact is a
/// descriptor/registry lookup — and no brand hex is inline in this file: the tile fill is
/// resolved from <c>descriptor.Brands</c> (the module owns the hexes) and the active chrome bar
/// binds the live palette key. A switch is <b>pure projection</b>: it writes the persisted
/// active-source key, re-points the status facade and the brand palette, and never touches the
/// pipeline (both source engines already run unattended).
/// </remarks>
public sealed class SourceSwitchTiles : ContentView
{
    // Mockup `.rail .src-tile { opacity: .45 }` for an inactive tile; active stays at 1.
    private const double InactiveTileOpacity = 0.45;

    // Neutral sidebar name-ink for inactive tiles — the SAME slate pair AppSidebar uses for its
    // inactive nav rows (state chrome, never brand-retinted), kept in sync with that unit.
    private const string InactiveNameLight = "#475569";
    private const string InactiveNameDark = "#94A3B8";

    private readonly SourceRegistry? _registry;
    private readonly IBrandTokenProvider? _provider;
    private readonly ISettingsRepository? _settings;
    private readonly GlobalAppStatusService? _status;

    private readonly List<Tile> _tiles = [];

    /// <summary>Raised after a tile tap has switched the app's active source projection.</summary>
    public event EventHandler<SourceId>? SourceChanged;

    public SourceSwitchTiles()
    {
        _registry = IPlatformApplication.Current?.Services.GetService<SourceRegistry>();
        _provider = IPlatformApplication.Current?.Services.GetService<IBrandTokenProvider>();
        _settings = IPlatformApplication.Current?.Services.GetService<ISettingsRepository>();
        _status = IPlatformApplication.Current?.Services.GetService<GlobalAppStatusService>();

        Content = BuildTiles();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private View BuildTiles()
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        if (_registry is null)
        {
            // Design-time / isolated host: no registry, so no tiles (never a fabricated site).
            return stack;
        }

        foreach (var descriptor in _registry.All)
        {
            var tile = BuildTile(descriptor);
            _tiles.Add(tile);
            stack.Children.Add(tile.Root);
        }

        ApplyActiveStates();
        return stack;
    }

    private Tile BuildTile(ISourceDescriptor descriptor)
    {
        // One compact horizontal chip per source: [44px brand glyph square][Arabic name], so both
        // tiles are live immediately and each carries glyph + label straight from the descriptor.
        var glyphLabel = new Label
        {
            Text = descriptor.RailGlyph,
            FontFamily = "TajawalBold",
            FontSize = 17,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            WidthRequest = 44,
            HeightRequest = 44,
        };

        var glyphSquare = new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            StrokeThickness = 0,
            BackgroundColor = ResolveOwnBrandFill(descriptor),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = glyphLabel,
        };
        glyphLabel.TextColor = ResolveOwnBrandOnText(descriptor);

        var nameLabel = new Label
        {
            Text = descriptor.DisplayNameAr,
            FontFamily = "TajawalMedium",
            FontSize = 14,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Fill,
        };

        var indicator = new BoxView
        {
            WidthRequest = 3,
            HeightRequest = 22,
            CornerRadius = 3,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
        };
        indicator.SetDynamicResource(BackgroundColorProperty, BrandPalette.BrandKey);

        var row = new HorizontalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(12, 8),
            VerticalOptions = LayoutOptions.Center,
            Children = { indicator, glyphSquare, nameLabel },
        };

        var hit = new Button
        {
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Text = string.Empty,
            Padding = 0,
        };
        hit.AutomationId = $"Sidebar_SourceTile_{descriptor.Id.Value}";
        // Mockup tile aria-label "تحويل التطبيق إلى {source}" — composed from the descriptor's own
        // Arabic name, never a hardcoded site string.
        SemanticProperties.SetDescription(hit, $"تحويل التطبيق إلى {descriptor.DisplayNameAr}");
        hit.Clicked += (_, _) => SwitchTo(descriptor.Id);

        var cell = new Grid();
        cell.Children.Add(row);
        cell.Children.Add(hit);

        return new Tile(descriptor, cell, indicator, glyphSquare, glyphLabel, nameLabel);
    }

    private void SwitchTo(SourceId source)
    {
        if (_provider is null || source == _provider.CurrentSource)
        {
            return;
        }

        // Pure projection, in contract order: persist → re-point status facade → swap brand palette.
        _settings?.SetGlobal(SettingsKeys.ActiveSource, source.Value);
        _status?.SetActiveProjection(source);
        _provider.SwapTo(source);

        // BrandPalette repaints on TokensSwapped; opacity/indicator re-apply also rides that event
        // (subscribed below), but re-apply now so the rail is correct even before a repaint lands.
        ApplyActiveStates();

        SourceChanged?.Invoke(this, source);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // BrandPalette is an app-lifetime publisher and a fresh rail is built per realized host
        // page, so subscribe on the on-screen lifetime and release on Unloaded (event-lifetime rule).
        BrandPalette.PaletteChanged -= OnPaletteChanged;
        BrandPalette.PaletteChanged += OnPaletteChanged;
        ApplyActiveStates();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        BrandPalette.PaletteChanged -= OnPaletteChanged;
    }

    private void OnPaletteChanged()
    {
        // A morph (and a theme flip) changes which tile is active and the theme variant of each
        // tile's own brand fill — repaint the tiles that read descriptor hexes directly.
        foreach (var tile in _tiles)
        {
            tile.GlyphSquare.BackgroundColor = ResolveOwnBrandFill(tile.Descriptor);
            tile.GlyphLabel.TextColor = ResolveOwnBrandOnText(tile.Descriptor);
        }

        ApplyActiveStates();
    }

    private void ApplyActiveStates()
    {
        var active = _provider?.CurrentSource;
        foreach (var tile in _tiles)
        {
            var isActive = active is not null && tile.Descriptor.Id == active.Value;
            tile.Indicator.IsVisible = isActive;
            tile.Root.Opacity = isActive ? 1d : InactiveTileOpacity;

            // Active name-ink is the live readable brand text key (repaints free); inactive is the
            // neutral slate AppSidebar uses for its own inactive rows — state chrome, never brand.
            if (isActive)
            {
                tile.NameLabel.RemoveDynamicResource(Label.TextColorProperty);
                tile.NameLabel.SetDynamicResource(Label.TextColorProperty, BrandPalette.BrandInkKey);
            }
            else
            {
                tile.NameLabel.RemoveDynamicResource(Label.TextColorProperty);
                var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
                tile.NameLabel.TextColor = Color.FromArgb(isDark ? InactiveNameDark : InactiveNameLight);
            }
        }
    }

    // Each tile paints its OWN source brand straight from the descriptor's hex set (theme-projected
    // the same way BrandPalette projects the active source), so no brand hex lives in this file.
    private static Color ResolveOwnBrandFill(ISourceDescriptor descriptor)
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var hex = isDark ? descriptor.Brands.BrandDark : descriptor.Brands.Brand;
        return Color.TryParse(hex, out var color) ? color : Colors.Transparent;
    }

    private static Color ResolveOwnBrandOnText(ISourceDescriptor descriptor)
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var hex = isDark ? descriptor.Brands.OnBrandDark : descriptor.Brands.OnBrand;
        return Color.TryParse(hex, out var color) ? color : Colors.White;
    }

    private sealed record Tile(
        ISourceDescriptor Descriptor,
        Grid Root,
        BoxView Indicator,
        Border GlyphSquare,
        Label GlyphLabel,
        Label NameLabel);
}

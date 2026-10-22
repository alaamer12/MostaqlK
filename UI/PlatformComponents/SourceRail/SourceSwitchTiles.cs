using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;
using MostaqlK.Core.Sources;
using MostaqlK.Services;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.UI.PlatformComponents.SourceRail;

/// <summary>
/// The v3 source-morph tiles (contract §3/§4; the interactive core of the <see cref="SourceRail"/>).
/// Renders ONE tile per <see cref="SourceRegistry.All"/> entry — glyph and Arabic label come straight
/// from each <see cref="ISourceDescriptor"/> — so a third source appears here with zero code change.
/// The tile is <em>always</em> painted in its own source's brand (identity preview, per the mockups),
/// and the ACTIVE source is distinguished by full opacity plus a <c>{DynamicResource Brand}</c>
/// inline-start indicator bar; inactive tiles drop to the mockup's 45% opacity.
/// </summary>
/// <remarks>
/// Two visual forms, one unit (no duplicated switching logic):
/// <list type="bullet">
/// <item><see cref="IconOnly"/> = true → the rail form from the v3 mockups: a vertical stack of
/// centered 44px glyph squares, the 3×22px indicator bar flush to the rail's outer edge, and the
/// hover tooltip carrying name + domain ("مستقل — mostaql.com").</item>
/// <item><see cref="IconOnly"/> = false (default) → the legacy horizontal chip (indicator + glyph +
/// Arabic name) still hosted by <c>AppSidebar</c> on the pages that have not migrated to the rail yet.</item>
/// </list>
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

    public static readonly BindableProperty IconOnlyProperty = BindableProperty.Create(
        nameof(IconOnly), typeof(bool), typeof(SourceSwitchTiles), false,
        propertyChanged: OnIconOnlyChanged);

    /// <summary>
    /// Renders the tiles in the v3 rail form (icon-only vertical stack, mockup `.rail .src-tile`)
    /// instead of the legacy horizontal chip. Flipping it rebuilds the tile tree; the switching
    /// logic, registry enumeration, AutomationIds and palette subscriptions are identical in both.
    /// </summary>
    public bool IconOnly
    {
        get => (bool)GetValue(IconOnlyProperty);
        set => SetValue(IconOnlyProperty, value);
    }

    private static void OnIconOnlyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var self = (SourceSwitchTiles)bindable;
        self.RebuildTiles();
    }

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

    private void RebuildTiles()
    {
        _tiles.Clear();
        Content = BuildTiles();
    }

    private View BuildTiles()
    {
        // Rail form: tiles stack vertically like the mockup's `<aside class="rail flex flex-col
        // gap-2">`; the chip form keeps its own column inside the sidebar's horizontal nav stack.
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
            HorizontalOptions = IconOnly ? LayoutOptions.Center : LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
        };
        glyphLabel.TextColor = ResolveOwnBrandOnText(descriptor);

        var indicator = new BoxView
        {
            WidthRequest = RailWidths.IndicatorWidth,
            HeightRequest = RailWidths.IndicatorHeight,
            CornerRadius = 3,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
        };
        indicator.SetDynamicResource(BackgroundColorProperty, BrandPalette.BrandKey);

        Label? nameLabel = null;
        Grid cell;

        if (IconOnly)
        {
            // Mockup rail tile: a full-rail-width cell so the indicator bar can sit flush against
            // the rail's outer edge (`.rail-btn.active::before { inset-inline-start: -10px }`),
            // with the 44px glyph square centered in the remaining gutter.
            indicator.HorizontalOptions = LayoutOptions.Start;
            indicator.Margin = new Thickness(3, 0);

            cell = new Grid
            {
                WidthRequest = RailWidths.Width,
                HeightRequest = RailWidths.TileSize,
                HorizontalOptions = LayoutOptions.Center,
            };
            cell.Children.Add(indicator);
            cell.Children.Add(glyphSquare);
        }
        else
        {
            // Legacy chip: [indicator][44px brand glyph square][Arabic name], so both tiles are
            // live immediately and each carries glyph + label straight from the descriptor.
            nameLabel = new Label
            {
                Text = descriptor.DisplayNameAr,
                FontFamily = "TajawalMedium",
                FontSize = 14,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Fill,
            };

            var chipRow = new HorizontalStackLayout
            {
                Spacing = 12,
                Padding = new Thickness(12, 8),
                VerticalOptions = LayoutOptions.Center,
                Children = { indicator, glyphSquare, nameLabel },
            };

            cell = new Grid();
            cell.Children.Add(chipRow);
        }

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
        // Mockup rail tooltip: name + domain ("مستقل — mostaql.com"), straight from the descriptor.
        ToolTipProperties.SetText(hit, $"{descriptor.DisplayNameAr} — {descriptor.Domain}");
        hit.Clicked += (_, _) => SwitchTo(descriptor.Id, cell);

        // The cell is the tree root for both modes: the hit Button overlays the visuals, exactly
        // like every other rail row (plain Border + tap never surfaces an AutomationId to Windows
        // UI Automation — dotnet/maui#4715).
        cell.Children.Add(hit);

        return new Tile(descriptor, cell, indicator, glyphSquare, glyphLabel, nameLabel);
    }

    private void SwitchTo(SourceId source, VisualElement origin)
    {
        if (_provider is null || source == _provider.CurrentSource)
        {
            return;
        }

        // The single canonical mutation path, unchanged (contract §6): pure projection — the
        // persisted active-source key, the status facade, the brand palette repaint — never the
        // pipeline. It runs as ONE atomic instant change at the START of the presentation wrapper.
        void ApplyState()
        {
            MostaqlK.Services.SourceMorph.Apply(_settings, _status, _provider, source);

            // BrandPalette repaints on TokensSwapped; opacity/indicator re-apply also rides that event
            // (subscribed below), but re-apply now so the rail is correct even before a repaint lands.
            ApplyActiveStates();

            SourceChanged?.Invoke(this, source);
        }

        // Presentation wrapper only (contract §4.2): reduced motion / non-Windows ⇒ an instant swap
        // identical to calling ApplyState() directly. targetRegion=null morphs the whole app; the
        // tapped tile (origin) anchors the eventual radial center.
        SourceMorphTransition.Run(origin, targetRegion: null, ApplyState, SourceMorphTransition.SourceMorphDurationMs);
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

            if (tile.NameLabel is not { } name)
            {
                continue; // rail form has no name label — the tooltip above carries the identity.
            }

            // Active name-ink is the live readable brand text key (repaints free); inactive is the
            // neutral slate AppSidebar uses for its own inactive rows — state chrome, never brand.
            name.RemoveDynamicResource(Label.TextColorProperty);
            if (isActive)
            {
                name.SetDynamicResource(Label.TextColorProperty, BrandPalette.BrandInkKey);
            }
            else
            {
                var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
                name.TextColor = Color.FromArgb(isDark ? InactiveNameDark : InactiveNameLight);
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
        Label? NameLabel);
}

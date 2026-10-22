using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using MostaqlK.Core.Sources;

namespace MostaqlK.UI.DesignSystem.BrandTokens;

/// <summary>
/// The app-level brand palette (B1b): one <see cref="ResourceDictionary"/> layer inside
/// <c>Application.Current.Resources</c> that every DynamicResource consumer paints from, fed by
/// <see cref="IBrandTokenProvider.CurrentFor(AppTheme)"/>. It is the single bridge between the
/// two things that can change the brand's pixels — a source morph (<c>TokensSwapped</c>) and a
/// theme flip (<c>RequestedThemeChanged</c>, which every theme setter in the app funnels through
/// <c>Application.UserAppTheme</c>) — so components never have to subscribe to the provider just
/// to recolor: XAML uses <c>{DynamicResource Brand/BrandInk/BrandSoft/OnBrand/BrandLink}</c>, and
/// draw-only code reads the cached <see cref="Brand"/>-style properties (and optionally subscribes
/// to <see cref="PaletteChanged"/> when it needs to repaint mid-idle).
/// <para>
/// Replacing a key in the application's ROOT resource dictionary is what makes MAUI repaint
/// DynamicResource consumers, so <see cref="Apply"/> mutates the same five keys in place rather
/// than merging a new dictionary. Initialize is called once from the <c>App</c> constructor
/// before any window exists; the values then always describe the active source projected onto the
/// effective theme (mostaql light renders the locked light set, mostaql dark the locked dark set).
/// </para>
/// </summary>
public static class BrandPalette
{
    /// <summary>DynamicResource keys — one per brand token of the active source + current theme.</summary>
    public const string BrandKey = "Brand";
    public const string BrandInkKey = "BrandInk";
    public const string BrandSoftKey = "BrandSoft";
    public const string OnBrandKey = "OnBrand";
    public const string BrandLinkKey = "BrandLink";

    private static readonly Dictionary<string, Color> ColorCache = new(StringComparer.Ordinal);

    private static IBrandTokenProvider? _provider;
    private static Application? _app;

    // Cached projections, refreshed on every Apply(). Transparent until Initialize runs —
    // every UI consumer paints after the App constructor, and a design-time default of
    // transparent fails visible instead of silently pinning a stale brand hex.
    private static Color _brand = Colors.Transparent;
    private static Color _brandInk = Colors.Transparent;
    private static Color _brandSoft = Colors.Transparent;
    private static Color _onBrand = Colors.Transparent;
    private static Color _brandLink = Colors.Transparent;
    private static Color _accentLight = Colors.Transparent;
    private static Color _accentDark = Colors.Transparent;
    private static Color _linkLight = Colors.Transparent;
    private static Color _linkDark = Colors.Transparent;

    /// <summary>Brand fill (edge/accent/selection) for the active source on the effective theme.</summary>
    public static Color Brand => _brand;

    /// <summary>Readable brand text color for the effective theme.</summary>
    public static Color BrandInk => _brandInk;

    /// <summary>Chip/tile wash for the effective theme (ARGB in dark).</summary>
    public static Color BrandSoft => _brandSoft;

    /// <summary>Text drawn on a solid <see cref="Brand"/> fill.</summary>
    public static Color OnBrand => _onBrand;

    /// <summary>Source-link ink for the effective theme (LinkLight/LinkDark projected).</summary>
    public static Color BrandLink => _brandLink;

    /// <summary>Brand fill as the LIGHT theme renders it, independent of the current theme (ex-DesignTokens.AccentPrimary).</summary>
    public static Color AccentLight => _accentLight;

    /// <summary>Brand fill as the DARK theme renders it, independent of the current theme (ex-DesignTokens.AccentPrimaryDark).</summary>
    public static Color AccentDark => _accentDark;

    /// <summary>Link ink as the LIGHT theme renders it, independent of the current theme.</summary>
    public static Color LinkLight => _linkLight;

    /// <summary>Link ink as the DARK theme renders it, independent of the current theme.</summary>
    public static Color LinkDark => _linkDark;

    /// <summary>
    /// Raised after the resource keys and cached colors are re-applied. Draw-only components that
    /// cache colors per-frame subscribe on their on-screen lifetime (Loaded/Attach) and release on
    /// Unloaded/Detach, same event-lifetime rule as the provider's own consumers.
    /// </summary>
    public static event Action? PaletteChanged;

    /// <summary>
    /// Wires the palette to the provider and the app, then applies the first projection. Called
    /// once from the <c>App</c> constructor AFTER <c>UserAppTheme</c> and the persisted-source
    /// restore, so the initial apply already reflects both. Idempotent.
    /// </summary>
    public static void Initialize(Application app, IServiceProvider services)
    {
        _provider ??= services.GetService<IBrandTokenProvider>();
        if (_provider is null)
        {
            return;
        }

        if (!ReferenceEquals(_app, app))
        {
            if (_app is { } old)
            {
                old.RequestedThemeChanged -= OnRequestedThemeChanged;
            }

            _app = app;
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }

        _provider.TokensSwapped -= OnTokensSwapped;
        _provider.TokensSwapped += OnTokensSwapped;
        Apply();
    }

    private static void OnTokensSwapped(BrandTokenSet _) => Apply();

    private static void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) => Apply();

    private static void Apply()
    {
        // Swap/theme handlers may one day fire off the main thread; resource-dictionary mutation
        // and DynamicResource re-resolution are UI-thread work. MainThread is the same dispatch
        // helper App.xaml.cs already uses (it resolves via the MAUI implicit usings).
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(Apply);
            return;
        }

        if (_provider is null)
        {
            return;
        }

        var isDark = _app?.RequestedTheme == AppTheme.Dark;
        var themed = _provider.CurrentFor(isDark ? AppTheme.Dark : AppTheme.Light);
        var source = _provider.Current;

        _brand = Parse(themed.Brand);
        _brandInk = Parse(themed.BrandInk);
        _brandSoft = Parse(themed.BrandSoft);
        _onBrand = Parse(themed.OnBrand);
        _brandLink = Parse(isDark ? source.LinkDark : source.LinkLight);
        _accentLight = Parse(source.Brand);
        _accentDark = Parse(source.BrandDark);
        _linkLight = Parse(source.LinkLight);
        _linkDark = Parse(source.LinkDark);

        if (_app is { } app)
        {
            app.Resources[BrandKey] = _brand;
            app.Resources[BrandInkKey] = _brandInk;
            app.Resources[BrandSoftKey] = _brandSoft;
            app.Resources[OnBrandKey] = _onBrand;
            app.Resources[BrandLinkKey] = _brandLink;
        }

        PaletteChanged?.Invoke();
    }

    private static Color Parse(string hex) =>
        ColorCache.TryGetValue(hex, out var cached)
            ? cached
            : ColorCache[hex] = Color.TryParse(hex, out var parsed) ? parsed : Colors.Transparent;
}

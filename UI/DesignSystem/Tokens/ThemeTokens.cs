using System.Collections.Generic;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MostaqlK.UI.DesignSystem.Tokens;

/// <summary>
/// The theme-only counterpart to <see cref="BrandTokens.BrandPalette"/>: writes every
/// <see cref="NeutralTokenSet"/> color (and its SolidColorBrush twin) into the application ROOT
/// resource dictionary so <c>{DynamicResource …}</c> consumers repaint on a theme flip, and
/// exposes the same values to draw/chrome code that cannot use DynamicResource.
/// <para>
/// Neutral values never vary by source; a source swap does not touch this table. Initialize runs
/// once from the <c>App</c> constructor immediately after <see cref="BrandTokens.BrandPalette.Initialize"/>,
/// before any window exists.
/// </para>
/// </summary>
public static class ThemeTokens
{
    private static readonly Dictionary<string, Color> ColorCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new(StringComparer.Ordinal);

    private static Application? _app;

    /// <summary>Raised after a theme flip rewrote the token values. Imperative painters
    /// (settings rows, native chrome) subscribe on their on-screen lifetime.</summary>
    public static event Action? TokensChanged;

    public static void Initialize(Application app)
    {
        if (!ReferenceEquals(_app, app))
        {
            if (_app is { } old)
            {
                old.RequestedThemeChanged -= OnRequestedThemeChanged;
            }

            _app = app;
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }

        Apply();
    }

    private static void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) => Apply();

    private static void Apply()
    {
        // Resource-dictionary mutation and DynamicResource re-resolution are UI-thread work;
        // AppThemeChanged can arrive off the UI thread on some platforms.
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(Apply);
            return;
        }

        if (_app is not { } app)
        {
            return;
        }

        var isDark = app.RequestedTheme == AppTheme.Dark;
        foreach (var token in NeutralTokenSet.All)
        {
            var color = Parse(isDark ? token.Dark : token.Light);
            ColorCache[token.Name] = color;
            app.Resources[token.Name] = color;

            if (NeutralTokenSet.BrushTokens.Contains(token.Name))
            {
                var brush = new SolidColorBrush(color);
                BrushCache[token.Name] = brush;
                app.Resources[token.Name + "Brush"] = brush;
            }
        }

        TokensChanged?.Invoke();
    }

    /// <summary>Current-theme color for a token name (Transparent for an unknown name — fails
    /// visibly instead of silently pinning a stale value).</summary>
    public static Color Get(string name)
    {
        if (ColorCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        if (NeutralTokenSet.Find(name) is not { } token)
        {
            return Colors.Transparent;
        }

        var value = Parse(_app?.RequestedTheme == AppTheme.Dark ? token.Dark : token.Light);
        ColorCache[name] = value;
        return value;
    }

    /// <summary>Current-theme brush for a Brush token; a fresh brush for a Color-only token.</summary>
    public static SolidColorBrush GetBrush(string name) =>
        BrushCache.TryGetValue(name, out var brush) ? brush : new SolidColorBrush(Get(name));

    private static Color Parse(string hex) =>
        Color.TryParse(hex, out var color) ? color : Colors.Transparent;

    // Typed getters for C# consumers. XAML uses the DynamicResource keys directly.
    public static Color Background => Get(nameof(Background));
    public static Color BackgroundChrome => Get(nameof(BackgroundChrome));
    public static Color Surface => Get(nameof(Surface));
    public static Color SurfaceRaised => Get(nameof(SurfaceRaised));
    public static Color SurfaceSunken => Get(nameof(SurfaceSunken));
    public static Color SurfaceHover => Get(nameof(SurfaceHover));
    public static Color SurfaceField => Get(nameof(SurfaceField));
    public static Color SurfaceDeep => Get(nameof(SurfaceDeep));
    public static Color SurfaceTooltip => Get(nameof(SurfaceTooltip));
    public static Color TextPrimary => Get(nameof(TextPrimary));
    public static Color TextStrong => Get(nameof(TextStrong));
    public static Color TextSecondary => Get(nameof(TextSecondary));
    public static Color TextSecondaryStrong => Get(nameof(TextSecondaryStrong));
    public static Color TextMuted => Get(nameof(TextMuted));
    public static Color TextFaint => Get(nameof(TextFaint));
    public static Color TextSubtle => Get(nameof(TextSubtle));
    public static Color TextDescription => Get(nameof(TextDescription));
    public static Color TextDisplay => Get(nameof(TextDisplay));
    public static Color Border => Get(nameof(Border));
    public static Color BorderSubtle => Get(nameof(BorderSubtle));
    public static Color BorderMuted => Get(nameof(BorderMuted));
    public static Color BorderPositive => Get(nameof(BorderPositive));
    public static Color BorderDanger => Get(nameof(BorderDanger));
    public static Color Accent => Get(nameof(Accent));
    public static Color AccentSoft => Get(nameof(AccentSoft));
    public static Color AccentSoftPanel => Get(nameof(AccentSoftPanel));
    public static Color Positive => Get(nameof(Positive));
    public static Color PositiveAction => Get(nameof(PositiveAction));
    public static Color Danger => Get(nameof(Danger));
    public static Color DangerStrong => Get(nameof(DangerStrong));
    public static Color Warning => Get(nameof(Warning));
    public static Color PollActive => Get(nameof(PollActive));
    public static Color PollInactive => Get(nameof(PollInactive));
    public static Color DataQueue => Get(nameof(DataQueue));
    public static Color DataWorker => Get(nameof(DataWorker));
    public static Color DataCompleted => Get(nameof(DataCompleted));
    public static Color DataError => Get(nameof(DataError));
    public static Color DataIdle => Get(nameof(DataIdle));
    public static Color DataDiscovery => Get(nameof(DataDiscovery));
    public static Color Letterbox => Get(nameof(Letterbox));
    public static Color Scrim => Get(nameof(Scrim));
    public static Color ShimmerBase => Get(nameof(ShimmerBase));
    public static Color ShimmerHighlight => Get(nameof(ShimmerHighlight));
    public static Color TitleBarBackground => Get(nameof(TitleBarBackground));
    public static Color TitleBarInk => Get(nameof(TitleBarInk));
    public static Color TitleBarHover => Get(nameof(TitleBarHover));
    public static Color TitleBarPressed => Get(nameof(TitleBarPressed));
}

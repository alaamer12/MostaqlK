using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using MostaqlK.UI.DesignSystem;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Shared, platform-neutral project card surface (the flat, card-based feed item from the
/// mockups). Same shape everywhere; only native handler mapping differs per OS (see the
/// <c>AppCard.Windows.cs</c> partial). Carries the unread/read accent-border state used by the
/// project feed to distinguish unread items (accent bar + typography weight).
/// </summary>
public partial class AppCard : Border
{
    public static readonly BindableProperty IsUnreadProperty = BindableProperty.Create(
        nameof(IsUnread),
        typeof(bool),
        typeof(AppCard),
        defaultValue: false,
        propertyChanged: OnIsUnreadChanged);

    /// <summary>
    /// Whether the underlying project has not yet been viewed. When <c>true</c>, the card shows
    /// the accent-border unread treatment; when <c>false</c> ("read"), the accent is removed.
    /// </summary>
    public bool IsUnread
    {
        get => (bool)GetValue(IsUnreadProperty);
        set => SetValue(IsUnreadProperty, value);
    }

    /// <summary>
    /// Convenience inverse of <see cref="IsUnread"/> for XAML bindings that read more naturally
    /// as "is read" (e.g. <c>IsVisible="{Binding IsRead}"</c>).
    /// </summary>
    public bool IsRead
    {
        get => !IsUnread;
        set => IsUnread = !value;
    }

    public AppCard()
    {
        // Flat white/dark-surface card with a rounded corner, matching .project-card's inner
        // panel in projects.html (bg-white/dark:bg-slate-900, rounded-xl, border, shadow-sm).
        StrokeShape = new RoundRectangle { CornerRadius = DesignTokens.CornerRadius.Default };
        StrokeThickness = 1;
        Padding = new Thickness(24);
        Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.05f, Radius = 4, Offset = new Point(0, 1) };
        UpdateThemeColors();

        // Add tactile feedback behavior
        Behaviors.Add(new PressableEffect { ApplyHoverHighlight = true });

        // Read/unread accent colors differ per theme (ReadBorderLight/Dark, AccentPrimary/Dark in
        // Colors.xaml) - re-apply whenever the OS/app theme flips, same pattern as
        // AppSidebar/SplitterHandle/PipelineRadar.
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged += (_, _) => UpdateThemeColors();
        }
    }

    private static void OnIsUnreadChanged(BindableObject bindable, object oldValue, object newValue)
    {
        (bindable as AppCard)?.UpdateThemeColors();
    }

    private void UpdateThemeColors()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        // Surface background — mirrors AppSurfaceLight/AppSurfaceDark from Colors.xaml.
        BackgroundColor = isDark ? Color.FromArgb("#0F172A") : Colors.White;

        // Unread cards use the mockup's blue edge treatment (border-inline-start accent, using
        // AccentPrimary/AccentPrimaryDark). MAUI Border can't do per-edge strokes, so a stronger
        // full outline stands in for the accent bar. Read cards use the muted
        // ReadBorderLight/ReadBorderDark slate outline instead.
        StrokeThickness = IsUnread ? 2 : 1;
        Stroke = IsUnread
            ? new SolidColorBrush(isDark ? Color.FromArgb("#5CA8DE") : Color.FromArgb("#2386C8"))
            : new SolidColorBrush(isDark ? Color.FromArgb("#475569") : Color.FromArgb("#CBD5E1"));
    }
}

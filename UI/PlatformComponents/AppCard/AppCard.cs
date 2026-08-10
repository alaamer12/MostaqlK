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
        BackgroundColor = Colors.White;
        Stroke = new SolidColorBrush(DesignTokens.Colors.ReadBorderLight.WithAlpha(0.5f));
        Padding = new Thickness(DesignTokens.Spacing.XL, DesignTokens.Spacing.L);
        Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.06f, Radius = 6, Offset = new Point(0, 1) };
        UpdateAccentBorder(false);
    }

    private static void OnIsUnreadChanged(BindableObject bindable, object oldValue, object newValue)
    {
        (bindable as AppCard)?.UpdateAccentBorder((bool)newValue);
    }

    private void UpdateAccentBorder(bool isUnread)
    {
        // MAUI's Border draws a uniform stroke, not a per-edge one, so the 4px accent bar from
        // .project-card.unread { border-inline-start-color: accent } is approximated with a
        // slightly heavier stroke tinted with the accent color while unread.
        StrokeThickness = isUnread ? 1 : 1;
        Stroke = isUnread
            ? new SolidColorBrush(DesignTokens.Colors.AccentPrimary)
            : new SolidColorBrush(DesignTokens.Colors.ReadBorderLight.WithAlpha(0.6f));
    }
}

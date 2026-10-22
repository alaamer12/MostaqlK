using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;
using MostaqlK.Core.Navigation;
using MostaqlK.Core.Sources;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.UI.PlatformComponents.SourceRail;

/// <summary>
/// The v3 shell rail (contract §3): a 64px icon-only vertical strip that replaces the 256px
/// labelled <c>AppSidebar</c> on desktop. Top→bottom, exactly like the approved mockups
/// (<c>.repertoire/design/postmvp/v3-multisource/projects.html</c> "icon-rail"): brand mark ·
/// separator · source tiles (<see cref="SourceSwitchTiles"/> in its icon-only form — tap =
/// <see cref="SourceMorph.Apply"/>, instant, no reveal animation) · separator · section buttons
/// (المشاريع · البحث المتقدم · الإعدادات · حول التطبيق) · the ACTIVE source's contributed pages
/// (<see cref="SourceContributionRegistry.PagesFor"/> — growth without branches) · spacer ·
/// notification bell (unread pill) · theme toggle.
/// </summary>
/// <remarks>
/// Rules honored: every brand pixel routes through the B1 DynamicResource keys
/// (<c>Brand/BrandInk/BrandSoft/OnBrand/BrandLink</c>) — zero inline brand hex; no `source == id`
/// branch anywhere; the rail rides the shared <see cref="RailWidths"/> token; AutomationIds keep
/// the <c>Sidebar_*</c> names the UI tests already assert; theme toggle persists through the same
/// <see cref="SettingsKeys.IsDarkMode"/> path the sidebar row used. Event-lifetime rule: the
/// app-lifetime publishers (<c>RequestedThemeChanged</c>, <see cref="BrandPalette.PaletteChanged"/>)
/// are subscribed on Loaded and released on Unloaded, because a fresh rail is built per realized
/// host page. Zero animations (`.repertoire/.steering/v3/animation-safety.md`; reveal is a later wave).
/// </remarks>
public partial class SourceRail : ContentView
{
    // Mockup `.rail-btn { color: var(--ink-3) }` neutral inactive ink (state chrome, never brand):
    // the same slate pair AppSidebar uses for its inactive rows, kept in sync with that unit.
    private const string InactiveInkLight = "#475569";
    private const string InactiveInkDark = "#94A3B8";

    public static readonly BindableProperty ActivePageProperty = BindableProperty.Create(
        nameof(ActivePage), typeof(RailPage), typeof(SourceRail), RailPage.None,
        propertyChanged: OnActivePageChanged);

    public static readonly BindableProperty UnreadCountProperty = BindableProperty.Create(
        nameof(UnreadCount), typeof(int), typeof(SourceRail), 0,
        propertyChanged: OnUnreadCountChanged);

    /// <summary>Which section button carries the active (brand-soft + indicator bar) treatment.</summary>
    public RailPage ActivePage
    {
        get => (RailPage)GetValue(ActivePageProperty);
        set => SetValue(ActivePageProperty, value);
    }

    /// <summary>Live unread-notification count; &gt;0 paints the pill on the bell.</summary>
    public int UnreadCount
    {
        get => (int)GetValue(UnreadCountProperty);
        set => SetValue(UnreadCountProperty, value);
    }

    public event EventHandler? ProjectsClicked;
    public event EventHandler? AdvancedSearchClicked;
    public event EventHandler? SettingsClicked;
    public event EventHandler? AboutClicked;
    public event EventHandler? NotificationsClicked;

    /// <summary>
    /// Raised after a source tile tap has switched the app's active projection (the global triple
    /// already ran inside <see cref="SourceSwitchTiles"/> via the canonical
    /// <see cref="MostaqlK.Services.SourceMorph.Apply"/>); a host that owns a source-scoped surface
    /// (the projects feed) uses this to re-point its own data.
    /// </summary>
    public event EventHandler<SourceId>? SourceChanged;

    public SourceRail()
    {
        InitializeComponent();

        // Hover tooltips + accessibility names, straight from the mockup `.tip` strings — the
        // icon-only rail must stay as scannable as the labelled sidebar it replaces (contract §11.11).
        AttachLabels(ProjectsHit, "المشاريع");
        AttachLabels(AdvancedSearchHit, "البحث المتقدم");
        AttachLabels(SettingsHit, "الإعدادات");
        AttachLabels(AboutHit, "حول التطبيق");
        AttachLabels(NotificationsHit, "التنبيهات");
        AttachLabels(ThemeHit, "الوضع الداكن");

        ApplyActiveState();
        UpdateUnreadBadge();

        Loaded += OnRailLoaded;
        Unloaded += OnRailUnloaded;
    }

    private static void AttachLabels(View target, string labelAr)
    {
        ToolTipProperties.SetText(target, labelAr);
        SemanticProperties.SetDescription(target, labelAr);
    }

    private void OnTilesSourceChanged(object? sender, SourceId source)
    {
        RebuildContributions();
        SourceChanged?.Invoke(this, source);
    }

    /// <summary>
    /// Re-renders the ACTIVE source's contributed rail pages, in registry order, after the standard
    /// sections. Presence/absence is pure data: <c>PagesFor</c> returns the active source's
    /// contributions (empty for a source that ships none), so a new/dev-fake source grows the rail
    /// with zero shared-code change and this never branches on a source id (contract §10).
    /// </summary>
    private void RebuildContributions()
    {
        ContributionNavHost.Children.Clear();

        var registry = IPlatformApplication.Current?.Services.GetService<SourceContributionRegistry>();
        var provider = IPlatformApplication.Current?.Services.GetService<IBrandTokenProvider>();
        if (registry is null || provider is null)
        {
            ContributionNavHost.IsVisible = false;
            return;
        }

        var items = SourceRailContributionProjection.Project(registry.PagesFor(provider.CurrentSource));
        foreach (var item in items)
        {
            ContributionNavHost.Children.Add(BuildContributionCell(item));
        }

        ContributionNavHost.IsVisible = items.Count > 0;
    }

    private View BuildContributionCell(SourceRailContributionItem item)
    {
        // Same rail-btn anatomy as the static sections: full-rail-width cell, 44px visual,
        // transparent Button overlay carrying the AutomationId + click (Border + tap never
        // surfaces one to Windows UI Automation — dotnet/maui#4715).
        var glyph = new Label
        {
            Text = item.Glyph,
            FontFamily = "TajawalBold",
            FontSize = 17,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = RailWidths.TileSize,
            HeightRequest = RailWidths.TileSize,
        };
        glyph.SetDynamicResource(Label.TextColorProperty, BrandPalette.BrandLinkKey);

        var border = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            WidthRequest = RailWidths.TileSize,
            HeightRequest = RailWidths.TileSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        var hit = new Button
        {
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Text = string.Empty,
            Padding = 0,
            Margin = 0,
            FontFamily = "TajawalMedium",
        };
        hit.AutomationId = $"Sidebar_Contribution_{item.Route}";
        ToolTipProperties.SetText(hit, item.TitleAr);
        SemanticProperties.SetDescription(hit, item.TitleAr);
        hit.Clicked += async (_, _) => await AppRoutes.NavigateAsync(new AppRoute(item.Route));

        var cell = new Grid
        {
            WidthRequest = RailWidths.Width,
            HeightRequest = RailWidths.TileSize,
            HorizontalOptions = LayoutOptions.Center,
        };
        // The glyph itself centers in the 44px button; the border is the hover/press surface.
        cell.Children.Add(border);
        cell.Children.Add(glyph);
        cell.Children.Add(hit);
        return cell;
    }

    private void OnRailLoaded(object? sender, EventArgs e)
    {
        // Bound to the on-screen lifetime on purpose: Application is an app-lifetime publisher, so a
        // constructor subscription would root every rail this process ever builds — a new one per
        // realized host page. Re-applying here also catches a theme flip while the rail was off screen.
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }

        // The active-row ink reads the live brand palette, so a source morph must re-apply the row
        // states too — same on-screen lifetime as the theme subscription above.
        BrandPalette.PaletteChanged -= OnPaletteChanged;
        BrandPalette.PaletteChanged += OnPaletteChanged;

        ApplyActiveState();

        // A switch from another page's rail (while this one was off-screen) may have changed the
        // active source; re-project the contributed rows so the rail matches the live identity.
        RebuildContributions();
    }

    private void OnRailUnloaded(object? sender, EventArgs e)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
        }

        BrandPalette.PaletteChanged -= OnPaletteChanged;
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        ApplyActiveState();
    }

    private void OnPaletteChanged()
    {
        ApplyActiveState();
    }

    private static void OnActivePageChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((SourceRail)bindable).ApplyActiveState();
    }

    private static void OnUnreadCountChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((SourceRail)bindable).UpdateUnreadBadge();
    }

    private void UpdateUnreadBadge()
    {
        var count = UnreadCount;
        UnreadBadge.IsVisible = count > 0;
        UnreadBadgeLabel.Text = count > 9 ? "9+" : count.ToString();
    }

    private void ApplyActiveState()
    {
        SetRowState(ProjectsButton, ProjectsIcon, ProjectsActiveBar, ActivePage == RailPage.Projects);
        SetRowState(AdvancedSearchButton, AdvancedSearchIcon, AdvancedSearchActiveBar, ActivePage == RailPage.AdvancedSearch);
        SetRowState(SettingsButton, SettingsIcon, SettingsActiveBar, ActivePage == RailPage.Settings);
        SetRowState(AboutButton, AboutIcon, AboutActiveBar, ActivePage == RailPage.About);
    }

    /// <summary>
    /// Mockup `.rail-btn.active { background: var(--brand-soft); color: var(--brand-ink) }` + the
    /// 3px inline-start bar. The wash and the bar bind the live brand keys and repaint for free on a
    /// morph; the raster nav icon cannot re-tint (AppIcon history), so the active ink is the
    /// pre-baked "_active" variant — matched the same AppSidebar-proven way, via the palette's
    /// light link value, re-applied on <see cref="BrandPalette.PaletteChanged"/>.
    /// </summary>
    private static void SetRowState(Border row, AppIcon icon, BoxView bar, bool isActive)
    {
        bar.IsVisible = isActive;

        if (isActive)
        {
            row.RemoveDynamicResource(BackgroundColorProperty);
            row.SetDynamicResource(BackgroundColorProperty, BrandPalette.BrandSoftKey);
            icon.TextColor = BrandPalette.LinkLight;
        }
        else
        {
            row.RemoveDynamicResource(BackgroundColorProperty);
            row.BackgroundColor = Colors.Transparent;
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            icon.TextColor = Color.FromArgb(isDark ? InactiveInkDark : InactiveInkLight);
        }
    }

    // --------------------------------------------------------------------------- section clicks

    [TraceInteraction("Sidebar_ProjectsButton")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "Sidebar_ProjectsButton")]
    private void OnProjectsClicked(object? sender, EventArgs e)
    {
        using var _ = TraceScope.Begin("Sidebar_ProjectsButton");
        try
        {
            ProjectsClicked?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    [TraceInteraction("Sidebar_AdvancedSearchButton")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "Sidebar_AdvancedSearchButton")]
    private void OnAdvancedSearchClicked(object? sender, EventArgs e)
    {
        using var _ = TraceScope.Begin("Sidebar_AdvancedSearchButton");
        try
        {
            AdvancedSearchClicked?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    [TraceInteraction("Sidebar_SettingsButton")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "Sidebar_SettingsButton")]
    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        using var _ = TraceScope.Begin("Sidebar_SettingsButton");
        try
        {
            SettingsClicked?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    [TraceInteraction("Sidebar_AboutButton")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "Sidebar_AboutButton")]
    private void OnAboutClicked(object? sender, EventArgs e)
    {
        using var _ = TraceScope.Begin("Sidebar_AboutButton");
        try
        {
            AboutClicked?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    [TraceInteraction("Sidebar_NotificationsButton")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "Sidebar_NotificationsButton")]
    private void OnNotificationsClicked(object? sender, EventArgs e)
    {
        using var _ = TraceScope.Begin("Sidebar_NotificationsButton");
        try
        {
            NotificationsClicked?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    [TraceInteraction("Sidebar_DarkModeToggle")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "Sidebar_DarkModeToggle")]
    private void OnThemeClicked(object? sender, EventArgs e)
    {
        // Behavior-identical relocation of the AppSidebar dark-mode row: the same global (not
        // per-source) key SettingsViewModel writes, then the same UserAppTheme funnel every theme
        // setter in the app — including BrandPalette's repaint — already rides.
        using var _ = TraceScope.Begin("Sidebar_DarkModeToggle");
        try
        {
            var isDark = Application.Current?.RequestedTheme != AppTheme.Dark;

            if (IPlatformApplication.Current?.Services.GetService<ISettingsRepository>() is { } settingsRepository)
            {
                settingsRepository.SetGlobal(SettingsKeys.IsDarkMode, isDark);
            }

            if (Application.Current is { } app)
            {
                app.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
            }
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }
}

/// <summary>
/// The rail's own section vocabulary. Kept independent of the legacy <c>SidebarPage</c> enum so the
/// superseded sidebar can evolve or retire without dragging the rail's contract with it.
/// </summary>
public enum RailPage
{
    None,
    Projects,
    AdvancedSearch,
    Settings,
    About,
}

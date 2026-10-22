using MostaqlK.Core.Sources;
using MostaqlK.Services.Diagnostics;
using MostaqlK.UI.DesignSystem.BrandTokens;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.PlatformComponents.AppSidebar;

/// <summary>
/// Shared sidebar nav-rail unit reused by every page that has the sidebar in its mockup
/// (projects.html, settings.html, about.html, project-details.html): logo, 5 nav items, the
/// "مشاريع مضافة اليوم" stat card, and the dark-mode row. <see cref="ActivePage"/> controls
/// which nav item gets the active (blue) highlight, mirroring the mockups' active-link style.
/// </summary>
public partial class AppSidebar : ContentView
{
    // Dark mode is a deliberately-global (app-wide, not per-source) setting persisted under the same
    // SettingsKeys.IsDarkMode key SettingsViewModel writes, so a toggle from either the sidebar or
    // the Settings page row stays in sync everywhere it appears.

    public static readonly BindableProperty StatValueProperty = BindableProperty.Create(
        nameof(StatValue), typeof(string), typeof(AppSidebar), "0");

    /// <summary>Unread-notification count shown in the blue pill on the التنبيهات row.</summary>

    public static readonly BindableProperty ActivePageProperty = BindableProperty.Create(
        nameof(ActivePage), typeof(SidebarPage), typeof(AppSidebar), SidebarPage.None,
        propertyChanged: OnActivePageChanged);

    public string StatValue
    {
        get => (string)GetValue(StatValueProperty);
        set => SetValue(StatValueProperty, value);
    }


    public SidebarPage ActivePage
    {
        get => (SidebarPage)GetValue(ActivePageProperty);
        set => SetValue(ActivePageProperty, value);
    }

    public event EventHandler? ProjectsClicked;
    public event EventHandler? AdvancedSearchClicked;
    public event EventHandler? SettingsClicked;
    public event EventHandler? AboutClicked;

    /// <summary>
    /// Raised when a source-morph tile taps a new active source. The global projection (persist +
    /// <see cref="GlobalAppStatusService.SetActiveProjection"/> + brand <see cref="IBrandTokenProvider.SwapTo"/>)
    /// already ran inside <see cref="SourceSwitchTiles"/>; this lets a host that owns a source-scoped
    /// surface (the projects feed) re-point its own data to the freshly-active source.
    /// </summary>
    public event EventHandler<SourceId>? SourceChanged;

    // Active row: `bg-blue-50 text-blue-600` / `dark:bg-blue-500/10 dark:text-blue-400`.
    // Inactive row: `text-slate-600` / `dark:text-slate-400`.
    // The active-row INK is no longer a literal: it paints from the live brand palette (the locked
    // link pair the rail historically used), so it re-tints with the active source. The row's wash
    // backgrounds stay inline — legacy Tailwind blues, not members of the locked brand set, kept
    // pixel-identical until the SourceRail (B2/B3) restyles them onto BrandSoft.
    private const string ActiveBackgroundLight = "#EFF6FF";
    private const string ActiveBackgroundDark = "#1A2A44";
    private const string InactiveTextLight = "#475569";
    private const string InactiveTextDark = "#94A3B8";

    private bool _suppressToggleHandler;

    public AppSidebar()
    {
        InitializeComponent();
        try
        {
            VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
        }
        catch
        {
            // Fallback in case AppInfo is unavailable during design-time/isolated testing
            VersionLabel.Text = "v1.0.4";
        }
        ApplyActiveState();
        SyncDarkModeToggleFromCurrentTheme();
        DarkModeToggle.Toggled += OnDarkModeToggleToggled;

        // The morph tiles do the global projection themselves; the sidebar only re-renders the
        // active source's contributed rail pages and bubbles the event for hosts that own a
        // source-scoped surface (the feed).
        SourceTiles.SourceChanged += OnSourceTilesChanged;
        RebuildContributions();

        Loaded += OnSidebarLoaded;
        Unloaded += OnSidebarUnloaded;
    }

    private void OnSourceTilesChanged(object? sender, SourceId source)
    {
        RebuildContributions();
        SourceChanged?.Invoke(this, source);
    }

    /// <summary>
    /// Re-renders the ACTIVE source's contributed rail pages after the standard nav items, in
    /// registry order. Presence/absence is pure data: <c>PagesFor</c> returns the active source's
    /// contributions (empty for a source that ships none), so a new source grows the rail with zero
    /// shared-code change and this never branches on a source id (contract §10).
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
            ContributionNavHost.Children.Add(BuildContributionRow(item));
        }

        ContributionNavHost.IsVisible = items.Count > 0;
    }

    private View BuildContributionRow(SourceRailContributionItem item)
    {
        // Same hit-target pattern as the standard nav rows (transparent Button overlay carries the
        // AutomationId/click; plain Border + tap never surfaces one to Windows UI Automation).
        var glyph = new Label
        {
            Text = item.Glyph,
            FontFamily = "TajawalBold",
            FontSize = 16,
            WidthRequest = 20,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        glyph.SetDynamicResource(Label.TextColorProperty, BrandPalette.BrandLinkKey);

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var title = new Label
        {
            Text = item.TitleAr,
            FontSize = 14,
            FontFamily = "Tajawal",
            VerticalOptions = LayoutOptions.Center,
            TextColor = Color.FromArgb(isDark ? InactiveTextDark : InactiveTextLight),
        };

        var inner = new Grid { ColumnSpacing = 12 };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        inner.Children.Add(glyph);
        inner.Children.Add(title);
        Grid.SetColumn(title, 1);

        var border = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(16, 12),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = inner,
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
        hit.Clicked += async (_, _) => await Core.Navigation.AppRoutes.NavigateAsync(new Core.Navigation.AppRoute(item.Route));

        var cell = new Grid();
        cell.Children.Add(border);
        cell.Children.Add(hit);
        return cell;
    }

    private void OnSidebarLoaded(object? sender, EventArgs e)
    {
        // Bound to the on-screen lifetime on purpose: Application is an app-lifetime publisher, so a
        // constructor subscription would root every sidebar this process ever builds — a new one per
        // realized host page. Re-applying here also catches a theme flip while the rail was off screen.
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }

        // The active-row ink reads the live brand palette, so a source morph must re-apply the
        // row states too — same on-screen lifetime as the theme subscription above.
        MostaqlK.UI.DesignSystem.BrandTokens.BrandPalette.PaletteChanged -= OnPaletteChanged;
        MostaqlK.UI.DesignSystem.BrandTokens.BrandPalette.PaletteChanged += OnPaletteChanged;

        ApplyActiveState();
        SyncDarkModeToggleFromCurrentTheme();

        // A switch from another page's rail (while this sidebar was off-screen) may have changed the
        // active source; re-project the contributed rows so the rail matches the live identity.
        RebuildContributions();
    }

    private void OnSidebarUnloaded(object? sender, EventArgs e)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
        }

        MostaqlK.UI.DesignSystem.BrandTokens.BrandPalette.PaletteChanged -= OnPaletteChanged;
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        ApplyActiveState();
        SyncDarkModeToggleFromCurrentTheme();
    }

    private void OnPaletteChanged()
    {
        ApplyActiveState();
    }

    private void SyncDarkModeToggleFromCurrentTheme()
    {
        _suppressToggleHandler = true;
        DarkModeToggle.IsToggled = Application.Current?.RequestedTheme == AppTheme.Dark;
        _suppressToggleHandler = false;
    }

    [TraceInteraction("Sidebar_DarkModeToggle")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "Sidebar_DarkModeToggle")]
    private void OnDarkModeToggleToggled(object? sender, ToggledEventArgs e)
    {
        // Reentrancy guard: SyncDarkModeToggleFromCurrentTheme sets IsToggled in response to a
        // theme change, which would otherwise re-enter this handler and re-apply the same theme.
        if (_suppressToggleHandler)
        {
            return;
        }

        using var _ = TraceScope.Begin("Sidebar_DarkModeToggle");
        try
        {
            // Persist through the shared settings repository (global dark-mode key), the same one
            // SettingsViewModel writes, so both surfaces stay in sync and the write is namespaced
            // consistently with the rest of the app's settings.
            if (IPlatformApplication.Current?.Services.GetService<ISettingsRepository>() is { } settingsRepository)
            {
                settingsRepository.SetGlobal(SettingsKeys.IsDarkMode, e.Value);
            }

            if (Application.Current is { } app)
            {
                app.UserAppTheme = e.Value ? AppTheme.Dark : AppTheme.Light;
            }
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }

    private static void OnActivePageChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((AppSidebar)bindable).ApplyActiveState();
    }

    private void ApplyActiveState()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        SetRowState(ProjectsButton, ProjectsIcon, ProjectsLabel, ActivePage == SidebarPage.Projects, isDark);
        SetRowState(AdvancedSearchButton, AdvancedSearchIcon, AdvancedSearchLabel, ActivePage == SidebarPage.AdvancedSearch, isDark);
        SetRowState(SettingsButton, SettingsIcon, SettingsLabel, ActivePage == SidebarPage.Settings, isDark);
        SetRowState(AboutButton, AboutIcon, AboutLabel, ActivePage == SidebarPage.About, isDark);
        ProjectsActiveBar.IsVisible = ActivePage == SidebarPage.Projects;
        AdvancedSearchActiveBar.IsVisible = ActivePage == SidebarPage.AdvancedSearch;
        SettingsActiveBar.IsVisible = ActivePage == SidebarPage.Settings;
        AboutActiveBar.IsVisible = ActivePage == SidebarPage.About;
    }

    private static void SetRowState(Border row, AppIcon icon, Label label, bool isActive, bool isDark)
    {
        row.BackgroundColor = isActive
            ? Color.FromArgb(isDark ? ActiveBackgroundDark : ActiveBackgroundLight)
            : Colors.Transparent;
        // Active ink = the live brand's link pair (the rail's historical highlight blues, now
        // provider-owned); inactive stays the neutral slate — state chrome, never brand-retinted.
        var textColor = isActive
            ? (isDark
                ? MostaqlK.UI.DesignSystem.BrandTokens.BrandPalette.LinkDark
                : MostaqlK.UI.DesignSystem.BrandTokens.BrandPalette.LinkLight)
            : Color.FromArgb(isDark ? InactiveTextDark : InactiveTextLight);
        icon.TextColor = textColor;
        label.TextColor = textColor;
        // The mockups' active nav <a> carries `font-medium`; inactive rows use the regular weight.
        label.FontFamily = isActive ? "TajawalMedium" : "Tajawal";
    }

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
}

public enum SidebarPage
{
    None,
    Projects,
    AdvancedSearch,
    Settings,
    About
}

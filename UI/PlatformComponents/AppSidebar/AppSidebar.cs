using MostaqlK.Services.Diagnostics;
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
    // Same Preferences key SettingsViewModel persists dark-mode under, so a toggle from either
    // the sidebar or the Settings page row stays in sync everywhere it appears.
    private const string KeyIsDarkMode = "settings_is_dark_mode";

    public static readonly BindableProperty StatValueProperty = BindableProperty.Create(
        nameof(StatValue), typeof(string), typeof(AppSidebar), "0");

    /// <summary>Unread-notification count shown in the blue pill on the التنبيهات row.</summary>
    public static readonly BindableProperty NotificationCountProperty = BindableProperty.Create(
        nameof(NotificationCount), typeof(string), typeof(AppSidebar), "0");

    public static readonly BindableProperty ActivePageProperty = BindableProperty.Create(
        nameof(ActivePage), typeof(SidebarPage), typeof(AppSidebar), SidebarPage.None,
        propertyChanged: OnActivePageChanged);

    public string StatValue
    {
        get => (string)GetValue(StatValueProperty);
        set => SetValue(StatValueProperty, value);
    }

    public string NotificationCount
    {
        get => (string)GetValue(NotificationCountProperty);
        set => SetValue(NotificationCountProperty, value);
    }

    public SidebarPage ActivePage
    {
        get => (SidebarPage)GetValue(ActivePageProperty);
        set => SetValue(ActivePageProperty, value);
    }

    public event EventHandler? ProjectsClicked;
    public event EventHandler? AdvancedSearchClicked;
    public event EventHandler? NotificationsClicked;
    public event EventHandler? SettingsClicked;
    public event EventHandler? AboutClicked;

    // Active row: `bg-blue-50 text-blue-600` / `dark:bg-blue-500/10 dark:text-blue-400`.
    // Inactive row: `text-slate-600` / `dark:text-slate-400`.
    private const string ActiveBackgroundLight = "#EFF6FF";
    private const string ActiveBackgroundDark = "#1A2A44";
    private const string ActiveTextLight = "#2563EB";
    private const string ActiveTextDark = "#60A5FA";
    private const string InactiveTextLight = "#475569";
    private const string InactiveTextDark = "#94A3B8";

    private bool _suppressToggleHandler;

    public AppSidebar()
    {
        InitializeComponent();
        ApplyActiveState();
        SyncDarkModeToggleFromCurrentTheme();
        DarkModeToggle.Toggled += OnDarkModeToggleToggled;
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged += (_, _) =>
            {
                ApplyActiveState();
                SyncDarkModeToggleFromCurrentTheme();
            };
        }
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
            Microsoft.Maui.Storage.Preferences.Set(KeyIsDarkMode, e.Value);
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
        SetRowState(NotificationsButton, NotificationsIcon, NotificationsLabel, ActivePage == SidebarPage.Notifications, isDark);
        SetRowState(SettingsButton, SettingsIcon, SettingsLabel, ActivePage == SidebarPage.Settings, isDark);
        SetRowState(AboutButton, AboutIcon, AboutLabel, ActivePage == SidebarPage.About, isDark);
        ProjectsActiveBar.IsVisible = ActivePage == SidebarPage.Projects;
        AdvancedSearchActiveBar.IsVisible = ActivePage == SidebarPage.AdvancedSearch;
        NotificationsActiveBar.IsVisible = ActivePage == SidebarPage.Notifications;
        SettingsActiveBar.IsVisible = ActivePage == SidebarPage.Settings;
        AboutActiveBar.IsVisible = ActivePage == SidebarPage.About;
    }

    private static void SetRowState(Border row, AppIcon icon, Label label, bool isActive, bool isDark)
    {
        row.BackgroundColor = isActive
            ? Color.FromArgb(isDark ? ActiveBackgroundDark : ActiveBackgroundLight)
            : Colors.Transparent;
        var textColor = isActive
            ? Color.FromArgb(isDark ? ActiveTextDark : ActiveTextLight)
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
    Notifications,
    Settings,
    About
}

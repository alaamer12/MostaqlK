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
    public static readonly BindableProperty StatValueProperty = BindableProperty.Create(
        nameof(StatValue), typeof(string), typeof(AppSidebar), "0");

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
    public event EventHandler? NotificationsClicked;
    public event EventHandler? SettingsClicked;
    public event EventHandler? AboutClicked;

    private const string ActiveBackground = "#EFF6FF";
    private const string ActiveText = "#2563EB";
    private const string InactiveText = "#475569";

    public AppSidebar()
    {
        InitializeComponent();
        ApplyActiveState();
    }

    private static void OnActivePageChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((AppSidebar)bindable).ApplyActiveState();
    }

    private void ApplyActiveState()
    {
        SetRowState(ProjectsButton, ProjectsIcon, ProjectsLabel, ActivePage == SidebarPage.Projects);
        SetRowState(AdvancedSearchButton, AdvancedSearchIcon, AdvancedSearchLabel, ActivePage == SidebarPage.AdvancedSearch);
        SetRowState(NotificationsButton, NotificationsIcon, NotificationsLabel, ActivePage == SidebarPage.Notifications);
        SetRowState(SettingsButton, SettingsIcon, SettingsLabel, ActivePage == SidebarPage.Settings);
        SetRowState(AboutButton, AboutIcon, AboutLabel, ActivePage == SidebarPage.About);
        ProjectsActiveBar.IsVisible = ActivePage == SidebarPage.Projects;
        AdvancedSearchActiveBar.IsVisible = ActivePage == SidebarPage.AdvancedSearch;
        NotificationsActiveBar.IsVisible = ActivePage == SidebarPage.Notifications;
        SettingsActiveBar.IsVisible = ActivePage == SidebarPage.Settings;
        AboutActiveBar.IsVisible = ActivePage == SidebarPage.About;
    }

    private static void SetRowState(Border row, AppIcon icon, Label label, bool isActive)
    {
        row.BackgroundColor = isActive ? Color.FromArgb(ActiveBackground) : Colors.Transparent;
        var textColor = isActive ? Color.FromArgb(ActiveText) : Color.FromArgb(InactiveText);
        icon.TextColor = textColor;
        label.TextColor = textColor;
    }

    private void OnProjectsClicked(object? sender, TappedEventArgs e) => ProjectsClicked?.Invoke(this, EventArgs.Empty);

    private void OnAdvancedSearchClicked(object? sender, TappedEventArgs e) => AdvancedSearchClicked?.Invoke(this, EventArgs.Empty);

    private void OnNotificationsClicked(object? sender, TappedEventArgs e) => NotificationsClicked?.Invoke(this, EventArgs.Empty);

    private void OnSettingsClicked(object? sender, TappedEventArgs e) => SettingsClicked?.Invoke(this, EventArgs.Empty);

    private void OnAboutClicked(object? sender, TappedEventArgs e) => AboutClicked?.Invoke(this, EventArgs.Empty);
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

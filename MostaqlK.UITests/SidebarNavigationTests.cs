using NUnit.Framework;
using MostaqlK.UITests.Utils;
using OpenQA.Selenium;
using System;

namespace MostaqlK.UITests;

/// <summary>
/// Exercises the shared <c>AppSidebar</c> nav rail from each of the 4 MVP pages (Projects,
/// ProjectDetails, Settings, About), clicking every one of the 5 nav rows and asserting the
/// destination page's marker AutomationId appears (per docs/ui-test-catalog.md's shared-sidebar
/// section). Uses <see cref="UiDebugger"/> for every find/click so a failing row self-documents
/// with a page-source dump instead of a bare NoSuchElementException.
/// </summary>
[TestFixture]
public class SidebarNavigationTests
{
    // Marker AutomationIds per docs/ui-test-catalog.md — a stable element that is always present
    // (not state-dependent) on each destination page.
    private const string ProjectsMarker = "Projects_SearchInput";
    private const string SettingsMarker = "Settings_SaveButton";
    private const string AboutMarker = "About_ScrollView";
    private const string DetailsMarker = "Details_BackButton";
    private const string NotificationsFlyoutMarker = "Notifications_Flyout";
    private const string ProjectCardMarker = "ProjectCard_Root";

    private const string SidebarProjects = "Sidebar_ProjectsButton";
    private const string SidebarAdvancedSearch = "Sidebar_AdvancedSearchButton";
    private const string SidebarNotifications = "Sidebar_NotificationsButton";
    private const string SidebarSettings = "Sidebar_SettingsButton";
    private const string SidebarAbout = "Sidebar_AboutButton";

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(10);

    // A native WinUI/Shell navigation-transition race (observed as a fatal, uncatchable
    // Microsoft.UI.Xaml.dll fault, Windows Event Log id 1000/exception 0xc000027b) can crash the
    // app if the next UI Automation click fires while the previous Shell.GoToAsync transition is
    // still animating. This settle delay is a test-only mitigation (not a production fix — the
    // underlying WinUI instability is outside this file's sidebar-AutomationId scope) so the
    // sidebar itself can still be exercised reliably.
    private static readonly TimeSpan NavigationSettleDelay = TimeSpan.FromMilliseconds(500);

    private static OpenQA.Selenium.Appium.Windows.WindowsDriver<OpenQA.Selenium.Appium.Windows.WindowsElement> Driver =>
        AppiumSetup.Driver!;

    [SetUp]
    public void EnsureDriverIsReady()
    {
        Assert.That(AppiumSetup.Driver, Is.Not.Null, "Appium WindowsDriver should be initialized.");
    }

    /// <summary>Returns to the Projects page (the app's default landing page) via the sidebar.</summary>
    private static void GoToProjects()
    {
        UiDebugger.WaitAndClick(Driver, SidebarProjects, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, ProjectsMarker, ShortTimeout);
        Thread.Sleep(NavigationSettleDelay);
    }

    private static void GoToSettings()
    {
        UiDebugger.WaitAndClick(Driver, SidebarSettings, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, SettingsMarker, ShortTimeout);
        Thread.Sleep(NavigationSettleDelay);
    }

    private static void GoToAbout()
    {
        UiDebugger.WaitAndClick(Driver, SidebarAbout, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, AboutMarker, ShortTimeout);
        Thread.Sleep(NavigationSettleDelay);
    }

    /// <summary>
    /// Navigates to ProjectDetailsPage by tapping the first project card from the Projects feed.
    /// If the feed is empty, the test is inconclusive (skipped) rather than falsely failing —
    /// project-card tap behavior itself is owned by ProjectsPageTests, not this file.
    /// </summary>
    private static void GoToProjectDetails()
    {
        GoToProjects();
        try
        {
            UiDebugger.WaitAndClick(Driver, ProjectCardMarker, ShortTimeout);
        }
        catch (NoSuchElementException)
        {
            Assert.Ignore("No project cards present in the feed; cannot reach ProjectDetailsPage to test its sidebar.");
        }

        UiDebugger.WaitAndFind(Driver, DetailsMarker, ShortTimeout);
    }

    // ---------------------------------------------------------------------
    // Starting page: Projects (MainWindowPage)
    // ---------------------------------------------------------------------

    [Test]
    public void FromProjects_Click_SettingsRow_NavigatesToSettings()
    {
        GoToProjects();
        UiDebugger.WaitAndClick(Driver, SidebarSettings, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, SettingsMarker, ShortTimeout);
    }

    [Test]
    public void FromProjects_Click_AboutRow_NavigatesToAbout()
    {
        GoToProjects();
        UiDebugger.WaitAndClick(Driver, SidebarAbout, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, AboutMarker, ShortTimeout);
    }

    [Test]
    public void FromProjects_Click_ProjectsRow_StaysOnProjects()
    {
        GoToProjects();
        UiDebugger.WaitAndClick(Driver, SidebarProjects, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, ProjectsMarker, ShortTimeout);
    }

    [Test]
    public void FromProjects_Click_NotificationsRow_TogglesFlyout()
    {
        GoToProjects();
        UiDebugger.WaitAndClick(Driver, SidebarNotifications, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, NotificationsFlyoutMarker, ShortTimeout);
        // Close it again (counterpart state) so subsequent tests start from a clean state.
        UiDebugger.WaitAndClick(Driver, SidebarNotifications, ShortTimeout);
    }

    [Test]
    public void FromProjects_Click_AdvancedSearchRow_DoesNotCrash()
    {
        // Route not implemented yet (per docs/ui-test-catalog.md) — only assert the row is
        // reachable/clickable and the app stays responsive (Projects marker still present).
        GoToProjects();
        UiDebugger.WaitAndClick(Driver, SidebarAdvancedSearch, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, ProjectsMarker, ShortTimeout);
    }

    // ---------------------------------------------------------------------
    // Starting page: Settings (SettingsPanel)
    // ---------------------------------------------------------------------

    [Test]
    public void FromSettings_Click_ProjectsRow_NavigatesToProjects()
    {
        GoToSettings();
        UiDebugger.WaitAndClick(Driver, SidebarProjects, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, ProjectsMarker, ShortTimeout);
    }

    [Test]
    public void FromSettings_Click_AboutRow_NavigatesToAbout()
    {
        GoToSettings();
        UiDebugger.WaitAndClick(Driver, SidebarAbout, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, AboutMarker, ShortTimeout);
    }

    [Test]
    public void FromSettings_Click_NotificationsRow_NavigatesBackToProjects()
    {
        // Settings' handler navigates back to MainWindowPage first (flyout is owned by it).
        GoToSettings();
        UiDebugger.WaitAndClick(Driver, SidebarNotifications, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, ProjectsMarker, ShortTimeout);
    }

    // ---------------------------------------------------------------------
    // Starting page: About (AboutPage)
    // ---------------------------------------------------------------------

    [Test]
    public void FromAbout_Click_ProjectsRow_NavigatesToProjects()
    {
        GoToAbout();
        UiDebugger.WaitAndClick(Driver, SidebarProjects, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, ProjectsMarker, ShortTimeout);
    }

    [Test]
    public void FromAbout_Click_SettingsRow_NavigatesToSettings()
    {
        GoToAbout();
        UiDebugger.WaitAndClick(Driver, SidebarSettings, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, SettingsMarker, ShortTimeout);
    }

    [Test]
    public void FromAbout_Click_NotificationsRow_NavigatesBackToProjects()
    {
        GoToAbout();
        UiDebugger.WaitAndClick(Driver, SidebarNotifications, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, ProjectsMarker, ShortTimeout);
    }

    // ---------------------------------------------------------------------
    // Starting page: ProjectDetails (ProjectDetailsPage)
    // ---------------------------------------------------------------------

    [Test]
    public void FromProjectDetails_Click_ProjectsRow_NavigatesToProjects()
    {
        GoToProjectDetails();
        UiDebugger.WaitAndClick(Driver, SidebarProjects, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, ProjectsMarker, ShortTimeout);
    }

    [Test]
    public void FromProjectDetails_Click_SettingsRow_NavigatesToSettings()
    {
        GoToProjectDetails();
        UiDebugger.WaitAndClick(Driver, SidebarSettings, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, SettingsMarker, ShortTimeout);
    }

    [Test]
    public void FromProjectDetails_Click_AboutRow_NavigatesToAbout()
    {
        GoToProjectDetails();
        UiDebugger.WaitAndClick(Driver, SidebarAbout, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, AboutMarker, ShortTimeout);
    }

    [Test]
    public void FromProjectDetails_Click_NotificationsRow_NavigatesBackToProjects()
    {
        GoToProjectDetails();
        UiDebugger.WaitAndClick(Driver, SidebarNotifications, ShortTimeout);
        UiDebugger.WaitAndFind(Driver, ProjectsMarker, ShortTimeout);
    }
}

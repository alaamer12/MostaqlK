using NUnit.Framework;
using OpenQA.Selenium;
using MostaqlK.UITests.Utils;

namespace MostaqlK.UITests;

[TestFixture]
public class SidebarPersistenceTests : AppiumSetup
{
    [Test]
    public void SidebarStat_PersistsAcrossNavigation()
    {
        // 1. Wait for MainWindowPage to load.
        var statLabel = UiDebugger.WaitAndFind(Driver, "Sidebar_StatValueLabel");
        
        // Wait for data to load (it might be "0" initially, then update to "12" due to DesignDataSeeder).
        var wait = new OpenQA.Selenium.Support.UI.WebDriverWait(Driver, TimeSpan.FromSeconds(10));
        wait.Until(d => statLabel.Text != "0");
        
        var initialValue = statLabel.Text;
        Assert.That(initialValue, Is.Not.EqualTo("0"), "Stat value should be loaded (non-zero).");

        // 2. Navigate to Settings.
        UiDebugger.WaitAndClick(Driver, "Sidebar_SettingsButton");
        var settingsStatLabel = UiDebugger.WaitAndFind(Driver, "Sidebar_StatValueLabel");
        Assert.That(settingsStatLabel.Text, Is.EqualTo(initialValue), "Stat value should persist on Settings page.");

        // 3. Navigate to About.
        UiDebugger.WaitAndClick(Driver, "Sidebar_AboutButton");
        var aboutStatLabel = UiDebugger.WaitAndFind(Driver, "Sidebar_StatValueLabel");
        Assert.That(aboutStatLabel.Text, Is.EqualTo(initialValue), "Stat value should persist on About page.");

        // 4. Navigate back to Projects.
        UiDebugger.WaitAndClick(Driver, "Sidebar_ProjectsButton");
        var projectsStatLabel = UiDebugger.WaitAndFind(Driver, "Sidebar_StatValueLabel");
        Assert.That(projectsStatLabel.Text, Is.EqualTo(initialValue), "Stat value should persist when returning to Projects page.");
    }
}

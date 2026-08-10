using NUnit.Framework;
using MostaqlK.UITests.Utils;
using OpenQA.Selenium.Appium.Windows;
using System;
using System.Threading;

namespace MostaqlK.UITests;

/// <summary>
/// Exercises the About page (<c>AboutPage.xaml</c>) catalog cases from
/// <c>docs/ui-test-catalog.md</c>: tapping the Mostaqlk footer link (asserting it is
/// invokable/doesn't crash the app — <c>Launcher.OpenAsync</c> hands off to the OS shell, which
/// this harness cannot observe directly, so the assertion is "the tap registers and the page
/// stays responsive") and scrolling the facts/roadmap list.
/// </summary>
[TestFixture]
public class AboutPageTests
{
    private static WindowsDriver<WindowsElement> Driver => AppiumSetup.Driver!;

    // Same test-only mitigation as SidebarNavigationTests: a native WinUI/Shell navigation-
    // transition race can fatally crash the app if the next UI Automation click fires while the
    // previous Shell.GoToAsync transition is still animating.
    private static readonly TimeSpan NavigationSettleDelay = TimeSpan.FromMilliseconds(2000);

    private static void EnsureAboutPage()
    {
        try
        {
            UiDebugger.WaitAndFind(Driver, "About_ScrollView", TimeSpan.FromSeconds(3));
            return;
        }
        catch (OpenQA.Selenium.NoSuchElementException)
        {
            UiDebugger.WaitAndClick(Driver, "Sidebar_AboutButton");
            UiDebugger.WaitAndFind(Driver, "About_ScrollView");
            Thread.Sleep(NavigationSettleDelay);
        }
    }

    [SetUp]
    public void SetUp() => EnsureAboutPage();

    [Test]
    public void Click_MostaqlLink_IsInvokable_AndDoesNotCrash()
    {
        var link = UiDebugger.WaitAndFind(Driver, "About_MostaqlLink");

        // Tapping this label hands off to Launcher.Default.OpenAsync (opens the OS-default
        // browser out-of-process); this harness cannot observe the external browser window, so
        // the assertion is that the tap registers without throwing and the app/page stays
        // responsive afterward (no crash, no freeze).
        link.Click();
        Thread.Sleep(1000);

        UiDebugger.WaitAndFind(Driver, "About_ScrollView");
        UiDebugger.WaitAndFind(Driver, "About_MostaqlLink");
    }

    [Test]
    public void Scroll_AboutScrollView_IsPannable()
    {
        var scrollView = UiDebugger.WaitAndFind(Driver, "About_ScrollView");
        var size = scrollView.Size;

        var startOffsetX = size.Width / 2;
        var startOffsetY = (int)(size.Height * 0.8);
        var dragDistance = (int)(size.Height * 0.6);

        new OpenQA.Selenium.Interactions.Actions(Driver)
            .MoveToElement(scrollView, startOffsetX, startOffsetY)
            .ClickAndHold()
            .MoveByOffset(0, -dragDistance)
            .Release()
            .Perform();

        Thread.Sleep(500);

        // No crash/exception is the primary assertion; the ScrollView (and the roadmap/facts
        // content reachable within it) must still be present.
        UiDebugger.WaitAndFind(Driver, "About_ScrollView");
    }
}

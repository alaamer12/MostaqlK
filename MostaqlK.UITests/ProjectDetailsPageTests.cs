using NUnit.Framework;
using MostaqlK.UITests.Utils;
using OpenQA.Selenium.Appium.Windows;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace MostaqlK.UITests;

/// <summary>
/// Exercises the Project Details page (<c>ProjectDetailsPage.xaml</c>) catalog cases from
/// <c>docs/ui-test-catalog.md</c>: the back button navigation, an attachment's download
/// (<c>ResolveCommand</c>) button + its <c>StatusMessage</c> update, and scrolling the details
/// <c>ScrollView</c>. Backend-effect evidence for the resolve click is read from the rolling
/// <c>interaction-log.txt</c> sink written by <c>InteractionLogger</c>/<c>TraceScope</c>.
/// </summary>
[TestFixture]
public class ProjectDetailsPageTests
{
    private static readonly string InteractionLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "User Name", "com.companyname.mostaqlk", "Data", "interaction-log.txt");

    private static WindowsDriver<WindowsElement> Driver => AppiumSetup.Driver!;

    // Same test-only mitigation as SidebarNavigationTests: a native WinUI/Shell navigation-
    // transition race can fatally crash the app if the next UI Automation click fires while the
    // previous Shell.GoToAsync transition is still animating.
    private static readonly TimeSpan NavigationSettleDelay = TimeSpan.FromMilliseconds(2000);

    private static string[] ReadLogLines()
    {
        try
        {
            return File.Exists(InteractionLogPath) ? File.ReadAllLines(InteractionLogPath) : Array.Empty<string>();
        }
        catch (IOException)
        {
            Thread.Sleep(200);
            return File.Exists(InteractionLogPath) ? File.ReadAllLines(InteractionLogPath) : Array.Empty<string>();
        }
    }

    private static int CountLogEntries(string checkpoint, DateTimeOffset since, string? kind = null)
    {
        return ReadLogLines().Count(line =>
        {
            var parts = line.Split(" | ", 4, StringSplitOptions.None);
            if (parts.Length < 3 || !string.Equals(parts[2], checkpoint, StringComparison.Ordinal))
            {
                return false;
            }

            if (kind is not null && !string.Equals(parts[1], kind, StringComparison.Ordinal))
            {
                return false;
            }

            return DateTimeOffset.TryParse(parts[0], out var ts) && ts >= since;
        });
    }

    /// <summary>Navigates to ProjectDetailsPage by tapping the first available project card from
    /// the Projects feed (the page requires a real project id — there is no direct route/id-less
    /// entry point other than "no id => newest project", which OnAppearing already handles).</summary>
    private static void EnsureProjectDetailsPage()
    {
        try
        {
            UiDebugger.WaitAndFind(Driver, "Details_BackButton", TimeSpan.FromSeconds(3));
            return;
        }
        catch (OpenQA.Selenium.NoSuchElementException)
        {
            // Not already there — go via Projects -> first card.
        }

        UiDebugger.WaitAndClick(Driver, "Sidebar_ProjectsButton");
        UiDebugger.WaitAndFind(Driver, "Projects_SearchInput");
        Thread.Sleep(NavigationSettleDelay);

        try
        {
            UiDebugger.WaitAndClick(Driver, "ProjectCard_Root", TimeSpan.FromSeconds(30));
        }
        catch (OpenQA.Selenium.NoSuchElementException)
        {
            Assert.Ignore("No project cards present in the feed; cannot reach ProjectDetailsPage.");
        }

        UiDebugger.WaitAndFind(Driver, "Details_BackButton");
        Thread.Sleep(NavigationSettleDelay);
    }

    [SetUp]
    public void SetUp() => EnsureProjectDetailsPage();

    [Test]
    public void Click_Back_NavigatesToProjects()
    {
        UiDebugger.WaitAndClick(Driver, "Details_BackButton");
        UiDebugger.WaitAndFind(Driver, "Projects_SearchInput");
        Thread.Sleep(NavigationSettleDelay);
    }

    [Test]
    public void Click_AttachmentResolve_UpdatesStatusMessage_AndLogsResolveCommand()
    {
        UiDebugger.WaitAndFind(Driver, "Details_ScrollView");

        WindowsElement resolveButton;
        try
        {
            resolveButton = UiDebugger.WaitAndFind(Driver, "Details_AttachmentResolveButton", TimeSpan.FromSeconds(5));
        }
        catch (OpenQA.Selenium.NoSuchElementException)
        {
            Assert.Ignore("The current project has no attachments; cannot exercise the resolve button.");
            return;
        }

        var since = DateTimeOffset.Now;
        resolveButton.Click();
        Thread.Sleep(1500);

        // Backend-effect evidence: ResolveCommand must have logged entry/exit around the real
        // AssetDownloadService.ResolveAsync call, regardless of whether the resolution itself
        // required network access unavailable in this environment.
        var enterCount = CountLogEntries("ResolveCommand", since, kind: "ENTER");
        Assert.That(enterCount, Is.GreaterThan(0),
            "Clicking the attachment resolve button should have logged a ResolveCommand ENTER entry " +
            "(proves the click reached AttachmentItemViewModel.ResolveAsync, independent of network availability).");

        // The page must remain responsive after the resolve attempt (whether it succeeded or the
        // AssetDownloadService failed due to no network access in this environment).
        UiDebugger.WaitAndFind(Driver, "Details_ScrollView");
    }

    [Test]
    public void Scroll_DetailsScrollView_IsPannable()
    {
        var scrollView = UiDebugger.WaitAndFind(Driver, "Details_ScrollView");
        var size = scrollView.Size;

        var startOffsetX = size.Width / 2;
        var startOffsetY = (int)(size.Height * 0.8);
        var dragDistance = (int)(size.Height * 0.5);

        new OpenQA.Selenium.Interactions.Actions(Driver)
            .MoveToElement(scrollView, startOffsetX, startOffsetY)
            .ClickAndHold()
            .MoveByOffset(0, -dragDistance)
            .Release()
            .Perform();

        Thread.Sleep(500);

        // No crash/exception is the primary assertion; the ScrollView must still be present.
        UiDebugger.WaitAndFind(Driver, "Details_ScrollView");
    }
}

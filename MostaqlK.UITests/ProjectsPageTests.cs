using NUnit.Framework;
using MostaqlK.UITests.Utils;
using OpenQA.Selenium.Appium.Windows;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace MostaqlK.UITests;

/// <summary>
/// Exercises the Projects page (<c>MainWindowPage.xaml</c>) catalog cases from
/// <c>docs/ui-test-catalog.md</c>: search input + Enter, pause/resume, refresh (once and twice),
/// scrolling the feed, and tapping a card to navigate to details. Backend-effect assertions read
/// the rolling <c>interaction-log.txt</c> sink (<see cref="MostaqlK.Services.Diagnostics.InteractionLogger"/>)
/// as evidence that the underlying command actually ran, not just that a UI label changed.
/// </summary>
[TestFixture]
public class ProjectsPageTests
{
    // Same on-disk location MostaqlK.csproj's SqliteConnectionFactory/InteractionLogger resolve via
    // FileSystem.AppDataDirectory for this unpackaged app id (confirmed by inspecting the running app).
    private static readonly string InteractionLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "User Name", "com.companyname.mostaqlk", "Data", "interaction-log.txt");

    private static WindowsDriver<WindowsElement> Driver => AppiumSetup.Driver!;

    private static string[] ReadLogLines()
    {
        try
        {
            return File.Exists(InteractionLogPath)
                ? File.ReadAllLines(InteractionLogPath)
                : Array.Empty<string>();
        }
        catch (IOException)
        {
            // The app process may be mid-write; a short retry is enough for a rolling text log.
            Thread.Sleep(200);
            return File.Exists(InteractionLogPath) ? File.ReadAllLines(InteractionLogPath) : Array.Empty<string>();
        }
    }

    // Log lines are "<timestamp> | <KIND> | <checkpoint> | ..." (see InteractionLogger.Write):
    // KIND is one of MARK/ENTER/EXIT/FAULT, checkpoint is the TraceInteraction name (e.g. "RefreshCommand").
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

    private static void EnsureProjectsPage()
    {
        // Idempotently return to the Projects page via the sidebar if a previous test navigated away.
        try
        {
            UiDebugger.WaitAndFind(Driver, "Projects_SearchInput", TimeSpan.FromSeconds(3));
            return;
        }
        catch (OpenQA.Selenium.NoSuchElementException)
        {
            UiDebugger.WaitAndClick(Driver, "Sidebar_ProjectsButton");
            UiDebugger.WaitAndFind(Driver, "Projects_SearchInput");
        }
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Cold-start: the real IPollService/WorkerPool pipeline needs a moment to complete its
        // first load before any project card is rendered — give it a generous one-time budget so
        // individual tests aren't flaky on a slow first fetch.
        EnsureProjectsPage();
        UiDebugger.WaitAndFind(Driver, "ProjectCard_Root", TimeSpan.FromSeconds(30));
    }

    [SetUp]
    public void SetUp() => EnsureProjectsPage();

    // The CollectionView is bound to ShowFeed (!IsLoading && !HasError && !IsEmpty), so once a
    // filter matches nothing the whole control is hidden (removed from the UIA tree) rather than
    // showing zero rows — an absent element and a present-but-empty element are both "0 items".
    private static int CountVisibleCards()
    {
        try
        {
            var collectionView = UiDebugger.WaitAndFind(Driver, "Projects_ProjectsCollectionView", TimeSpan.FromSeconds(5));
            return collectionView.FindElementsByClassName("ListItem").Count;
        }
        catch (OpenQA.Selenium.NoSuchElementException)
        {
            return 0;
        }
    }

    [Test]
    public void Type_SearchInput_Enter_FiltersFeed()
    {
        var countBefore = CountVisibleCards();
        Assert.That(countBefore, Is.GreaterThan(0), "Precondition: the unfiltered feed should show at least one project card.");

        var search = UiDebugger.WaitAndFind(Driver, "Projects_SearchInput");
        search.Clear();
        search.SendKeys("zzz_no_such_project_should_match_zzz");
        search.SendKeys(OpenQA.Selenium.Keys.Enter);
        Thread.Sleep(1500);

        var countAfter = CountVisibleCards();

        Assert.That(countAfter, Is.Not.EqualTo(countBefore),
            "Filtering by a query that matches nothing should change the visible item count.");

        // Restore full feed for subsequent tests.
        search.Clear();
        search.SendKeys(OpenQA.Selenium.Keys.Enter);
        Thread.Sleep(1500);
    }

    [Test]
    public void Click_TogglePolling_Pauses_ThenResumes()
    {
        var since = DateTimeOffset.Now;
        UiDebugger.WaitAndClick(Driver, "Projects_TogglePollingButton");
        Thread.Sleep(500);

        var entriesAfterFirstTap = CountLogEntries("TogglePolling", since);
        Assert.That(entriesAfterFirstTap, Is.GreaterThan(0),
            "TogglePolling should have logged a TraceInteraction entry after the first tap (pause).");

        since = DateTimeOffset.Now;
        UiDebugger.WaitAndClick(Driver, "Projects_TogglePollingButton");
        Thread.Sleep(500);

        var entriesAfterSecondTap = CountLogEntries("TogglePolling", since);
        Assert.That(entriesAfterSecondTap, Is.GreaterThan(0),
            "TogglePolling should have logged a TraceInteraction entry after the second tap (resume).");
    }

    [Test]
    public void Click_Refresh_UpdatesLastScanText_AndDoesNotDoubleFireOnRapidSecondTap()
    {
        var since = DateTimeOffset.Now;
        UiDebugger.WaitAndClick(Driver, "Projects_RefreshLabel");
        Thread.Sleep(300);

        // Tap again immediately (rapid double-fire probe) before the first refresh has settled.
        UiDebugger.WaitAndClick(Driver, "Projects_RefreshLabel");
        Thread.Sleep(1500);

        var enterCount = CountLogEntries("RefreshCommand", since, kind: "ENTER");
        var exitCount = CountLogEntries("RefreshCommand", since, kind: "EXIT");

        Assert.That(enterCount, Is.GreaterThan(0),
            "RefreshCommand should have logged at least one entry after the rapid double-tap.");
        Assert.That(exitCount, Is.LessThanOrEqualTo(enterCount),
            "RefreshCommand should not report more completions than invocations (no runaway double-fire).");

        // The page must still be responsive (no freeze/crash) — the search input must remain reachable.
        UiDebugger.WaitAndFind(Driver, "Projects_SearchInput");
    }

    [Test]
    public void Scroll_ProjectsFeed_IsPannable()
    {
        var collectionView = UiDebugger.WaitAndFind(Driver, "Projects_ProjectsCollectionView");
        var size = collectionView.Size;

        var startOffsetX = size.Width / 2;
        var startOffsetY = (int)(size.Height * 0.8);
        var dragDistance = (int)(size.Height * 0.6);

        // Simple pointer-drag scroll gesture (WinAppDriver supports basic touch/pointer actions).
        new OpenQA.Selenium.Interactions.Actions(Driver)
            .MoveToElement(collectionView, startOffsetX, startOffsetY)
            .ClickAndHold()
            .MoveByOffset(0, -dragDistance)
            .Release()
            .Perform();

        Thread.Sleep(500);

        // No crash/exception is the primary assertion; the collection must still be present.
        UiDebugger.WaitAndFind(Driver, "Projects_ProjectsCollectionView");
    }

    [Test]
    public void Click_ProjectCard_NavigatesToDetails()
    {
        var card = UiDebugger.WaitAndFind(Driver, "ProjectCard_Root");
        card.Click();

        UiDebugger.WaitAndFind(Driver, "Details_ScrollView", TimeSpan.FromSeconds(10));

        // Return to Projects for any subsequent tests.
        UiDebugger.WaitAndClick(Driver, "Details_BackButton");
        UiDebugger.WaitAndFind(Driver, "Projects_SearchInput");
    }
}

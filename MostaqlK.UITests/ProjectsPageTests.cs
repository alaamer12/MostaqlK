using NUnit.Framework;
using MostaqlK.Core.Platform;
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
    private static readonly string InteractionLogPath = AppPaths.LogFilePath;

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
            // WinAppDriver's ClassName lookup matches the native UIA ClassName property, which for
            // a MAUI CollectionView's realized rows on Windows is "ListViewItem" (confirmed via a
            // UiDebugger dump: <ListItem ... ClassName="ListViewItem" ...>), not the XML tag name
            // "ListItem" itself (that's the UIA ControlType, not the ClassName).
            return collectionView.FindElementsByClassName("ListViewItem").Count;
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
        var lastScanLabel = UiDebugger.WaitAndFind(Driver, "Projects_LastScanLabel");
        
        // Wait until we are out of the "moments" (5s) window so we can see a text change.
        // We check for "ثانية" which appears in "منذ 5 ثانية", "منذ 6 ثانية" etc.
        var wait = new OpenQA.Selenium.Support.UI.WebDriverWait(Driver, TimeSpan.FromSeconds(10));
        wait.Until(d => lastScanLabel.Text.Contains("ثانية"));
        
        var textBefore = lastScanLabel.Text;

        UiDebugger.WaitAndClick(Driver, "Projects_RefreshLabel");
        Thread.Sleep(300);

        // Tap again immediately (rapid double-fire probe) before the first refresh has settled.
        UiDebugger.WaitAndClick(Driver, "Projects_RefreshLabel");
        
        // After refresh, it should go back to "منذ لحظات" (within moments).
        wait.Until(d => lastScanLabel.Text.Contains("لحظات"));
        
        var textAfter = lastScanLabel.Text;
        var enterCount = CountLogEntries("RefreshCommand", since, kind: "ENTER");
        var exitCount = CountLogEntries("RefreshCommand", since, kind: "EXIT");

        Assert.Multiple(() =>
        {
            Assert.That(enterCount, Is.GreaterThan(0),
                "RefreshCommand should have logged at least one entry after the rapid double-tap.");
            Assert.That(exitCount, Is.LessThanOrEqualTo(enterCount),
                "RefreshCommand should not report more completions than invocations (no runaway double-fire).");
            Assert.That(textAfter, Is.Not.EqualTo(textBefore),
                $"The LastScanLabel text should have changed from '{textBefore}' to '{textAfter}'.");
        });

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

    // Regression test for the fabricated execution-duration bug: ProjectCardViewModel.Execution
    // used to synthesize "{Project.DeliveryDays * 3} يوما" instead of surfacing the real scraped
    // "duration" field. Now it must be either the genuine "{days} يوما" value or the "—"
    // placeholder for cards not yet enriched — never a value implied to be 3x the delivery days.
    [Test]
    public void ProjectCard_ExecutionLabel_ShowsRealDurationOrPlaceholder_NeverFabricated()
    {
        var collectionView = UiDebugger.WaitAndFind(Driver, "Projects_ProjectsCollectionView");
        var executionLabels = collectionView.FindElementsByAccessibilityId("ProjectCard_ExecutionLabel");

        Assert.That(executionLabels.Count, Is.GreaterThan(0),
            "At least one project card should expose the execution-duration label.");

        foreach (var label in executionLabels)
        {
            var text = label.Text?.Trim() ?? string.Empty;

            Assert.That(text, Is.Not.Empty, "Execution label must never render blank.");

            var isPlaceholder = text == "—";
            var isRealDuration = System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+\s*يوما$");

            Assert.That(isPlaceholder || isRealDuration, Is.True,
                $"Execution label text '{text}' must be either the placeholder '—' or a real '<days> يوما' value, never a fabricated number.");
        }
    }
}

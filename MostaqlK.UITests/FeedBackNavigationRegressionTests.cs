using MostaqlK.UITests.Utils;
using NUnit.Framework;
using OpenQA.Selenium.Appium.Windows;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace MostaqlK.UITests;

/// <summary>
/// Regression harness for the owner-reported feed bug (2026-11-01): open a source → open a project →
/// press back, and the projects list loses its cards (first reported as "only one project left",
/// later as "all projects gone"), with the same flow ending in a WinUI fail-fast
/// (<c>0xC000027B</c> in <c>Microsoft.UI.Xaml.dll</c>) when the feed is scrolled afterwards.
/// <para>
/// The assertions deliberately measure what is <b>on screen</b> (<c>ListViewItem</c> containers that
/// the Windows virtualizer actually realized), not what the view-model believes it holds: the
/// <c>FeedDiag.*</c> marks captured on the owner's machine proved the data path keeps all 44 rows
/// while the native list realizes 1-2 of them. Counting the collection's items would have passed.
/// </para>
/// </summary>
[TestFixture]
public class FeedBackNavigationRegressionTests
{
    private static WindowsDriver<WindowsElement> Driver => AppiumSetup.Driver!;

    /// <summary>Cards the Windows list has actually realized (see <c>ProjectsPageTests.CountVisibleCards</c>).</summary>
    private static int CountVisibleCards()
    {
        try
        {
            var collectionView = UiDebugger.WaitAndFind(Driver, "Projects_ProjectsCollectionView", TimeSpan.FromSeconds(8));
            return collectionView.FindElementsByClassName("ListViewItem").Count;
        }
        catch (OpenQA.Selenium.NoSuchElementException)
        {
            return 0;
        }
    }

    private static bool IsFeedEmptyStateShown()
    {
        try
        {
            return Driver.FindElementsByAccessibilityId("Projects_EmptyStateLabel").Count > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Echoes the feed's own diagnostics so a red test is self-explaining: the store/VM marks show
    /// whether the rows collapsed in the data (<c>FeedDiag.Apply</c> paintable) or in the renderer
    /// (<c>FeedDiag.NativeList</c> realized, and the <c>FeedDiag.Collapse</c> verdict).
    /// </summary>
    private static void DumpFeedDiag(DateTimeOffset since)
    {
        try
        {
            var path = MostaqlK.Core.Platform.AppPaths.LogFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            var lines = File.ReadAllLines(path)
                .Where(line => line.Contains("FeedDiag.") && DateTimeOffset.TryParse(line.Split(" | ")[0], out var ts) && ts >= since)
                .TakeLast(40);

            foreach (var line in lines)
            {
                TestContext.WriteLine(line.Length > 260 ? line[..260] : line);
            }
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"(could not read the interaction log: {ex.GetType().Name})");
        }
    }

    /// <summary>Open the first card, come back with the details page's back button, return the card count after.</summary>
    private static int OpenFirstCardAndGoBack()
    {
        UiDebugger.WaitAndFind(Driver, "ProjectCard_Root", TimeSpan.FromSeconds(15)).Click();
        UiDebugger.WaitAndFind(Driver, "Details_ScrollView", TimeSpan.FromSeconds(15));
        Thread.Sleep(400);

        UiDebugger.WaitAndClick(Driver, "Details_BackButton");
        UiDebugger.WaitAndFind(Driver, "Projects_SearchInput", TimeSpan.FromSeconds(15));

        // Let the post-return batches and the virtualizer's realization pass settle: the reported
        // symptom landed seconds after the apply, not on the first frame.
        Thread.Sleep(2500);
        return CountVisibleCards();
    }

    [Test]
    public void BackFromDetails_FeedKeepsItsCards()
    {
        var startedAt = DateTimeOffset.Now;
        var before = CountVisibleCards();
        Assert.That(before, Is.GreaterThanOrEqualTo(2),
            "Precondition: the feed should show several project cards before any navigation.");

        var after = OpenFirstCardAndGoBack();

        if (after < 2)
        {
            DumpFeedDiag(startedAt);
        }

        Assert.That(IsFeedEmptyStateShown(), Is.False,
            "Returning from a project must not drop the feed into its empty state.");
        Assert.That(after, Is.GreaterThanOrEqualTo(2),
            $"REGRESSION: back from project details collapsed the feed to {after} card(s) "
            + $"(it held {before} before opening the project).");
    }

    [Test]
    public void SwitchSource_ThenBackFromDetails_FeedKeepsItsCards()
    {
        var startedAt = DateTimeOffset.Now;

        // The owner's second reproduction: open one source, morph to the other, open a project,
        // press back. The morph is what makes the peer source's snapshot warm, so it is the path
        // where the overlay + dim + fresh batches all land inside one second.
        var other = Driver.FindElementsByAccessibilityId("Sidebar_SourceTile_nafezly").Count > 0
            ? "Sidebar_SourceTile_nafezly"
            : "Sidebar_SourceTile_mostaql";

        Driver.FindElementByAccessibilityId(other).Click();
        Thread.Sleep(2000); // the radial reveal + the settle pass it hands off to

        var before = CountVisibleCards();
        Assert.That(before, Is.GreaterThanOrEqualTo(2),
            $"Precondition: after morphing to {other} the feed should show several cards (saw {before}).");

        var after = OpenFirstCardAndGoBack();

        if (after < 2)
        {
            DumpFeedDiag(startedAt);
        }

        Assert.That(after, Is.GreaterThanOrEqualTo(2),
            $"REGRESSION: after a source morph, back from project details collapsed the feed to "
            + $"{after} card(s) (it held {before}).");
    }

    [Test]
    public void ScrollFeed_AfterBackFromDetails_DoesNotTakeTheProcess()
    {
        var startedAt = DateTimeOffset.Now;
        CountVisibleCards();
        OpenFirstCardAndGoBack();

        // The crash the owner hit was on the scroll after the return: a WinUI fail-fast at the same
        // instruction in every dump. "The session is still alive" IS the assertion.
        var collectionView = UiDebugger.WaitAndFind(Driver, "Projects_ProjectsCollectionView", TimeSpan.FromSeconds(10));
        for (int i = 0; i < 3; i++)
        {
            new OpenQA.Selenium.Interactions.Actions(Driver)
                .MoveToElement(collectionView, collectionView.Size.Width / 2, (int)(collectionView.Size.Height * 0.8))
                .ClickAndHold()
                .MoveByOffset(0, -(int)(collectionView.Size.Height * 0.6))
                .Release()
                .Perform();
            Thread.Sleep(600);
        }

        var stillAlive = false;
        try
        {
            stillAlive = Driver.FindElementByAccessibilityId("Projects_ProjectsCollectionView") is not null;
        }
        catch (Exception)
        {
            stillAlive = false;
        }

        if (!stillAlive)
        {
            DumpFeedDiag(startedAt);
        }

        Assert.That(stillAlive, Is.True,
            "REGRESSION: scrolling the feed after returning from a project killed the app "
            + "(WinUI fail-fast); see the dumps in %LocalAppData%\\CrashDumps.");
    }
}

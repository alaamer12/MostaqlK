using Microsoft.Data.Sqlite;
using NUnit.Framework;
using MostaqlK.UITests.Utils;
using OpenQA.Selenium.Appium.Windows;
using System;
using System.IO;
using System.Threading;

namespace MostaqlK.UITests;

/// <summary>
/// Step 8 of <c>.junie/plans/appium-ui-test-catalog-and-fixes.md</c>: proves dynamic-looking
/// surfaces on the Projects page (<c>MainWindowPage.xaml</c>) genuinely reflect live DB/pipeline
/// state rather than a stale/hardcoded value, by comparing what's on screen against a direct
/// query against the same SQLite store the app itself reads (<c>IProjectRepository</c>/
/// <c>FtsQueryService</c>), constructed the same way <c>ProjectFeedViewModel</c> does.
/// </summary>
[TestFixture]
public class DataSyncTests
{
    // Same on-disk location MostaqlK.csproj's SqliteConnectionFactory resolves via
    // FileSystem.AppDataDirectory for this unpackaged app id (confirmed against
    // ProjectsPageTests' identical resolution for interaction-log.txt in the same folder).
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "User Name", "com.companyname.mostaqlk", "Data", "mostaqlk.db");

    private static WindowsDriver<WindowsElement> Driver => AppiumSetup.Driver!;

    private static readonly TimeSpan NavigationSettleDelay = TimeSpan.FromMilliseconds(2000);

    private static SqliteConnection OpenDb()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString());
        connection.Open();
        return connection;
    }

    private static void EnsureProjectsPage()
    {
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

    private static void EnsureSettingsPage()
    {
        try
        {
            UiDebugger.WaitAndFind(Driver, "Settings_SaveButton", TimeSpan.FromSeconds(3));
            return;
        }
        catch (OpenQA.Selenium.NoSuchElementException)
        {
            UiDebugger.WaitAndClick(Driver, "Sidebar_SettingsButton");
            UiDebugger.WaitAndFind(Driver, "Settings_SaveButton");
            Thread.Sleep(NavigationSettleDelay);
        }
    }

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

    // Mirrors FtsQueryService.SearchAsync's FTS5 MATCH query exactly, so the assertion checks the
    // real pipeline query result count, not a hand-rolled substring count of a different shape.
    private static int CountFtsMatches(string query)
    {
        using var connection = OpenDb();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM projects_fts f
            JOIN projects p ON p.project_id = f.project_id
            WHERE f.projects_fts MATCH @query;
            """;
        command.Parameters.AddWithValue("@query", query);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static (int Tracked, int Unread) CountTracked()
    {
        using var connection = OpenDb();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), COALESCE(SUM(is_unread), 0) FROM projects;";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static int CountAddedToday()
    {
        using var connection = OpenDb();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM projects WHERE date(discovered_at) = date('now');";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        EnsureProjectsPage();
        UiDebugger.WaitAndFind(Driver, "ProjectCard_Root", TimeSpan.FromSeconds(30));
    }

    [SetUp]
    public void SetUp() => EnsureProjectsPage();

    [Test]
    public void Type_SearchInput_Enter_VisibleCardCount_ExactlyMatchesLiveFtsQuery()
    {
        // ASCII term (real project titles/descriptions routinely embed English tech terms, e.g.
        // "CSS Text Shadows") deliberately chosen over an Arabic term: WinAppDriver's SendKeys into
        // the RTL SearchInputField cannot be trusted to faithfully reproduce Arabic code points, so
        // an Arabic query here would test SendKeys fidelity rather than the DB/pipeline sync itself.
        const string term = "CSS";

        var expected = CountFtsMatches(term);

        using (var diagConn = OpenDb())
        using (var diagCmd = diagConn.CreateCommand())
        {
            diagCmd.CommandText = """
                SELECT p.project_id, p.title
                FROM projects_fts f
                JOIN projects p ON p.project_id = f.project_id
                WHERE f.projects_fts MATCH @query;
                """;
            diagCmd.Parameters.AddWithValue("@query", term);
            using var diagReader = diagCmd.ExecuteReader();
            while (diagReader.Read())
            {
                TestContext.WriteLine($"[DataSyncTests] DB match for '{term}': id={diagReader.GetInt64(0)} title='{diagReader.GetString(1)}'");
            }
        }

        var search = UiDebugger.WaitAndFind(Driver, "Projects_SearchInput");
        try
        {
            search.Clear();
            search.SendKeys(term);
            search.SendKeys(OpenQA.Selenium.Keys.Enter);
            Thread.Sleep(2500);

            TestContext.WriteLine($"[DataSyncTests] SearchInput.Text after typing '{term}': '{search.Text}'");

            var actual = CountVisibleCards();

            // A transient SQLite lock/contention from the live background IPollService writer can
            // surface as a one-off HasError state; retry once via the Retry button before treating
            // a zero-count result as a genuine data-sync bug.
            if (actual != expected)
            {
                try
                {
                    var retryButton = UiDebugger.WaitAndFind(Driver, "Projects_RetryButton", TimeSpan.FromSeconds(2));
                    TestContext.WriteLine("[DataSyncTests] HasError state detected after search; retrying once.");
                    retryButton.Click();
                    Thread.Sleep(2000);
                    actual = CountVisibleCards();
                }
                catch (OpenQA.Selenium.NoSuchElementException)
                {
                    // No error state present — the mismatch is not a transient retry-fixable one.
                }
            }

            Assert.That(actual, Is.EqualTo(expected),
                $"Visible card count after searching '{term}' ({actual}) should exactly match the live " +
                $"FtsQueryService.SearchAsync-equivalent DB query ({expected}), not just differ from the unfiltered count.");
        }
        finally
        {
            // Restore full feed for subsequent tests, even if the assertion above failed.
            search.Clear();
            search.SendKeys(OpenQA.Selenium.Keys.Enter);
            Thread.Sleep(1500);
        }
    }

    [Test]
    public void Type_SearchInput_Enter_NoMatch_VisibleCardCount_ExactlyMatchesLiveFtsQuery()
    {
        const string term = "zzz_no_such_project_should_match_zzz";

        var expected = CountFtsMatches(term);
        Assert.That(expected, Is.EqualTo(0), "Precondition: this nonsense term should match zero rows in the live DB.");

        var search = UiDebugger.WaitAndFind(Driver, "Projects_SearchInput");
        search.Clear();
        search.SendKeys(term);
        search.SendKeys(OpenQA.Selenium.Keys.Enter);
        Thread.Sleep(1500);

        var actual = CountVisibleCards();
        Assert.That(actual, Is.EqualTo(expected), "Visible card count should exactly match the live DB query result (0), not a stale prior count.");

        search.Clear();
        search.SendKeys(OpenQA.Selenium.Keys.Enter);
        Thread.Sleep(1500);
    }

    [Test]
    public void FooterTrackedAndUnreadCounts_MatchLiveDbCounts()
    {
        // The background IPollService loop keeps inserting newly-discovered rows for real while
        // this test runs, so force a fresh LoadAsync via the Refresh command immediately before
        // taking the DB snapshot — otherwise an un-refreshed footer and a DB queried moments later
        // can legitimately disagree without any actual data-sync bug.
        UiDebugger.WaitAndClick(Driver, "Projects_RefreshLabel");
        Thread.Sleep(1000);

        var (tracked, unread) = CountTracked();

        // A plain contains(@Name, ...) text match is ambiguous here: every rendered ProjectCard
        // also carries its own static "غير مقروء" unread-dot label (ProjectCard.xaml), and
        // WinAppDriver's XPath engine doesn't reliably support parent::/preceding-sibling:: axes
        // for a relative lookup either — so these two footer labels now carry their own stable
        // AutomationIds (Projects_TrackedCountLabel/Projects_UnreadCountLabel) for exactly this.
        var trackedText = UiDebugger.WaitAndFind(Driver, "Projects_TrackedCountLabel").Text;
        var unreadText = UiDebugger.WaitAndFind(Driver, "Projects_UnreadCountLabel").Text;

        Assert.That(trackedText, Is.EqualTo($"{tracked} مشروع متتبَّع"),
            "Footer 'tracked' count should exactly match COUNT(*) FROM projects in the live DB.");
        Assert.That(unreadText, Is.EqualTo($"{unread} غير مقروء"),
            "Footer 'unread' count should exactly match SUM(is_unread) FROM projects in the live DB.");
    }

    [Test]
    public void ProjectsAddedTodayStat_MatchesLiveDbCount()
    {
        var expected = CountAddedToday();

        // No dedicated AutomationId on the sidebar's stat value (per docs/ui-test-catalog.md); it
        // sits directly below the fixed Arabic caption label, so locate it via that stable caption.
        var captionLabel = Driver.FindElementByXPath("//*[@Name='مشاريع مضافة اليوم']");
        var statValueLabel = Driver.FindElementByXPath("//*[@Name='مشاريع مضافة اليوم']/following-sibling::*[1]");

        Assert.That(captionLabel, Is.Not.Null);
        Assert.That(statValueLabel.Text, Is.EqualTo(expected.ToString()),
            "Sidebar 'مشاريع مضافة اليوم' stat should exactly match COUNT(*) WHERE date(discovered_at) = date('now') in the live DB.");
    }

    [Test]
    public void PollIntervalText_ReflectsLiveConfiguredValue_NotAHardcodedLiteral()
    {
        // Cross-page consistency check: the header pill's number must track whatever the
        // Settings page's poll-interval input (bound to the same IPollService/Preferences value)
        // currently holds, proving it's a live read and not a literal baked into XAML.
        EnsureSettingsPage();
        var pollInput = UiDebugger.WaitAndFind(Driver, "Settings_PollIntervalInput");
        pollInput.Clear();
        pollInput.SendKeys("77");
        pollInput.SendKeys(OpenQA.Selenium.Keys.Tab);
        Thread.Sleep(300);
        UiDebugger.WaitAndClick(Driver, "Settings_SaveButton");
        Thread.Sleep(500);

        UiDebugger.WaitAndClick(Driver, "Sidebar_ProjectsButton");
        UiDebugger.WaitAndFind(Driver, "Projects_SearchInput");
        Thread.Sleep(NavigationSettleDelay);

        var pollIntervalLabel = Driver.FindElementByXPath("//*[contains(@Name, 'يتم الفحص كل')]");
        Assert.That(pollIntervalLabel.Text, Does.Contain("77"),
            "The Projects page header's poll-interval text should reflect the value just saved on the Settings page, " +
            "proving it reads the live configured value rather than a hardcoded string literal.");

        // Restore a sane default for subsequent tests.
        EnsureSettingsPage();
        var restoreInput = UiDebugger.WaitAndFind(Driver, "Settings_PollIntervalInput");
        restoreInput.Clear();
        restoreInput.SendKeys("30");
        restoreInput.SendKeys(OpenQA.Selenium.Keys.Tab);
        Thread.Sleep(300);
        UiDebugger.WaitAndClick(Driver, "Settings_SaveButton");
        Thread.Sleep(500);
        UiDebugger.WaitAndClick(Driver, "Sidebar_ProjectsButton");
        UiDebugger.WaitAndFind(Driver, "Projects_SearchInput");
        Thread.Sleep(NavigationSettleDelay);
    }
}

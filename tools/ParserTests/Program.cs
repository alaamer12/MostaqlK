using System.Text;
using HtmlAgilityPack;
using MostaqlK.Core.Formatting;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Infrastructure.Http.Parsers;
using MostaqlK.Models;

namespace ParserTests;

/// <summary>
/// Focused, self-contained test harness for the Mostaql HTML parsers - the C# counterpart of
/// the Python prototype's <c>test_analyzer.py</c>
/// (.repertoire/progress/python/parser/scratch/test_analyzer.py), and deliberately stricter
/// than it: where the Python tests only asserted "the field was found", these assert the
/// exact parsed values, and they run the same project data through four fixtures - the
/// current markup, a fully renamed redesign, the Python prototype's adversarial page, and a
/// listing page - so a regression in ANY single extraction strategy fails the run.
///
/// Run:      dotnet run --project tools\ParserTests
/// Live:     dotnet run --project tools\ParserTests -- --live https://mostaql.com/project/1268113-...
/// Exits non-zero if any check fails, so it can be wired into CI or scripts\test.ps1.
/// </summary>
public static class Program
{
    private static readonly List<string> Passed = [];
    private static readonly List<(string Name, string Detail)> Failed = [];

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length >= 2 && args[0] == "--live")
        {
            // Optional 3rd arg: path to a cookies file (Netscape or "name=value" per line).
            // Defaults to the repo-root cookies.txt when present, so a logged-in fetch is the
            // norm rather than the exception - anonymous fetches hide attachment URLs behind
            // a /register stub and cannot be used to verify the download path at all.
            var cookiePath = args.Length >= 3 ? args[2] : null;
            return RunLive(args[1], cookiePath).GetAwaiter().GetResult();
        }

        Console.WriteLine("Running MostaqlK parser tests...\n");

        Run(nameof(TestCurrentMarkupHappyPath), TestCurrentMarkupHappyPath);
        Run(nameof(TestRenamedMarkupSurvivesFullRedesign), TestRenamedMarkupSurvivesFullRedesign);
        Run(nameof(TestBothFixturesAgreeOnTheSameProject), TestBothFixturesAgreeOnTheSameProject);
        Run(nameof(TestAdversarialPageStillParses), TestAdversarialPageStillParses);
        Run(nameof(TestListingPageIdExtraction), TestListingPageIdExtraction);
        Run(nameof(TestNormalizationPrimitives), TestNormalizationPrimitives);
        Run(nameof(TestProposalCountForms), TestProposalCountForms);
        Run(nameof(TestDegenerateInputs), TestDegenerateInputs);
        Run(nameof(TestCookieJarParsing), TestCookieJarParsing);

        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"TOTAL: {Passed.Count} passed, {Failed.Count} failed");
        if (Failed.Count == 0)
        {
            Console.WriteLine("All checks passed.");
            return 0;
        }

        Console.WriteLine("\nFailed checks:");
        foreach (var (name, detail) in Failed)
        {
            Console.WriteLine($"  - {name}: {detail}");
        }
        return 1;
    }

    // -----------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------

    /// <summary>The structural (class/id) fast path against today's real Mostaql markup.</summary>
    private static void TestCurrentMarkupHappyPath()
    {
        var details = DetailParser.Parse(1268113, LoadFixture("project_current_markup.html"));

        Check("title parsed from h1", details.Title == "مصمم مبدع ومحترف في Canva", details.Title);
        Check("budget parsed", details.Budget == "$50.00 - $100.00", details.Budget);
        Check("delivery days parsed", details.DeliveryDays == 10, $"{details.DeliveryDays}");
        Check("owner name parsed", details.Owner.Name == "Abdulrhman S.", details.Owner.Name);
        Check("hire rate 36.36% rounds to 36", details.Owner.HiringRatePercent == 36, $"{details.Owner.HiringRatePercent}");
        Check("all three skills parsed", details.Skills.Count == 3, string.Join("/", details.Skills.Select(s => s.Name)));

        // The description-newlines regression: paragraph structure must survive.
        Check("description keeps paragraph breaks", details.Description.Contains('\n'), Preview(details.Description));
        Check("description keeps the 'المهام:' section header on its own line",
            details.Description.Contains("\nالمهام:\n", StringComparison.Ordinal), Preview(details.Description));
        Check("description is not a run-on single line",
            details.Description.Split('\n').Length >= 5, $"{details.Description.Split('\n').Length} lines");

        var asset = details.Attachments.FirstOrDefault();
        Check("attachment discovered", asset is not null, $"{details.Attachments.Count} attachments");
        Check("attachment extension resolved", asset?.Extension == "docx", asset?.Extension);
        Check("anonymous /register attachment link flagged as requiring auth",
            asset?.RequiresAuth == true && string.IsNullOrEmpty(asset.Url), asset?.RawUrl);

        Check("every meta field resolved structurally (no inference needed)",
            details.FieldProvenance.Where(kv => kv.Key is "project_status" or "budget" or "duration")
                .All(kv => kv.Value.Source == "structural"),
            string.Join(", ", details.FieldProvenance.Select(kv => $"{kv.Key}={kv.Value.Source}")));
    }

    /// <summary>
    /// The core robustness claim: identical data, zero recognizable class/id names, labels
    /// with colons and spelling variants, values in Arabic-Indic numerals.
    /// </summary>
    private static void TestRenamedMarkupSurvivesFullRedesign()
    {
        var details = DetailParser.Parse(1268113, LoadFixture("project_renamed_markup.html"));

        Check("title recovered without any h1", details.Title == "مصمم مبدع ومحترف في Canva", details.Title);
        Check("budget recovered via label-driven walk", details.Budget == "$50.00 - $100.00", details.Budget);
        Check("Arabic-Indic '١٠ أيام' parsed as 10 days", details.DeliveryDays == 10, $"{details.DeliveryDays}");
        Check("owner name recovered from the /u/ profile link", details.Owner.Name == "Abdulrhman S.", details.Owner.Name);
        Check("hire rate recovered from a renamed stat list", details.Owner.HiringRatePercent == 36, $"{details.Owner.HiringRatePercent}");
        Check("skills recovered by href shape, pagination link excluded",
            details.Skills.Count == 3 && details.Skills.All(s => s.Url?.Contains("/skills/") == true),
            string.Join("/", details.Skills.Select(s => s.Name)));
        Check("description recovered without .text-wrapper-div",
            details.Description.Contains("المهام:", StringComparison.Ordinal), Preview(details.Description));
        Check("recovered description still keeps its line breaks", details.Description.Contains('\n'), Preview(details.Description));
        Check("attachment recovered with no data-file-type attribute",
            details.Attachments.FirstOrDefault()?.Extension == "docx",
            details.Attachments.FirstOrDefault()?.Extension);
    }

    /// <summary>
    /// Cross-fixture invariant: a redesign must not change WHAT we extract, only HOW. Any
    /// divergence between the two fixtures is a robustness hole regardless of which one is
    /// "correct", so assert them against each other rather than only against constants.
    /// </summary>
    private static void TestBothFixturesAgreeOnTheSameProject()
    {
        var current = DetailParser.Parse(1268113, LoadFixture("project_current_markup.html"));
        var renamed = DetailParser.Parse(1268113, LoadFixture("project_renamed_markup.html"));

        Check("title identical across markups", current.Title == renamed.Title, $"{current.Title} vs {renamed.Title}");
        Check("budget identical across markups", current.Budget == renamed.Budget, $"{current.Budget} vs {renamed.Budget}");
        Check("delivery days identical across markups", current.DeliveryDays == renamed.DeliveryDays,
            $"{current.DeliveryDays} vs {renamed.DeliveryDays}");
        Check("owner name identical across markups", current.Owner.Name == renamed.Owner.Name,
            $"{current.Owner.Name} vs {renamed.Owner.Name}");
        Check("hire rate identical across markups", current.Owner.HiringRatePercent == renamed.Owner.HiringRatePercent,
            $"{current.Owner.HiringRatePercent} vs {renamed.Owner.HiringRatePercent}");
        Check("skill names identical across markups",
            current.Skills.Select(s => s.Name).SequenceEqual(renamed.Skills.Select(s => s.Name)),
            string.Join("/", current.Skills.Select(s => s.Name)) + " vs " + string.Join("/", renamed.Skills.Select(s => s.Name)));
    }

    /// <summary>
    /// The Python prototype's adversarial fixture (synonym labels, words split across spans,
    /// deeply nested wrappers, reordered DOM, decoy numbers) - here it must reach the
    /// inference engine rather than throwing, and must not invent completed-only fields.
    /// </summary>
    private static void TestAdversarialPageStillParses()
    {
        var html = LoadFixture("project_adversarial_redesign.html");

        ProjectDetails? details = null;
        try
        {
            details = DetailParser.Parse(1, html);
        }
        catch (Exception ex)
        {
            Check("adversarial page does not throw", false, ex.Message);
            return;
        }

        Check("adversarial page does not throw", true);
        Check("title falls back to <title> when the page has no h1",
            details.Title == "مشروع جديد بتصميم مختلف تماما", details.Title);

        var duration = details.FieldProvenance.GetValueOrDefault("duration")?.Value;
        Check("duration inferred from split characters + 'يوما' unit",
            duration is not null && duration.Contains("11", StringComparison.Ordinal), duration);

        var hireRate = details.FieldProvenance.GetValueOrDefault("hire_rate")?.Value;
        Check("hire rate inferred from the '%'-adjacent 30, not the contextless one",
            hireRate is not null && hireRate.Contains("30", StringComparison.Ordinal), hireRate);

        Check("hire rate percent value parses to 30", details.Owner.HiringRatePercent == 30,
            $"{details.Owner.HiringRatePercent}");

        // The page's status is not "مكتمل", so the completed-only fields must stay null even
        // though plausible-looking numbers exist nearby.
        foreach (var field in new[] { "started_since", "deal_date", "delivery_date" })
        {
            Check($"completed-only field '{field}' stays null on a non-completed project",
                details.FieldProvenance.GetValueOrDefault(field)?.Value is null,
                details.FieldProvenance.GetValueOrDefault(field)?.Value);
        }
    }

    private static void TestListingPageIdExtraction()
    {
        var summaries = ListingParser.Parse(LoadFixture("projects_list.html"));

        Check("all three listing rows parsed", summaries.Count == 3, $"{summaries.Count}");
        Check("plain project id extracted", summaries.ElementAtOrDefault(0)?.ProjectId == 1268113,
            $"{summaries.ElementAtOrDefault(0)?.ProjectId}");
        Check("numeric slug suffix does not hijack the id", summaries.ElementAtOrDefault(1)?.ProjectId == 999001,
            $"{summaries.ElementAtOrDefault(1)?.ProjectId}");
        Check("absolute url with trailing slash parsed", summaries.ElementAtOrDefault(2)?.ProjectId == 555222,
            $"{summaries.ElementAtOrDefault(2)?.ProjectId}");
        Check("listing title parsed", summaries.ElementAtOrDefault(0)?.Title == "مصمم مبدع ومحترف في Canva",
            summaries.ElementAtOrDefault(0)?.Title);
    }

    private static void TestNormalizationPrimitives()
    {
        Check("ToAsciiDigits converts Arabic-Indic digits",
            StructuralExtractor.ToAsciiDigits("١٥ يوما") == "15 يوما",
            StructuralExtractor.ToAsciiDigits("١٥ يوما"));
        Check("ToAsciiDigits converts extended/Persian digits",
            StructuralExtractor.ToAsciiDigits("۲۰۲۴") == "2024",
            StructuralExtractor.ToAsciiDigits("۲۰۲۴"));

        var canonical = StructuralExtractor.NormalizeLabel("الميزانية");
        foreach (var variant in new[] { "الميزانية:", " الميزانية ", "الميزانيه", "الميزانية ", "الميزانيّة:" })
        {
            Check($"NormalizeLabel folds '{variant}' onto the canonical label",
                StructuralExtractor.NormalizeLabel(variant) == canonical,
                StructuralExtractor.NormalizeLabel(variant));
        }

        Check("NormalizeLabel keeps genuinely different labels distinct",
            StructuralExtractor.NormalizeLabel("الميزانية") != StructuralExtractor.NormalizeLabel("مدة التنفيذ"));

        var doc = new HtmlDocument();
        doc.LoadHtml("<div><p>سطر أول</p><p>سطر ثان<br>سطر ثالث</p></div>");
        var multiline = StructuralExtractor.NormalizeMultiline(doc.DocumentNode.SelectSingleNode("//div"));
        Check("NormalizeMultiline turns block boundaries into real line breaks",
            multiline == "سطر أول\nسطر ثان\nسطر ثالث", multiline.Replace("\n", "\\n"));
    }

    private static void TestProposalCountForms()
    {
        var cases = new Dictionary<string, int>
        {
            ["عرض واحد"] = 1,
            ["عرضان"] = 2,
            ["عرضين"] = 2,
            ["2 عرض"] = 2,
            ["2 عروض"] = 2,
            ["٣ عروض"] = 3,
            ["أضف أول عرض"] = 0,
        };

        foreach (var (text, expected) in cases)
        {
            var parsed = ArabicProposalParser.Parse(text);
            Check($"proposal '{text}' numeric value", parsed.Number == expected, $"{parsed.Number}");
            Check($"proposal '{text}' display text preserved", parsed.Text == text, parsed.Text);
        }
    }

    /// <summary>Degenerate inputs must fail loudly and predictably, never silently.</summary>
    private static void TestDegenerateInputs()
    {
        Check("empty html throws ParseException", Throws(() => DetailParser.Parse(1, string.Empty)));
        Check("whitespace-only html throws ParseException", Throws(() => DetailParser.Parse(1, "   ")));
        Check("a page with no title at all throws ParseException",
            Throws(() => DetailParser.Parse(1, "<html><body><div>لا شيء</div></body></html>")));
        Check("a non-listing page throws rather than returning an empty feed",
            Throws(() => ListingParser.Parse("<html><body><p>لا مشاريع</p></body></html>")));
    }

    /// <summary>
    /// The cookie header is what turns an attachment from a "/register" stub into a real,
    /// downloadable /file/... URL, so its parsing is load-bearing and must survive both export
    /// formats. Uses no real session values.
    /// </summary>
    private static void TestCookieJarParsing()
    {
        var devTools = CookieJar.ParseFile("mostaqlweb=\"abc%3D\"\nXSRF-TOKEN=\"def\"\n_ga=\"noise\"");
        Check("quoted name=value lines parse and unquote", devTools == "mostaqlweb=abc%3D; XSRF-TOKEN=def", devTools);
        Check("analytics cookies are dropped", devTools?.Contains("_ga") == false, devTools);

        var oneLine = CookieJar.ParseFile("a=1; b=2; c=3");
        Check("a single copied Cookie header line parses", oneLine == "a=1; b=2; c=3", oneLine);

        var netscape = CookieJar.ParseFile(
            "# Netscape HTTP Cookie File\n" +
            ".mostaql.com\tTRUE\t/\tTRUE\t1799999999\tmostaqlweb\tabc%3D\n" +
            ".mostaql.com\tTRUE\t/\tTRUE\t1799999999\tXSRF-TOKEN\tdef\n");
        Check("Netscape/curl 7-column export parses", netscape == "mostaqlweb=abc%3D; XSRF-TOKEN=def", netscape);
        Check("comment lines are ignored", netscape?.Contains('#') == false, netscape);

        var duplicated = CookieJar.ParseFile("mostaqlweb=old\nmostaqlweb=new");
        Check("a re-login duplicate keeps the last value only", duplicated == "mostaqlweb=new", duplicated);

        Check("empty input yields null rather than an empty header", CookieJar.ParseFile("   ") is null);
        Check("a file with only analytics yields null", CookieJar.ParseFile("_gid=x\n_ga=y") is null);
    }

    // -----------------------------------------------------------------
    // Live mode - parse a real URL end-to-end (opt-in, needs network)
    // -----------------------------------------------------------------

    private static async Task<int> RunLive(string url, string? cookiePath)
    {
        Console.WriteLine($"Live parse: {url}\n");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", LiveUserAgent);

        var cookieHeader = CookieJar.Load(cookiePath);
        if (cookieHeader is not null)
        {
            Console.WriteLine($"Cookie:       loaded ({CookieJar.LastSource}, {cookieHeader.Split(';').Length} cookies)");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieHeader);
        }
        else
        {
            Console.WriteLine("Cookie:       none (anonymous fetch - attachment URLs will be /register stubs)");
        }

        var html = await http.GetStringAsync(url);

        var id = System.Text.RegularExpressions.Regex.Match(url, @"/project/(\d+)");
        var details = DetailParser.Parse(id.Success ? long.Parse(id.Groups[1].Value) : 0, html);

        Console.WriteLine($"Title:        {details.Title}");
        Console.WriteLine($"Budget:       {details.Budget}");
        Console.WriteLine($"DeliveryDays: {details.DeliveryDays}");
        Console.WriteLine($"Owner:        '{details.Owner.Name}' hire={details.Owner.HiringRatePercent}% open/inprogress={details.Owner.CompletedProjectsCount}");
        Console.WriteLine($"Skills:       {string.Join(", ", details.Skills.Select(s => s.Name))}");
        Console.WriteLine($"Attachments:  {details.Attachments.Count}");
        foreach (var a in details.Attachments)
        {
            Console.WriteLine($"  - '{a.FileName}' ext={a.Extension} size={a.SizeText} requiresAuth={a.RequiresAuth}");
            Console.WriteLine($"    url={a.RawUrl}");
        }

        if (details.Attachments.Count > 0)
        {
            await DownloadAttachmentsAsync(http, url, details, cookieHeader);
        }
        Console.WriteLine($"Description ({details.Description.Length} chars, {details.Description.Split('\n').Length} lines):");
        Console.WriteLine(Preview(details.Description, 400));
        Console.WriteLine("\n--- provenance ---");
        foreach (var (field, res) in details.FieldProvenance)
        {
            Console.WriteLine($"{field,-24} = '{res.Value}' (source={res.Source}, confidence={res.Confidence})");
        }
        if (details.Mismatches.Count > 0)
        {
            Console.WriteLine("\n--- structural/inference mismatches ---");
            foreach (var m in details.Mismatches)
            {
                Console.WriteLine($"{m.Field}: structural='{m.StructuralValue}' inference='{m.InferenceValue}'");
            }
        }
        return 0;
    }

    private const string LiveUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    /// <summary>
    /// Verifies the cookie-authenticated download path end-to-end (the C# counterpart of the
    /// Python prototype's <c>attachment_downloader.py</c> resolve step): saves each attachment
    /// under <c>scratch/attachments</c> and reports whether the bytes are a real file or an
    /// HTML login page (which is how an expired/invalid session manifests).
    /// </summary>
    private static async Task DownloadAttachmentsAsync(HttpClient http, string pageUrl, ProjectDetails details, string? cookieHeader)
    {
        var destDir = Path.Combine(AppContext.BaseDirectory, "attachments");
        Directory.CreateDirectory(destDir);
        Console.WriteLine($"\n--- attachment download ({destDir}) ---");

        foreach (var asset in details.Attachments)
        {
            if (string.IsNullOrWhiteSpace(asset.RawUrl))
            {
                Console.WriteLine($"  SKIP  '{asset.FileName}': no URL captured.");
                continue;
            }

            if (asset.RequiresAuth && cookieHeader is null)
            {
                Console.WriteLine($"  MANUAL '{asset.FileName}': requires a logged-in session and no cookie file was supplied.");
                continue;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, asset.RawUrl);
                request.Headers.Referrer = new Uri(pageUrl);
                using var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var data = await response.Content.ReadAsByteArrayAsync();

                if (LooksLikeHtml(data))
                {
                    Console.WriteLine($"  AUTHFAIL '{asset.FileName}': got an HTML page ({data.Length} bytes) instead of a file - session rejected.");
                    continue;
                }

                var path = Path.Combine(destDir, SafeFileName(asset.FileName));
                await File.WriteAllBytesAsync(path, data);
                Console.WriteLine($"  OK    '{asset.FileName}' -> {data.Length:N0} bytes ({data.Length / 1024.0:F2} KB) saved to {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR '{asset.FileName}': {ex.Message}");
            }
        }
    }

    private static bool LooksLikeHtml(byte[] data)
    {
        var head = Encoding.UTF8.GetString(data, 0, Math.Min(512, data.Length));
        return head.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
               || head.Contains("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    // -----------------------------------------------------------------
    // Tiny harness (mirrors test_analyzer.py's check/PASS/FAIL reporting)
    // -----------------------------------------------------------------

    private static void Run(string name, Action test)
    {
        Console.WriteLine($"[{name}]");
        try
        {
            test();
        }
        catch (Exception ex)
        {
            Check($"{name} completed without an unexpected exception", false, ex.ToString());
        }
        Console.WriteLine();
    }

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            Passed.Add(name);
            Console.WriteLine($"  PASS  {name}");
        }
        else
        {
            Failed.Add((name, detail ?? "(no detail)"));
            Console.WriteLine($"  FAIL  {name}  -- {detail}");
        }
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ParseException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string LoadFixture(string filename) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", filename));

    private static string Preview(string text, int max = 120) =>
        text.Length <= max ? text : text[..max] + "...";
}

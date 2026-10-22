using System.Text;
using HtmlAgilityPack;
using MostaqlK.Core.Formatting;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Sources.Mostaql.Parsing;
using MostaqlK.Sources.Nafezly;
using MostaqlK.Sources.Nafezly.Parsing;
using MostaqlK.Sources.Nafezly.Contributions;
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

        if (args.Length >= 1 && args[0] == "--audit-live")
        {
            var limit = args.Length >= 2 && int.TryParse(args[1], out var l) ? l : 10;
            var cookiePath = args.Length >= 3 ? args[2] : null;
            return RunAuditLive(limit, cookiePath).GetAwaiter().GetResult();
        }

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
        Run(nameof(TestArabicNameFormatter), TestArabicNameFormatter);
        Run(nameof(TestArabicProposalFormatting), TestArabicProposalFormatting);
        Run(nameof(TestTextTruncator), TestTextTruncator);
        Run(nameof(TestPipelineTelemetryFormatter), TestPipelineTelemetryFormatter);
        Run(nameof(TestArabicRelativeTimeParsing), TestArabicRelativeTimeParsing);
        Run(nameof(TestProposalCountForms), TestProposalCountForms);
        Run(nameof(TestProposalCountAdversarial), TestProposalCountAdversarial);
        Run(nameof(TestProposalCountJsonRoundTrip), TestProposalCountJsonRoundTrip);
        Run(nameof(TestDegenerateInputs), TestDegenerateInputs);
        Run(nameof(TestCookieJarParsing), TestCookieJarParsing);

        // Nafezly source module (A3) — offline fixture suite against the A2 capture-manifest truth,
        // plus the descriptor/contribution seam checks. None touch the 135 Mostaql checks above.
        Console.WriteLine("\n--- Nafezly (A3) ---\n");
        Run(nameof(TestNafezlyDescriptorIdentity), TestNafezlyDescriptorIdentity);
        Run(nameof(TestNafezlyListingPage1), TestNafezlyListingPage1);
        Run(nameof(TestNafezlyListingFilteredAndPagination), TestNafezlyListingFilteredAndPagination);
        Run(nameof(TestNafezlyDetailOpen56540), TestNafezlyDetailOpen56540);
        Run(nameof(TestNafezlyDetailInProgress56515), TestNafezlyDetailInProgress56515);
        Run(nameof(TestNafezlyDetailExtra56573), TestNafezlyDetailExtra56573);
        Run(nameof(TestNafezlyNullableAbsentFields), TestNafezlyNullableAbsentFields);
        Run(nameof(TestNafezlyDegenerateInputs), TestNafezlyDegenerateInputs);
        Run(nameof(TestNafezlyContributionRegistry), TestNafezlyContributionRegistry);

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
        Check("hire rate 36.36% parsed exactly", details.Owner.HiringRatePercent == 36.36, $"{details.Owner.HiringRatePercent}");
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
        Check("attachment anonymous link requiring auth",
            asset?.RequiresAuth == true && string.IsNullOrEmpty(asset.Url), asset?.RawUrl);

        Check("proposal count parsed structurally", details.ProposalCount == 5, $"{details.ProposalCount}");
        Check("proposal count source is structural",
            details.FieldProvenance.GetValueOrDefault("proposal_count")?.Source == "structural",
            details.FieldProvenance.GetValueOrDefault("proposal_count")?.Source);
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
        Check("hire rate recovered from a renamed stat list", details.Owner.HiringRatePercent == 36.36, $"{details.Owner.HiringRatePercent}");
        Check("skills recovered by href shape, pagination link excluded",
            details.Skills.Count == 3 && details.Skills.All(s => s.Url?.Contains("/skills/") == true),
            string.Join("/", details.Skills.Select(s => s.Name)));
        Check("description recovered without .text-wrapper-div",
            details.Description.Contains("المهام:", StringComparison.Ordinal), Preview(details.Description));
        Check("recovered description still keeps its line breaks", details.Description.Contains('\n'), Preview(details.Description));
        Check("attachment recovered with no data-file-type attribute",
            details.Attachments.FirstOrDefault()?.Extension == "docx",
            details.Attachments.FirstOrDefault()?.Extension);

        Check("proposal count inferred from renamed label + Arabic-Indic numeral",
            details.ProposalCount == 5, $"{details.ProposalCount}");
        Check("proposal count source is inference",
            details.FieldProvenance.GetValueOrDefault("proposal_count")?.Source == "inference",
            details.FieldProvenance.GetValueOrDefault("proposal_count")?.Source);
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
        Check("listing proposal number parsed", summaries.ElementAtOrDefault(0)?.ProposalCount == 7,
            $"{summaries.ElementAtOrDefault(0)?.ProposalCount}");
        Check("listing proposal count text parsed", summaries.ElementAtOrDefault(0)?.ProposalCountText == "7 عروض",
            $"{summaries.ElementAtOrDefault(0)?.ProposalCountText}");
        Check("listing publish time number parsed", summaries.ElementAtOrDefault(0)?.PublishTimeNumber == 4,
            $"{summaries.ElementAtOrDefault(0)?.PublishTimeNumber}");
        Check("listing publish time text parsed", summaries.ElementAtOrDefault(0)?.PublishTimeText == "منذ 4 ساعات",
            $"{summaries.ElementAtOrDefault(0)?.PublishTimeText}");
        Check("listing proposal number handles singular form", summaries.ElementAtOrDefault(1)?.ProposalCount == 12,
            $"{summaries.ElementAtOrDefault(1)?.ProposalCount}");
        Check("listing description parsed",
            summaries.ElementAtOrDefault(0)?.Description == "اريد تصميم علبه كرتون 5 كيلو تمر عجوه",
            summaries.ElementAtOrDefault(0)?.Description);
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

    private static void TestArabicNameFormatter()
    {
        Check("single word > 2 chars", ArabicNameFormatter.GetInitials("عميل") == "عم", ArabicNameFormatter.GetInitials("عميل"));
        Check("single word 1 char", ArabicNameFormatter.GetInitials("أ") == "أ", ArabicNameFormatter.GetInitials("أ"));
        Check("null/empty returns fallback", ArabicNameFormatter.GetInitials(null) == "؟", ArabicNameFormatter.GetInitials(null));
        Check("strips 'ال' from compound name", ArabicNameFormatter.GetInitials("أحمد العتيبي") == "أع", ArabicNameFormatter.GetInitials("أحمد العتيبي"));
        Check("strips 'ال' from both names if present", ArabicNameFormatter.GetInitials("المهندس المطيري") == "مم", ArabicNameFormatter.GetInitials("المهندس المطيري"));
        Check("preserves pure 'ال' word if not prefix", ArabicNameFormatter.StripArticle("ال") == "ال", ArabicNameFormatter.StripArticle("ال"));
        Check("StripArticle strips 'ال' prefix", ArabicNameFormatter.StripArticle("العتيبي") == "عتيبي", ArabicNameFormatter.StripArticle("العتيبي"));
    }

    private static void TestArabicProposalFormatting()
    {
        Check("format 0", ArabicProposalParser.Format(0) == "0 عرض", ArabicProposalParser.Format(0));
        Check("format 1", ArabicProposalParser.Format(1) == "عرض واحد", ArabicProposalParser.Format(1));
        Check("format 2", ArabicProposalParser.Format(2) == "عرضان", ArabicProposalParser.Format(2));
        Check("format 3", ArabicProposalParser.Format(3) == "3 عروض", ArabicProposalParser.Format(3));
        Check("format 10", ArabicProposalParser.Format(10) == "10 عروض", ArabicProposalParser.Format(10));
        Check("format 11", ArabicProposalParser.Format(11) == "11 عرضاً", ArabicProposalParser.Format(11));
        Check("format 25", ArabicProposalParser.Format(25) == "25 عرضاً", ArabicProposalParser.Format(25));
    }

    private static void TestTextTruncator()
    {
        Check("no truncation needed", TextTruncator.Truncate("Hello", 10) == "Hello", TextTruncator.Truncate("Hello", 10));
        Check("truncate with ellipsis", TextTruncator.Truncate("Hello World", 8) == "Hello...", TextTruncator.Truncate("Hello World", 8));
        Check("truncate null", TextTruncator.Truncate(null, 5) == "", TextTruncator.Truncate(null, 5));
        Check("truncateWords splits nicely", TextTruncator.TruncateWords("Hello Beautiful World", 14) == "Hello...", TextTruncator.TruncateWords("Hello Beautiful World", 14));
    }

    private static void TestPipelineTelemetryFormatter()
    {
        Check("processing state", PipelineTelemetryFormatter.FormatWorkerState("Processing") == "يعالج", PipelineTelemetryFormatter.FormatWorkerState("Processing"));
        Check("completed state", PipelineTelemetryFormatter.FormatWorkerState("Completed") == "مكتمل", PipelineTelemetryFormatter.FormatWorkerState("Completed"));
        Check("error state", PipelineTelemetryFormatter.FormatWorkerState("Error") == "خطأ", PipelineTelemetryFormatter.FormatWorkerState("Error"));
        Check("idle state", PipelineTelemetryFormatter.FormatWorkerState("Idle") == "خامل", PipelineTelemetryFormatter.FormatWorkerState("Idle"));
        Check("format seconds normal", PipelineTelemetryFormatter.FormatSeconds(12.36) == "12.4", PipelineTelemetryFormatter.FormatSeconds(12.36));
        Check("format seconds >= 100", PipelineTelemetryFormatter.FormatSeconds(120.4) == "120", PipelineTelemetryFormatter.FormatSeconds(120.4));
        Check("format seconds suffix", PipelineTelemetryFormatter.FormatSecondsSuffix(12.36) == "12.4 ث", PipelineTelemetryFormatter.FormatSecondsSuffix(12.36));
    }

    private static void TestArabicRelativeTimeParsing()
    {
        Check("parse 'منذ لحظات'", ArabicRelativeTime.ParseRelativeNumber("منذ لحظات") == 0);
        Check("parse 'منذ دقيقة'", ArabicRelativeTime.ParseRelativeNumber("منذ دقيقة") == 1);
        Check("parse 'منذ دقيقتين'", ArabicRelativeTime.ParseRelativeNumber("منذ دقيقتين") == 2);
        Check("parse 'منذ 7 دقائق'", ArabicRelativeTime.ParseRelativeNumber("منذ 7 دقائق") == 7);
        Check("parse 'منذ ٧ دقائق'", ArabicRelativeTime.ParseRelativeNumber("منذ ٧ دقائق") == 7);
        Check("parse 'منذ ساعتين'", ArabicRelativeTime.ParseRelativeNumber("منذ ساعتين") == 2);
        Check("parse 'منذ ساعة'", ArabicRelativeTime.ParseRelativeNumber("منذ ساعة") == 1);
        Check("parse 'منذ 3 ساعات'", ArabicRelativeTime.ParseRelativeNumber("منذ 3 ساعات") == 3);
        Check("parse 'منذ يوم'", ArabicRelativeTime.ParseRelativeNumber("منذ يوم") == 1);
        Check("parse 'منذ يومين'", ArabicRelativeTime.ParseRelativeNumber("منذ يومين") == 2);
        Check("parse 'منذ 15 يوما'", ArabicRelativeTime.ParseRelativeNumber("منذ 15 يوما") == 15);
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

    private static void TestProposalCountAdversarial()
    {
        var cases = new Dictionary<string, int>
        {
            ["\"25\""] = 25,
            ["'7'"] = 7,
            ["<span>12</span> عروض"] = 12,
            ["&quot;3&quot;"] = 3,
            ["<b>1</b>"] = 1,
            ["عدد المقترحات: ٥"] = 5,
        };

        foreach (var (input, expected) in cases)
        {
            var parsed = ArabicProposalParser.Parse(input);
            Check($"adversarial proposal '{input}' numeric value", parsed.Number == expected, $"{parsed.Number}");
            
            // Display text should be cleaned of quotes and HTML tags
            var cleanText = input.Replace("\"", "").Replace("'", "").Replace("<span>", "").Replace("</span>", "").Replace("&quot;", "").Replace("<b>", "").Replace("</b>", "");
            Check($"adversarial proposal '{input}' display text cleaned", parsed.Text == cleanText, parsed.Text);
        }
    }

    private static void TestProposalCountJsonRoundTrip()
    {
        var original = new ProjectSummary
        {
            ProjectId = 123,
            Title = "اختبار JSON",
            ProposalCount = 7,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ProjectSummary>(json);

        Check("JSON keeps proposal number", restored?.ProposalCount == 7,
            restored?.ProposalCount.ToString() ?? "null");
        Check("ArabicProposalParser formats proposal count", ArabicProposalParser.Format(restored?.ProposalCount ?? 0) == "7 عروض",
            ArabicProposalParser.Format(restored?.ProposalCount ?? 0));
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
        var devTools = CookieParser.ParseFile("mostaqlweb=\"abc%3D\"\nXSRF-TOKEN=\"def\"\n_ga=\"noise\"");
        Check("quoted name=value lines parse and unquote", devTools == "mostaqlweb=abc%3D; XSRF-TOKEN=def", devTools);
        Check("analytics cookies are dropped", devTools?.Contains("_ga") == false, devTools);

        var oneLine = CookieParser.ParseFile("a=1; b=2; c=3");
        Check("a single copied Cookie header line parses", oneLine == "a=1; b=2; c=3", oneLine);

        var netscape = CookieParser.ParseFile(
            "# Netscape HTTP Cookie File\n" +
            ".mostaql.com\tTRUE\t/\tTRUE\t1799999999\tmostaqlweb\tabc%3D\n" +
            ".mostaql.com\tTRUE\t/\tTRUE\t1799999999\tXSRF-TOKEN\tdef\n");
        Check("Netscape/curl 7-column export parses", netscape == "mostaqlweb=abc%3D; XSRF-TOKEN=def", netscape);
        Check("comment lines are ignored", netscape?.Contains('#') == false, netscape);

        var duplicated = CookieParser.ParseFile("mostaqlweb=old\nmostaqlweb=new");
        Check("a re-login duplicate keeps the last value only", duplicated == "mostaqlweb=new", duplicated);

        Check("empty input yields null rather than an empty header", CookieParser.ParseFile("   ") is null);
        Check("a file with only analytics yields null", CookieParser.ParseFile("_gid=x\n_ga=y") is null);
    }

    // -----------------------------------------------------------------
    // Nafezly (A3) — offline fixture suite
    // -----------------------------------------------------------------

    private static string LoadNafezlyFixture(string filename) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nafezly", filename));

    private static bool ThrowsNafezly(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (NafezlyParseException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The A3 identity block asserted EXACTLY (brief's fixed inputs), straight off the descriptor.</summary>
    private static void TestNafezlyDescriptorIdentity()
    {
        var d = NafezlySource.Instance;
        Check("nafezly id", d.Id == SourceId.From("nafezly"), d.Id.ToString());
        Check("DisplayNameAr تنفيذلي", d.DisplayNameAr == "نفذلي", d.DisplayNameAr);
        Check("DisplayNameLatin Nafezly", d.DisplayNameLatin == "Nafezly", d.DisplayNameLatin);
        Check("RailGlyph ن", d.RailGlyph == "ن", d.RailGlyph);
        Check("Domain nafezly.com", d.Domain == "nafezly.com", d.Domain);
        Check("currency suffix $", d.CurrencySuffix == "$", d.CurrencySuffix);
        Check("cadence 60s", d.DefaultPollIntervalSeconds == 60, $"{d.DefaultPollIntervalSeconds}");
        Check("rpm 8", d.DefaultRequestsPerMinute == 8, $"{d.DefaultRequestsPerMinute}");
        Check("internal id band 1_000_000_000_000", d.InternalIdBand == 1_000_000_000_000, $"{d.InternalIdBand}");
        Check("cookie key cookie_nafezly", d.CookieSecretKey == "cookie_nafezly", d.CookieSecretKey);
        Check("legacy cookie key empty (no pre-namespacing history)", d.LegacyCookieSecretKey == string.Empty, d.LegacyCookieSecretKey);
        Check("settings container nafezly", d.SettingsContainerId == "nafezly", d.SettingsContainerId);

        Check("brand light #FFA200", d.Brands.Brand == "#FFA200", d.Brands.Brand);
        Check("brand ink #9A6100", d.Brands.BrandInk == "#9A6100", d.Brands.BrandInk);
        Check("brand soft #FFF4DE", d.Brands.BrandSoft == "#FFF4DE", d.Brands.BrandSoft);
        Check("on-brand #3a2a00", d.Brands.OnBrand == "#3a2a00", d.Brands.OnBrand);
        Check("link light #9A6100", d.Brands.LinkLight == "#9A6100", d.Brands.LinkLight);
        Check("link dark #FFB84D", d.Brands.LinkDark == "#FFB84D", d.Brands.LinkDark);

        Check("capabilities: executor yes", d.Capabilities.HasExecutorInfo, $"{d.Capabilities.HasExecutorInfo}");
        Check("capabilities: supervised no (JS-only facet)", !d.Capabilities.HasSupervisedFlag, $"{d.Capabilities.HasSupervisedFlag}");
        Check("capabilities: session not required", !d.Capabilities.NeedsSessionForFullEnrichment, $"{d.Capabilities.NeedsSessionForFullEnrichment}");

        // BuildListingUrl appends the opaque settings query nafezly expects and defaults the path.
        Check("listing url base", d.BuildListingUrl(null) == "https://nafezly.com/projects", d.BuildListingUrl(null));
        Check("listing url appends opaque query", d.BuildListingUrl("specialize=design&page=2")
              == "https://nafezly.com/projects?specialize=design&page=2", d.BuildListingUrl("specialize=design&page=2"));
        Check("listing url normalizes leading ?", d.BuildListingUrl("?page=1") == "https://nafezly.com/projects?page=1", d.BuildListingUrl("?page=1"));
        Check("project url", d.BuildProjectUrl(56540) == "https://nafezly.com/project/56540", d.BuildProjectUrl(56540));
        Check("absolute url roots a path", d.BuildAbsoluteUrl("/x") == "https://nafezly.com/x", d.BuildAbsoluteUrl("/x"));
        Check("same-site sniff", d.IsSameSiteUrl("https://nafezly.com/project/1-a") && !d.IsSameSiteUrl("https://mostaql.com/x"));

        // The seven specialize facets (query vocabulary as module data).
        Check("seven specialize facets", NafezlyQueryVocabulary.SpecializeFacets.Count == 7, $"{NafezlyQueryVocabulary.SpecializeFacets.Count}");
        Check("development facet label", NafezlyQueryVocabulary.SpecializeFacets.Any(f => f.Slug == "development" && f.LabelAr == "برمجة، تطوير المواقع والتطبيقات"));

        // The four defensive state strings (three observed + مغلق) are all mapped.
        Check("state vocabulary has all four", NafezlyStates.All.Count == 4 &&
              NafezlyStates.All.SequenceEqual(["مفتوح", "تحت التنفيذ", "مكتمل", "مغلق"]),
              string.Join(",", NafezlyStates.All));
    }

    private static void TestNafezlyListingPage1()
    {
        var summaries = NafezlyListingParser.Parse(LoadNafezlyFixture("listing-page1.html"));

        Check("20 nafezly rows parsed", summaries.Count == 20, $"{summaries.Count}");
        var first = summaries[0];
        Check("slug-id 56609", first.ProjectId == 56609, $"{first.ProjectId}");
        Check("title present (truncated Arabic)", first.Title.Contains("مسوّق", StringComparison.Ordinal), first.Title);
        Check("state text raw مفتوح", first.ProjectStatus == "مفتوح", first.ProjectStatus);
        Check("budget text '10 - 25 $'", first.Budget == "10 - 25 $", first.Budget);
        Check("proposals count 2", first.ProposalCount == 2, $"{first.ProposalCount}");
        Check("proposals text preserved", first.ProposalCountText == "2 عروض", first.ProposalCountText);
        Check("publish relative 'منذ ساعة' -> 1", first.PublishTimeNumber == 1, $"{first.PublishTimeNumber}");
        // Listing intentionally leaves DeliveryDays null — the authoritative day count is the detail
        // page's JSON-LD workHours (see NafezlyListingParser.ParseRow).
        Check("listing duration not resolved into a day count (detail-owned)", first.DeliveryDays is null, $"{first.DeliveryDays}");
        Check("url is absolute nafezly project", first.Url.StartsWith("https://nafezly.com/project/56609-", StringComparison.Ordinal), first.Url);

        var ids = summaries.Select(s => s.ProjectId).ToList();
        Check("all ids positive", ids.All(id => id > 0));
        Check("ids distinct", ids.Distinct().Count() == ids.Count);
        Check("newest-first ordering", ids.SequenceEqual(ids.OrderByDescending(x => x)), string.Join(",", ids));
        Check("every row has a state + budget", summaries.All(s => !string.IsNullOrEmpty(s.ProjectStatus) && !string.IsNullOrEmpty(s.Budget)));
    }

    private static void TestNafezlyListingFilteredAndPagination()
    {
        var filtered = NafezlyListingParser.Parse(LoadNafezlyFixture("listing-filtered.html"));
        Check("filtered page still 20 rows (identical grammar)", filtered.Count == 20, $"{filtered.Count}");
        Check("filtered budgets shifted to the 100-250/250-500 ladder",
            filtered.Any(s => s.Budget == "100 - 250 $" || s.Budget == "250 - 500 $"),
            string.Join("|", filtered.Select(s => s.Budget).Distinct()));

        var (prev1, next1) = NafezlyListingParser.ParsePagination(LoadNafezlyFixture("listing-page1.html"));
        Check("page1 has no prev (disabled)", string.IsNullOrEmpty(prev1), prev1);
        Check("page1 next -> page=2", next1 == "https://nafezly.com/projects?page=2", next1);

        var (prev2, next2) = NafezlyListingParser.ParsePagination(LoadNafezlyFixture("listing-page2.html"));
        Check("page2 prev -> page=1", prev2 == "https://nafezly.com/projects?page=1", prev2);
        Check("page2 next -> page=3", next2 == "https://nafezly.com/projects?page=3", next2);
    }

    private static void TestNafezlyDetailOpen56540()
    {
        var d = NafezlyDetailParser.Parse(56540, LoadNafezlyFixture("detail-open.html"));
        Check("title from JSON-LD contains Cisco Packet Tracer", d.Title.Contains("Cisco Packet Tracer", StringComparison.Ordinal), d.Title);
        Check("state تحت التنفيذ (fixture name lies: it is in-progress)", d.ProjectStatus == "تحت التنفيذ", d.ProjectStatus);
        Check("budget '10 - 25 $'", d.Budget == "10 - 25 $", d.Budget);
        Check("proposals 72", d.ProposalCount == 72, $"{d.ProposalCount}");
        Check("publish 'منذ 23 ساعة' -> 23", d.PublishTimeNumber == 23, $"{d.PublishTimeNumber}");
        Check("duration 3 days (JSON-LD workHours)", d.DeliveryDays == 3, $"{d.DeliveryDays}");
        Check("3 skills from JSON-LD CSV", d.Skills.Count == 3, string.Join("/", d.Skills.Select(s => s.Name)));
        Check("skills include Cisco + Arabic labels",
            d.Skills.Any(s => s.Name == "Cisco") && d.Skills.Any(s => s.Name == "إدارة الشبكات"),
            string.Join("/", d.Skills.Select(s => s.Name)));
        Check("owner Anas Osama (app-gate anchor text)", d.Owner.Name == "Anas Osama", d.Owner.Name);
        Check("description keeps section structure", d.Description.Contains("المهام", StringComparison.Ordinal), Preview(d.Description));
        Check("budget field provenance recorded", d.FieldProvenance.TryGetValue("budget", out var bProv) && bProv.Source is "structural" or "jsonld", d.FieldProvenance.GetValueOrDefault("budget")?.Source);
        Check("duration field provenance recorded", d.FieldProvenance.ContainsKey("duration"), d.FieldProvenance.GetValueOrDefault("duration")?.Source);
    }

    private static void TestNafezlyDetailInProgress56515()
    {
        var d = NafezlyDetailParser.Parse(56515, LoadNafezlyFixture("detail-in-progress.html"));
        Check("state مكتمل (fixture name lies: it is completed)", d.ProjectStatus == "مكتمل", d.ProjectStatus);
        Check("proposals 43", d.ProposalCount == 43, $"{d.ProposalCount}");
        Check("budget '10 - 25 $'", d.Budget == "10 - 25 $", d.Budget);
        Check("publish 'منذ يوم' -> 1", d.PublishTimeNumber == 1, $"{d.PublishTimeNumber}");
        Check("duration 1 day", d.DeliveryDays == 1, $"{d.DeliveryDays}");
        Check("skills deduped (HTML/CSS repeated in CSV)", d.Skills.Count == d.Skills.Select(s => s.Name).Distinct().Count() && d.Skills.Count > 0,
            string.Join("/", d.Skills.Select(s => s.Name)));
        Check("owner دليل الخير", d.Owner.Name == "دليل الخير", d.Owner.Name);
    }

    private static void TestNafezlyDetailExtra56573()
    {
        var d = NafezlyDetailParser.Parse(56573, LoadNafezlyFixture("detail-extra.html"));
        Check("state مفتوح", d.ProjectStatus == "مفتوح", d.ProjectStatus);
        Check("budget '50 - 100 $'", d.Budget == "50 - 100 $", d.Budget);
        Check("proposals 30", d.ProposalCount == 30, $"{d.ProposalCount}");
        Check("duration 7 days", d.DeliveryDays == 7, $"{d.DeliveryDays}");
        Check("Laravel/PHP skills present", d.Skills.Any(s => s.Name == "Laravel") && d.Skills.Any(s => s.Name == "PHP"),
            string.Join("/", d.Skills.Select(s => s.Name)));
        Check("owner Saudisader Sader", d.Owner.Name == "Saudisader Sader", d.Owner.Name);
    }

    /// <summary>Fields the anonymous HTML never shows stay null/empty — never guessed.</summary>
    private static void TestNafezlyNullableAbsentFields()
    {
        var d = NafezlyDetailParser.Parse(56540, LoadNafezlyFixture("detail-open.html"));
        Check("attachments absent-by-default (no public list)", d.Attachments.Count == 0, $"{d.Attachments.Count}");
        Check("owner rating null (app-gated)", d.Owner.Rating is null, $"{d.Owner.Rating}");
        Check("owner hiring rate null (absent on nafezly)", d.Owner.HiringRatePercent is null, $"{d.Owner.HiringRatePercent}");
        Check("owner completed count null (absent)", d.Owner.CompletedProjectsCount is null, $"{d.Owner.CompletedProjectsCount}");
        Check("owner profile url null (js app-gate, no real url)", d.Owner.ProfileUrl is null, d.Owner.ProfileUrl);
        Check("owner avatar captured", d.Owner.AvatarUrl?.Contains("digitaloceanspaces.com") == true, d.Owner.AvatarUrl);
        Check("enrichment status Enriched on a good page", d.EnrichmentStatus == EnrichmentStatus.Enriched, d.EnrichmentStatus.ToString());
    }

    private static void TestNafezlyDegenerateInputs()
    {
        Check("nafezly empty html throws", ThrowsNafezly(() => NafezlyDetailParser.Parse(1, string.Empty)));
        Check("nafezly whitespace html throws", ThrowsNafezly(() => NafezlyDetailParser.Parse(1, "   ")));
        Check("nafezly non-listing page throws", ThrowsNafezly(() => NafezlyListingParser.Parse("<html><body><p>لا مشاريع</p></body></html>")));
        Check("nafezly listing empty html throws", ThrowsNafezly(() => NafezlyListingParser.Parse(string.Empty)));
    }

    /// <summary>
    /// The contract §10 "growth without branches" proof: the registry, built from DI-style
    /// collections, returns nafezly's contributed settings section for nafezly and NOTHING for
    /// mostaql — no source-id if-branch anywhere. (DI wiring itself is the app build; this checks the
    /// real contribution object the module registers.)
    /// </summary>
    private static void TestNafezlyContributionRegistry()
    {
        ISourceSettingsSectionContribution[] sections = [new NafezlySettingsSectionContribution()];
        var registry = new SourceContributionRegistry([], sections);

        var nafezlySections = registry.SettingsSectionsFor(SourceId.From("nafezly"));
        var mostaqlSections = registry.SettingsSectionsFor(SourceId.Mostaql);

        Check("registry returns nafezly's settings section", nafezlySections.Count == 1, $"{nafezlySections.Count}");
        Check("registry returns nothing for mostaql", mostaqlSections.Count == 0, $"{mostaqlSections.Count}");
        Check("nafezly section has no page contribution yet (B2)", registry.PagesFor(SourceId.From("nafezly")).Count == 0);

        var contribution = nafezlySections[0];
        Check("contribution source is nafezly", contribution.Source == SourceId.From("nafezly"), contribution.Source.ToString());
        Check("contribution header Arabic", !string.IsNullOrEmpty(contribution.HeaderAr), contribution.HeaderAr);

        var vm = contribution.CreateViewModel(new EmptyServiceProvider());
        Check("view-model built without DI", vm is NafezlySettingsSectionViewModel, vm?.GetType().Name);
        var nafezlyVm = (NafezlySettingsSectionViewModel)vm;
        Check("view-model enrichment-retry defaults on", nafezlyVm.EnrichmentRetryEnabled);
        Check("view-model reflects no-session capability", !nafezlyVm.RequiresSession, $"{nafezlyVm.RequiresSession}");
    }

    /// <summary>Minimal IServiceProvider so the contribution's CreateViewModel can be exercised offline.</summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }



    // -----------------------------------------------------------------
    // Live mode - parse a real URL end-to-end (opt-in, needs network)
    // -----------------------------------------------------------------

    /// <summary>
    /// The harness's own cookie resolution: explicit path, then the legacy dev env vars, then a
    /// repo-root <c>cookies.txt</c> walk-up - the same sources the app's per-source
    /// <c>CookieJar</c> uses, but self-contained (no DI/descriptor) and always for mostaql. Parses
    /// through the shared <see cref="CookieParser"/> so both understand identical file formats.
    /// </summary>
    private static (string? Header, string? Source) ResolveHarnessCookie(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return (CookieParser.ParseFile(File.ReadAllText(explicitPath)), explicitPath);
        }

        var inline = Environment.GetEnvironmentVariable("MOSTAQL_COOKIE");
        if (!string.IsNullOrWhiteSpace(inline))
        {
            return (CookieParser.ParseFile(inline), "MOSTAQL_COOKIE (dev)");
        }

        var envPath = Environment.GetEnvironmentVariable("MOSTAQL_COOKIE_FILE");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return (CookieParser.ParseFile(File.ReadAllText(envPath)), envPath + " (dev)");
        }

        var discovered = CookieParser.FindUpwards("cookies.txt");
        if (discovered is not null)
        {
            return (CookieParser.ParseFile(File.ReadAllText(discovered)), discovered + " (dev)");
        }

        return (null, null);
    }

    private static async Task<int> RunAuditLive(int limit, string? cookiePath)
    {
        Console.WriteLine($"Live Scrape & Audit starting (limit = {limit} projects)...\n");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", LiveUserAgent);

        var (cookieHeader, cookieSource) = ResolveHarnessCookie(cookiePath);
        if (cookieHeader is not null)
        {
            Console.WriteLine($"Cookie:       loaded ({cookieSource}, {cookieHeader.Split(';').Length} cookies)");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieHeader);
        }
        else
        {
            Console.WriteLine("Cookie:       none (anonymous fetch)");
        }

        Console.WriteLine("Fetching listing page: https://mostaql.com/projects ...");
        var listingHtml = await http.GetStringAsync("https://mostaql.com/projects");
        var summaries = ListingParser.Parse(listingHtml);
        Console.WriteLine($"Discovered {summaries.Count} projects on listing page.\n");

        var audited = 0;
        var issues = new List<string>();

        foreach (var summary in summaries.Take(limit))
        {
            audited++;
            var url = summary.Url;
            if (string.IsNullOrEmpty(url))
            {
                url = $"https://mostaql.com/project/{summary.ProjectId}";
            }

            Console.WriteLine($"[{audited}/{Math.Min(limit, summaries.Count)}] Auditing project {summary.ProjectId}: {summary.Title}");
            try
            {
                var projectHtml = await http.GetStringAsync(url);
                var details = DetailParser.Parse(summary.ProjectId, projectHtml);

                // Format values through domain formatters and presentation properties
                var budgetFormatted = BudgetFormatter.Format(details.Budget, "ر.س");
                var durationFormatted = details.DeliveryDays is int days ? ArabicRelativeTime.Days(days) : "—";
                var publishFormatted = ArabicRelativeTime.Since(details.DiscoveredAt);
                var proposalFormatted = ArabicProposalParser.Format(details.ProposalCount);
                var hireRateFormatted = details.Owner.HiringRatePercent is double rate
                    ? $"{rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%"
                    : "لم يحسب بعد";

                Console.WriteLine($"  - Owner: '{details.Owner.Name}', Reg: '{details.Owner.RegisteredAt}', Hire: '{hireRateFormatted}', Open: {details.Owner.OpenProjectsCount}, InProg: {details.Owner.InProgressProjectsCount}, Comms: {details.Owner.OngoingCommunicationsCount}, Comp: {details.Owner.CompletedProjectsCount?.ToString() ?? "null"}");
                Console.WriteLine($"  - Budget: '{budgetFormatted}', Duration: '{durationFormatted}', Proposals: '{proposalFormatted}', Skills: {details.Skills.Count}");

                // Integrity Checks
                if (string.IsNullOrWhiteSpace(details.Title))
                {
                    issues.Add($"Project {summary.ProjectId}: Title is empty.");
                }
                if (hireRateFormatted == "%")
                {
                    issues.Add($"Project {summary.ProjectId}: Hire rate rendered as broken lone '%'.");
                }
                if (details.Owner.CompletedProjectsCount is int comp && comp > 1000)
                {
                    issues.Add($"Project {summary.ProjectId}: Owner CompletedProjectsCount appears hallucinated ({comp}).");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Project {summary.ProjectId} ({url}): Exception during scrape/parse: {ex.Message}");
                Console.WriteLine($"  ERROR: {ex.Message}");
            }
            Console.WriteLine();
        }

        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"AUDIT COMPLETE: Audited {audited} projects. Issues found: {issues.Count}");
        foreach (var issue in issues)
        {
            Console.WriteLine($"  - [ISSUE] {issue}");
        }

        return issues.Count == 0 ? 0 : 1;
    }

    private static async Task<int> RunLive(string url, string? cookiePath)
    {
        Console.WriteLine($"Live parse: {url}\n");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", LiveUserAgent);

        var (cookieHeader, cookieSource) = ResolveHarnessCookie(cookiePath);
        if (cookieHeader is not null)
        {
            Console.WriteLine($"Cookie:       loaded ({cookieSource}, {cookieHeader.Split(';').Length} cookies)");
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

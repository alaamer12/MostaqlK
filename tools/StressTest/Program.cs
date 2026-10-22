using System.Text.Json;
using System.Text.RegularExpressions;
using MostaqlK.Core;
using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Http;
using MostaqlK.Models;
using MostaqlK.Sources.Nafezly;
using MostaqlK.Sources.Nafezly.Parsing;

// Nafezly stress harness — see StressTest.csproj header for usage. Modes:
//   fixtures (default): parse every fixture page offline; safe anywhere.
//   live: paced fetch from the real site — MUST be run from a residential network
//         (datacenter IPs get Cloudflare JS challenges; contract §11 precondition).

var opts = ParseArgs(args);
var repoRoot = FindRepoRoot();
var fixturesDir = Path.Combine(repoRoot, "tools/ParserTests/Fixtures/nafezly");
var outDir = opts.Out ?? Path.Combine(repoRoot, "tools/StressTest/out");
Directory.CreateDirectory(outDir);

var tierHist = new Dictionary<string, int>();
var anomalies = new List<string>();
var dump = new List<object>();
int listingRows = 0, detailsParsed = 0, failures = 0;

var states = new HashSet<string> { "مفتوح", "تحت التنفيذ", "مكتمل", "مغلق" };
var budgetRx = new Regex(@"^[\d.,]+\s*[-–]\s*[\d.,]+\s*\$?");

void ConsumeDetail(long id, string html, string pageName)
{
    var d = NafezlyDetailParser.Parse(id, html);
    detailsParsed++;
    var prov = d.FieldProvenance;
    foreach (var kv in prov)
        tierHist[kv.Value.Source] = tierHist.GetValueOrDefault(kv.Value.Source) + 1;
    dump.Add(new
    {
        page = pageName, kind = "detail", id = d.ProjectId, title = d.Title, status = d.ProjectStatus,
        budget = d.Budget, deliveryDays = d.DeliveryDays, proposals = d.ProposalCount,
        proposalText = d.ProposalCountText, publish = d.PublishTimeText, skills = d.Skills,
        owner = d.Owner.Name, executor = d.ExecutorName, enrichment = d.EnrichmentStatus.ToString(),
        provenance = prov.ToDictionary(p => p.Key, p => new { p.Value.Source, p.Value.Confidence, p.Value.Value }),
    });
    if (d.ProjectStatus is not null && !states.Contains(d.ProjectStatus))
        anomalies.Add($"{pageName}: state outside vocabulary: '{d.ProjectStatus}'");
    if (d.Budget is not null && !budgetRx.IsMatch(d.Budget.Trim()))
        anomalies.Add($"{pageName}: budget shape unusual: '{d.Budget}'");
    foreach (var p in prov.Where(p => p.Value.Source == "inference" && p.Value.Confidence < 0.5))
        anomalies.Add($"{pageName}: low-confidence inference on core field {p.Key}@{p.Value.Confidence:F2}");
    if (d.EnrichmentStatus == EnrichmentStatus.Failed)
        anomalies.Add($"{pageName}: EnrichmentStatus.Failed");
}

void ConsumeListing(string html, string pageName)
{
    var rows = NafezlyListingParser.Parse(html);
    listingRows += rows.Count;
    foreach (var s in rows)
    {
        dump.Add(new
        {
            page = pageName, kind = "listing", id = s.ProjectId, title = s.Title, state = s.ProjectStatus,
            budget = s.Budget, proposals = s.ProposalCount, proposalText = s.ProposalCountText,
            time = s.PublishTimeText, url = s.Url,
        });
        if (s.ProjectId <= 0) anomalies.Add($"{pageName}: non-positive id in '{Trunc(s.Title)}'");
        if (s.ProjectStatus is not null && !states.Contains(s.ProjectStatus))
            anomalies.Add($"{pageName}: listing state outside vocabulary: '{s.ProjectStatus}'");
        if (s.ProposalCount == 0 && !string.IsNullOrEmpty(s.ProposalCountText) && s.ProposalCountText.Any(char.IsDigit) && !s.ProposalCountText.Contains('0'))
            anomalies.Add($"{pageName}: proposal text/number mismatch '{s.ProposalCountText}'");
    }
    var (prev, next) = NafezlyListingParser.ParsePagination(html);
    if (next is not null && next.Contains("&amp;"))
        anomalies.Add($"{pageName}: pagination href still entity-encoded: {Trunc(next)}");
}

if (opts.Mode == "fixtures")
{
    foreach (var f in Directory.GetFiles(fixturesDir, "detail-*.html").Order())
    {
        var id = Path.GetFileName(f) switch
        {
            "detail-open.html" => 56540L, "detail-in-progress.html" => 56515L, "detail-extra.html" => 56573L,
            _ => long.TryParse(Regex.Match(Path.GetFileName(f), @"(\d+)").Groups[1].Value, out var p) ? p : 0L,
        };
        if (id == 0) continue;
        try { ConsumeDetail(id, File.ReadAllText(f), Path.GetFileName(f)); }
        catch (Exception ex) { failures++; anomalies.Add($"{f}: parse threw {ex.GetType().Name}"); }
    }
    foreach (var f in Directory.GetFiles(fixturesDir, "listing-*.html").Order())
    {
        try { ConsumeListing(File.ReadAllText(f), Path.GetFileName(f)); }
        catch (Exception ex) { failures++; anomalies.Add($"{f}: parse threw {ex.GetType().Name}"); }
    }
}
else
{
    var rnd = new Random(opts.Seed ?? Environment.TickCount);
    using var http = new HttpClient();
    var d = NafezlySource.Instance;
    http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", d.UserAgent);
    http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", d.AcceptHeader);
    http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", d.AcceptLanguageHeader);
    http.Timeout = TimeSpan.FromSeconds(30);

    var pages = PickPages(rnd, opts);
    var seenIds = new List<long>();
    foreach (var pg in pages)
    {
        var url = d.BuildListingUrl($"page={pg}");
        var (html, name) = await FetchAsync(http, url, $"live-page{pg}");
        if (html is null) { failures++; if (failures == pages.Count) { ReportBlocked(); return 3; } continue; }
        try { ConsumeListing(html, name); }
        catch (Exception ex) { failures++; anomalies.Add($"{name}: parse threw {ex.GetType().Name}"); }
        await Task.Delay(TimeSpan.FromSeconds(opts.Pace));
    }

    // detail sampling from freshly parsed listing rows
    var ids = dump.Where(x => (string)x.GetType().GetProperty("kind")!.GetValue(x)! == "listing"
        && (long)x.GetType().GetProperty("id")!.GetValue(x)! > 0)
        .Select(x => (long)x.GetType().GetProperty("id")!.GetValue(x)!).Distinct().ToList();
    foreach (var id in ids.OrderBy(_ => rnd.Next()).Take(opts.Details))
    {
        var (html, name) = await FetchAsync(http, d.BuildProjectUrl(id), $"live-detail{id}");
        if (html is null) { failures++; continue; }
        try { ConsumeDetail(id, html, name); }
        catch (Exception ex) { failures++; anomalies.Add($"{name}: parse threw {ex.GetType().Name}"); }
        await Task.Delay(TimeSpan.FromSeconds(opts.Pace));
    }
}

var stats = new
{
    mode = opts.Mode, detailPages = detailsParsed, listingRows,
    tierHistogram = tierHist.OrderByDescending(t => t.Value).ToDictionary(t => t.Key, t => t.Value),
    inferenceShare = tierHist.Values.Sum() == 0 ? 0 : (double)tierHist.GetValueOrDefault("inference", 0) / tierHist.Values.Sum(),
    failures, anomalies,
};
File.WriteAllText(Path.Combine(outDir, "dump.json"), JsonSerializer.Serialize(dump, JsonOpts));
File.WriteAllText(Path.Combine(outDir, "stats.json"), JsonSerializer.Serialize(stats, JsonOpts));
Console.WriteLine($"mode={opts.Mode} details={detailsParsed} listingRows={listingRows} failures={failures}");
Console.WriteLine("tiers: " + string.Join(" ", stats.tierHistogram.Select(t => $"{t.Key}={t.Value}")));
Console.WriteLine($"inferenceShare={stats.inferenceShare:P1} anomalies={anomalies.Count}");
foreach (var a in anomalies.Take(30)) Console.WriteLine("  ! " + a);
Console.WriteLine($"output: {outDir}");
return 0;

static JsonSerializerOptions JsonOpts { get; } = new()
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

static string Trunc(string s) => s.Length <= 40 ? s : s[..40];

static void ReportBlocked()
{
    Console.WriteLine("LIVE BLOCKED: every request was refused (Cloudflare). This is expected from datacenter IPs.");
    Console.WriteLine("Run `--mode live` from a residential network (owner paced pass, contract §11).");
}

static async Task<(string? html, string name)> FetchAsync(HttpClient http, string url, string name)
{
    try
    {
        var resp = await http.GetAsync(url);
        if ((int)resp.StatusCode == 429)
        {
            var ra = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
            await Task.Delay(ra);
            resp = await http.GetAsync(url);
        }
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            if (body.Contains("Just a moment")) { Console.WriteLine($"{name}: 403 challenge"); return (null, name); }
            Console.WriteLine($"{name}: HTTP {(int)resp.StatusCode}");
            return (null, name);
        }
        return (await resp.Content.ReadAsStringAsync(), name);
    }
    catch (Exception ex) { Console.WriteLine($"{name}: {ex.GetType().Name}"); return (null, name); }
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MostaqlK.csproj"))) dir = dir.Parent;
    return dir!.FullName;
}

static List<int> PickPages(Random rnd, Opts o)
{
    var (lo, hi) = o.Random is { } r ? (r.Min, r.Max) : (1, 100);
    return Enumerable.Range(Math.Max(1, lo), Math.Max(1, hi - Math.Max(1, lo) + 1))
        .OrderBy(_ => rnd.Next()).Take(o.Pages).Order().ToList();
}

static Opts ParseArgs(string[] args)
{
    var o = new Opts { Mode = "fixtures", Pages = 10, Details = 5, Pace = 8 };
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--mode": o.Mode = args[++i]; break;
            case "--pages": o.Pages = int.Parse(args[++i]); break;
            case "--details": o.Details = int.Parse(args[++i]); break;
            case "--pace": o.Pace = int.Parse(args[++i]); break;
            case "--seed": o.Seed = int.Parse(args[++i]); break;
            case "--out": o.Out = Path.GetFullPath(args[++i]); break;
            case "--random":
                var parts = args[++i].Split(',');
                o.Random = (int.Parse(parts[0]), int.Parse(parts[1]));
                break;
            case "--help": case "-h":
                Console.WriteLine("dotnet run --project tools/StressTest -- [--mode fixtures|live] [--pages N] [--random lo,hi] [--details N] [--pace SEC] [--seed N] [--out DIR]");
                Environment.Exit(0);
                break;
        }
    }
    return o;
}

record Opts
{
    public string Mode { get; init; } = "fixtures";
    public int Pages { get; init; }
    public int Details { get; init; }
    public int Pace { get; init; }
    public int? Seed { get; init; }
    public string? Out { get; init; }
    public (int Min, int Max)? Random { get; init; }
}

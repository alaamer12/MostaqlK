using MostaqlK.Core.Sources;

namespace MostaqlK.Sources.Nafezly;

/// <summary>
/// Nafezly's OWN query-facet vocabulary as immutable module data (backend-ui-contract §8, decision
/// 5): the URL path the poller hits, the seven <c>specialize</c> category slugs with the Arabic
/// labels the site itself prints next to each, and the facet parameter names + echoed order.
/// Shared code never names a facet — it hands the descriptor an opaque <c>QueryParams</c> string and
/// the nafezly UI (B3) would render this typed vocabulary; nothing here is a UI constant.
/// Every value is quoted from <c>tools/ParserTests/Fixtures/nafezly/capture-manifest.md</c> §3.
/// </summary>
public static class NafezlyQueryVocabulary
{
    /// <summary>The projects-listing route polled for this source.</summary>
    public const string ListingPath = "/projects";

    /// <summary>Facet parameter names, in the exact order nafezly echoes them in paged URLs (§2).</summary>
    public const string SpecializeParam = "specialize";
    public const string SkillsParam = "skills";
    public const string PricingParam = "pricing";
    public const string PageParam = "page";

    /// <summary>
    /// One specialize facet: the slug that goes in the query and the Arabic label the site renders
    /// for it (capture-manifest §3, verbatim from each <c>&lt;label for="{slug}"&gt;</c>).
    /// </summary>
    public sealed record SpecializeFacet(string Slug, string LabelAr);

    /// <summary>The seven confirmed category slugs (§3), in the site's own sidebar order.</summary>
    public static IReadOnlyList<SpecializeFacet> SpecializeFacets { get; } =
    [
        new("business", "أعمال وخدمات استشارية وإدارية"),
        new("development", "برمجة، تطوير المواقع والتطبيقات"),
        new("design", "تصميم وأعمال فنية وإبداعية"),
        new("marketing", "تسويق الكتروني ومبيعات"),
        new("writing-translation", "كتابة، صناعة محتوى، ترجمة ولغات"),
        new("training", "تدريب، تعليم ومساعدة عن بعد"),
        new("other", "أمور أخرى"),
    ];

    /// <summary>The slug set a filter builder validates a chosen <c>specialize</c> value against.</summary>
    public static IReadOnlySet<string> SpecializeSlugs { get; } =
        new HashSet<string>(SpecializeFacets.Select(f => f.Slug), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Compose the query string nafezly expects: <c>specialize</c>/<c>skills</c> are comma-joined
    /// (the site encodes commas as <c>%2C</c>), <c>pricing</c> is <c>min,max</c>, <c>page</c> last.
    /// Empty/null facet parts are omitted, matching how the site's own JS builds the URL (§3).
    /// </summary>
    public static string BuildQuery(
        IReadOnlyList<string>? specialize,
        IReadOnlyList<string>? skills,
        (long Min, long Max)? pricing,
        int? page)
    {
        var parts = new List<string>();
        if (specialize is { Count: > 0 })
        {
            parts.Add($"{SpecializeParam}={string.Join(",", specialize)}");
        }

        if (skills is { Count: > 0 })
        {
            parts.Add($"{SkillsParam}={string.Join(",", skills)}");
        }

        if (pricing is { } band)
        {
            parts.Add($"{PricingParam}={band.Min},{band.Max}");
        }

        if (page is > 0)
        {
            parts.Add($"{PageParam}={page}");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    /// <summary>
    /// The typed facet vocabulary the shared FilterModal renders for this source (B3; contract §8):
    /// the <c>specialize</c> chips straight from <see cref="SpecializeFacets"/> (no duplicated slugs
    /// or labels), the <c>skills</c> facet as a choice kind with NO options — the site's skill list is
    /// search/session-driven and has no anonymous option vocabulary (capture-manifest §3), so the UI
    /// honestly shows nothing for it while its values still round-trip — and the <c>pricing</c>
    /// slider as a range. Captions are the site's OWN sidebar group headings, verbatim from the
    /// fixtures: <c>التصنيفات</c> / <c>المهارات</c> / <c>الميزانية</c>. <c>page</c> is pagination,
    /// not a user facet, so it is parse/build-preserved but never rendered.
    /// </summary>
    public static IReadOnlyList<FilterFacetSpec> FacetSpecs { get; } =
    [
        new(SpecializeParam, FilterFacetKind.MultiChoice, "التصنيفات",
            [.. SpecializeFacets.Select(f => new FilterFacetOption(f.Slug, f.LabelAr))]),
        new(SkillsParam, FilterFacetKind.MultiChoice, "المهارات", []),
        new(PricingParam, FilterFacetKind.Range, "الميزانية", []),
    ];

    /// <summary>
    /// Serialize a modal selection back into the query string, reusing <see cref="BuildQuery"/> for
    /// every recognized facet (single grammar site) and echoing any other parsed keys afterwards in
    /// their original order, so values the modal never rendered (skills slugs, <c>page</c>, unknown
    /// params) survive Apply untouched.
    /// </summary>
    public static string BuildQueryFromFacetSelection(IReadOnlyDictionary<string, IReadOnlyList<string>> selections)
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { SpecializeParam, SkillsParam, PricingParam, PageParam };

        IReadOnlyList<string>? specialize = Take(SpecializeParam), skills = Take(SkillsParam);
        (long Min, long Max)? pricing = null;
        if (selections.TryGetValue(PricingParam, out var band) && band.Count >= 2
            && long.TryParse(band[0], out var min) && long.TryParse(band[1], out var max))
        {
            pricing = (min, max);
        }

        int? page = null;
        if (selections.TryGetValue(PageParam, out var pageNo) && pageNo.Count > 0
            && int.TryParse(pageNo[0], out var parsedPage) && parsedPage > 0)
        {
            page = parsedPage;
        }

        IReadOnlyList<string>? Take(string key) =>
            selections.TryGetValue(key, out var values) && values.Count > 0 ? values : null;

        var extras = selections
            .Where(kv => !known.Contains(kv.Key))
            .Select(kv => kv.Value.Count > 0 ? $"{kv.Key}={string.Join(",", kv.Value)}" : kv.Key)
            .ToList();

        var baseQuery = BuildQuery(specialize, skills, pricing, page);
        if (extras.Count == 0)
        {
            return baseQuery;
        }

        var extrasQuery = string.Join("&", extras);
        return baseQuery.Length == 0 ? "?" + extrasQuery : baseQuery + "&" + extrasQuery;
    }

    /// <summary>
    /// Parse a stored query string into the facet-selection shape: every <c>key=value</c> segment
    /// becomes key → value list, comma-separated ("a,b") and site-echoed percent-encoded ("a%2Cb")
    /// forms alike — the <c>Uri.UnescapeDataString</c> decode happens before the split,
    /// so %2C is a value separator, never data. Positional commas are kept (a blank bound stays a
    /// blank entry, so <c>pricing=,100</c> parses min-blank/max-100), and everything after the first
    /// '=' stays raw. A leading '?' (which <see cref="BuildQuery"/> emits and Settings may omit) is
    /// not data and is dropped.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseFacetSelection(string? queryParams)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var raw = queryParams?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return result;
        }

        if (raw[0] == '?')
        {
            raw = raw[1..];
        }

        foreach (var segment in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf('=');
            var key = separator < 0 ? segment : segment[..separator];
            var valueText = separator < 0 ? string.Empty : segment[(separator + 1)..];

            IReadOnlyList<string> values = valueText.Length == 0
                ? []
                : [.. SplitValues(Uri.UnescapeDataString(valueText))];

            result[key] = result.TryGetValue(key, out var existing)
                ? [.. existing, .. values]
                : values;
        }

        return result;
    }

    /// <summary>Comma split that keeps blank positions ("100," → ["100",""]), unlike RemoveEmptyEntries.</summary>
    private static IEnumerable<string> SplitValues(string decoded) => decoded.Split(',');
}

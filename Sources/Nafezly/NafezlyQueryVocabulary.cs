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
}

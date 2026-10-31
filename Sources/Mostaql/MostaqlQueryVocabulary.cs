using MostaqlK.Core.Sources;

namespace MostaqlK.Sources.Mostaql;

/// <summary>
/// Mostaql's OWN query-facet vocabulary as immutable module data (backend-ui-contract §8, decision 5),
/// mirroring <see cref="Sources.Nafezly.NafezlyQueryVocabulary"/>: the nine <c>category</c> slugs the
/// site's own filter sidebar emits (the slugs are exactly those in the site's category-filter URLs,
/// e.g. <c>mostaql.com/projects?category=business,development,...</c>) with the Arabic labels the site
/// prints next to each.
/// <para>
/// Unlike nafezly, mostaql must never break the byte-identical legacy passthrough that is its spec
/// today (<c>TestB3MostaqlLegacyPassthrough</c>): free-text suffixes, unknown slugs, duplicate
/// segments and a leading <c>?</c> all keep flowing through the source's echo parse/build. This
/// vocabulary therefore recognizes ONLY its own canonical typed shape — an optional leading
/// <c>?</c>-less <c>category=</c> segment of known slugs plus an optional <c>sort=latest</c> — and
/// hands everything else back as "not typed" so the caller falls through to the echo.
/// </para>
/// </summary>
public static class MostaqlQueryVocabulary
{
    /// <summary>Facet parameter names the site uses in its listing URLs.</summary>
    public const string CategoryParam = "category";
    public const string SortParam = "sort";

    /// <summary>The only sort value the typed builder emits; anything else is legacy free text.</summary>
    public const string SortLatest = "latest";

    /// <summary>One category facet: the slug that goes in the query and the Arabic label the site renders.</summary>
    public sealed record CategoryFacet(string Slug, string LabelAr);

    /// <summary>The nine confirmed category slugs, in the site's own sidebar order.</summary>
    public static IReadOnlyList<CategoryFacet> CategoryFacets { get; } =
    [
        new("business", "أعمال وخدمات استشارية وإدارية"),
        new("development", "برمجة وتطوير المواقع والتطبيقات"),
        new("ai-machine-learning", "ذكاء اصطناعي وتعلم الآلة"),
        new("engineering-architecture", "هندسة وعمارة"),
        new("design", "تصميم وأعمال فنية وإبداعية"),
        new("marketing", "تسويق إلكتروني ومبيعات"),
        new("writing-translation", "كتابة وترجمة ولغات"),
        new("support", "دعم ومساندة"),
        new("training", "تدريب وتعليم"),
    ];

    /// <summary>The slug set a filter builder validates a chosen <c>category</c> value against.</summary>
    public static IReadOnlySet<string> CategorySlugs { get; } =
        new HashSet<string>(CategoryFacets.Select(f => f.Slug), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The typed facet vocabulary the shared FilterModal renders for this source: one MultiChoice of
    /// the nine slugs, captioned with the site's own sidebar heading.
    /// </summary>
    public static IReadOnlyList<FilterFacetSpec> FacetSpecs { get; } =
    [
        new(CategoryParam, FilterFacetKind.MultiChoice, "التصنيفات",
            [.. CategoryFacets.Select(f => new FilterFacetOption(f.Slug, f.LabelAr))]),
    ];

    /// <summary>
    /// True only for the canonical typed shape: at most one <c>category=</c> segment whose every
    /// comma-separated slug is known, optionally followed by one <c>sort=latest</c> segment, with no
    /// leading <c>?</c> (the app's own stored canonical form), no empty segments and no other keys.
    /// Everything else — legacy presets, free text, odd encodings — must stay on the echo path.
    /// </summary>
    public static bool IsCanonicalTypedQuery(string? rawQuery) => TryParseTyped(rawQuery) is not null;

    /// <summary>
    /// Parses the canonical typed shape into the facet-selection form ({category:[slugs...],
    /// [sort:[latest]]}); returns null for anything that is not exactly that shape, so the caller
    /// falls through to the byte-identical legacy echo parse.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>>? TryParseTyped(string? queryParams)
    {
        var raw = queryParams?.Trim() ?? string.Empty;
        if (raw.Length == 0 || raw[0] is '?' or '&')
        {
            // Empty is the echo's "nothing to build"; a leading ?/& marks a hand-typed legacy string.
            return raw.Length == 0 ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) : null;
        }

        var segments = raw.Split('&');
        List<string>? categories = null;
        List<string>? sorts = null;

        foreach (var segment in segments)
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                return null; // keyless or "=weird" segments are legacy
            }

            var key = segment[..separator];
            var value = segment[(separator + 1)..];
            if (value.Length == 0)
            {
                return null; // empty values are legacy
            }

            if (key.Equals(CategoryParam, StringComparison.Ordinal))
            {
                if (categories is not null)
                {
                    return null; // duplicate category segments are legacy
                }

                var slugs = value.Split(',');
                if (slugs.Any(s => s.Length == 0 || !CategorySlugs.Contains(s)))
                {
                    return null; // unknown slugs stay legacy free text
                }

                categories = [.. slugs];
            }
            else if (key.Equals(SortParam, StringComparison.Ordinal))
            {
                if (sorts is not null || !value.Equals(SortLatest, StringComparison.Ordinal))
                {
                    return null; // only sort=latest is typed
                }

                sorts = [value];
            }
            else
            {
                return null; // any other key (sub_category, skills, ...) is legacy
            }
        }

        if (categories is null && sorts is null)
        {
            return null; // a bare unknown-shaped string is legacy, not an empty selection
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (categories is not null)
        {
            result[CategoryParam] = categories;
        }

        if (sorts is not null)
        {
            result[SortParam] = sorts;
        }

        return result;
    }

    /// <summary>
    /// Builds the canonical typed query from a selection: <c>category=&lt;comma-joined&gt;</c> then
    /// <c>sort=latest</c> when present; an empty selection builds the empty string (= all projects).
    /// Returns false when the selection carries any key outside the typed vocabulary (an echo-shaped
    /// dict from a legacy parse), so the caller keeps byte-echoing it.
    /// </summary>
    public static bool TryBuildTyped(
        IReadOnlyDictionary<string, IReadOnlyList<string>> selections,
        out string query)
    {
        query = string.Empty;
        var hasCategory = false;
        var parts = new List<string>();

        foreach (var (key, values) in selections)
        {
            if (key.Equals(CategoryParam, StringComparison.Ordinal))
            {
                if (hasCategory || values.Any(v => v.Length == 0))
                {
                    return false;
                }

                hasCategory = true;
                if (values.Count > 0)
                {
                    parts.Add($"{CategoryParam}={string.Join(",", values)}");
                }
            }
            else if (key.Equals(SortParam, StringComparison.Ordinal))
            {
                if (values.Count == 0)
                {
                    continue;
                }

                if (values.Count > 1 || !SortLatest.Equals(values[0], StringComparison.Ordinal))
                {
                    return false;
                }

                parts.Add($"{SortParam}={SortLatest}");
            }
            else
            {
                return false; // echo-shaped or foreign keys: not ours to rebuild
            }
        }

        query = string.Join("&", parts);
        return true;
    }
}

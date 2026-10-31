namespace MostaqlK.Core.Sources;

/// <summary>
/// The category options every registered source can filter by — the intersection of each source's
/// own typed MultiChoice facet vocabulary, derived at runtime from the descriptors (contract §10:
/// growth without branches). Onboarding's interest picker and any other shared surface consumes this
/// and never names a platform or a facet key: adding a third source automatically narrows the common
/// set, and a source with no typed facets contributes nothing to it.
/// </summary>
public static class SharedFilterVocabulary
{
    /// <summary>
    /// Options present in the first option-bearing MultiChoice facet of <b>every</b> descriptor,
    /// matched by value slug (case-insensitive), in the first source's order with the first
    /// source's label. Empty when fewer than two sources publish a category facet — there is
    /// nothing "common" to show until the app really is multi-source.
    /// </summary>
    public static IReadOnlyList<FilterFacetOption> CommonCategoryOptions(IEnumerable<ISourceDescriptor> descriptors)
    {
        var facets = descriptors
            .Select(d => d.FilterFacets.FirstOrDefault(f => f.Kind == FilterFacetKind.MultiChoice && f.Options.Count > 0))
            .Where(f => f is not null)
            .ToList();

        if (facets.Count < 2)
        {
            return [];
        }

        return
        [
            .. facets[0]!.Options.Where(o => facets.All(f =>
                f!.Options.Any(x => string.Equals(x.Value, o.Value, StringComparison.OrdinalIgnoreCase)))),
        ];
    }

    /// <summary>Registry-wide convenience overload — the same derivation over <see cref="SourceRegistry.All"/>.</summary>
    public static IReadOnlyList<FilterFacetOption> CommonCategoryOptions(SourceRegistry registry) =>
        CommonCategoryOptions(registry.All);
}

namespace MostaqlK.Core.Sources;

/// <summary>
/// Turns the onboarding wizard's shared interest selection into each platform's own
/// <c>QueryParams</c> string — pure, headless-testable glue between the picker and the
/// descriptors. The UI hands only the slug list; every grammar decision stays module-side
/// (contract §8): the subset is taken from the descriptor's own facet options and serialized
/// by <see cref="ISourceDescriptor.BuildQueryFromFacets"/>. An empty selection (or a source
/// with no matching facet/vocabulary) composes the empty string = the unfiltered default listing.
/// </summary>
public static class OnboardingQueryComposer
{
    public static string BuildQueryFor(ISourceDescriptor descriptor, IReadOnlyList<string> selectedInterests)
    {
        if (selectedInterests.Count == 0)
        {
            return string.Empty;
        }

        var facet = descriptor.FilterFacets.FirstOrDefault(f =>
            f.Kind == FilterFacetKind.MultiChoice && f.Options.Count > 0);
        if (facet is null)
        {
            return string.Empty;
        }

        var subset = facet.Options
            .Where(o => selectedInterests.Contains(o.Value, StringComparer.OrdinalIgnoreCase))
            .Select(o => o.Value)
            .ToList();
        if (subset.Count == 0)
        {
            return string.Empty;
        }

        var selection = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [facet.Key] = subset };
        return descriptor.BuildQueryFromFacets(selection);
    }
}

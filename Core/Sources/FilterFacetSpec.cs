namespace MostaqlK.Core.Sources;

/// <summary>
/// One filter facet a source exposes for its listing pages, as PURE DESCRIPTOR DATA — the
/// shared FilterModal (B3) renders whatever the active source's descriptor lists and never
/// names a facet or a site itself (contract §8 + §10 growth-without-branches). A source with
/// no facets contributes an empty list and the modal simply shows nothing for it.
/// </summary>
/// <param name="Key">Query-parameter name the module's builder emits (e.g. <c>specialize</c>).
/// Opaque to the UI.</param>
/// <param name="Kind">How the UI renders the control.</param>
/// <param name="LabelAr">Facet caption in Arabic.</param>
/// <param name="Options">Choices for choice-kind facets (value slug + Arabic label); empty for
/// range/text kinds.</param>
/// <param name="MinDefault">Range kind: placeholder/fallback lower bound (null = unset).</param>
/// <param name="MaxDefault">Range kind: placeholder/fallback upper bound (null = unset).</param>
public sealed record FilterFacetSpec(
    string Key,
    FilterFacetKind Kind,
    string LabelAr,
    IReadOnlyList<FilterFacetOption> Options,
    string? MinDefault = null,
    string? MaxDefault = null);

public enum FilterFacetKind
{
    /// <summary>Multiple selectable slugs joined by the module's separator.</summary>
    MultiChoice,
    /// <summary>Two numeric bounds (min/max) the module formats into its own syntax.</summary>
    Range,
}

/// <summary>One selectable option of a <see cref="FilterFacetKind.MultiChoice"/> facet.</summary>
/// <param name="Value">Query value slug, opaque to the UI.</param>
/// <param name="LabelAr">Option caption in Arabic.</param>
public sealed record FilterFacetOption(
    string Value,
    string LabelAr);

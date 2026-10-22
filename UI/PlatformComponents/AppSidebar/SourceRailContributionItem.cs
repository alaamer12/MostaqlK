using MostaqlK.Core.Sources;

namespace MostaqlK.UI.PlatformComponents.AppSidebar;

/// <summary>
/// A source's contributed page rendered as a rail nav item — the exact shape the shell rail
/// paints from <see cref="SourceContributionRegistry.PagesFor"/> (glyph + Arabic title + route).
/// Kept MAUI-free (plain strings) so the rail's contribution projection is unit-checkable without
/// the UI stack, and so shared UI never names a source id: it only reads what the registry hands back
/// for the ACTIVE source (contract §10 "growth without branches").
/// </summary>
public sealed record SourceRailContributionItem(string Glyph, string TitleAr, string Route, int Order);

/// <summary>
/// Pure projection from the contribution seam to rail items: filter + order already happened inside
/// <see cref="SourceContributionRegistry"/> (its ctor sorts by <c>Order</c>, <c>PagesFor</c> filters
/// by source), so this only maps each contribution's descriptor fields onto the visual item. A
/// source that contributes no pages simply yields an empty list — the rail renders nothing extra,
/// which is exactly the nafezly-today case.
/// </summary>
public static class SourceRailContributionProjection
{
    public static IReadOnlyList<SourceRailContributionItem> Project(IReadOnlyList<ISourcePageContribution> contributions)
    {
        var items = new List<SourceRailContributionItem>(contributions.Count);
        foreach (var contribution in contributions)
        {
            items.Add(new SourceRailContributionItem(
                contribution.Glyph,
                contribution.TitleAr,
                contribution.Route,
                contribution.Order));
        }

        return items;
    }
}

namespace MostaqlK.Core.Sources;

/// <summary>
/// A page that belongs to ONE source and appears in the shell only while that source is
/// active (plan decision 6: sources grow independently). Modules register implementations
/// in their <c>Register(IServiceCollection)</c>; the rail renders whatever
/// <see cref="SourceContributionRegistry.PagesFor"/> returns for the active source —
/// shared UI never branches on a source id.
/// </summary>
public interface ISourcePageContribution
{
    public SourceId Source { get; }

    /// <summary>Shell route the module registered (e.g. <c>"NafezlyInsightsPage"</c>), navigated via <see cref="MostaqlK.Core.Navigation.AppRoute"/>.</summary>
    public string Route { get; }

    /// <summary>Rail label in Arabic.</summary>
    public string TitleAr { get; }

    /// <summary>Single-glyph rail icon, same convention as <c>ISourceDescriptor.RailGlyph</c>.</summary>
    public string Glyph { get; }

    /// <summary>Order among the active source's own contributions (standard nav items always lead).</summary>
    public int Order { get; }
}

/// <summary>
/// A settings section contributed by one source only (e.g. nafezly's session-state /
/// enrichment-retry rows). The Settings page renders descriptor data rows first, then the
/// active source's contributed sections — presence/absence is data, never an if-branch.
/// The view-model is <c>object</c> so Core stays UI-free; the settings host presents it via
/// its <c>DataTemplate</c> registry, same as any feature VM.
/// </summary>
public interface ISourceSettingsSectionContribution
{
    public SourceId Source { get; }

    /// <summary>Section header in Arabic.</summary>
    public string HeaderAr { get; }

    /// <summary>Builds the section's view-model from the app container. Called once per Settings open.</summary>
    public object CreateViewModel(IServiceProvider services);
}

/// <summary>
/// Index of all registered per-source contributions, keyed like <see cref="SourceRegistry"/>.
/// Built from DI collections, so adding a contribution is only a module-side registration —
/// zero shared-code edits (the independent-growth gate in the v3 contract §10).
/// </summary>
public sealed class SourceContributionRegistry
{
    private readonly IReadOnlyList<ISourcePageContribution> _pages;
    private readonly IReadOnlyList<ISourceSettingsSectionContribution> _settingsSections;

    public SourceContributionRegistry(
        IEnumerable<ISourcePageContribution> pages,
        IEnumerable<ISourceSettingsSectionContribution> settingsSections)
    {
        _pages = pages.OrderBy(p => p.Order).ToList();
        _settingsSections = settingsSections.ToList();
    }

    public IReadOnlyList<ISourcePageContribution> PagesFor(SourceId source) =>
        _pages.Where(p => p.Source == source).ToList();

    public IReadOnlyList<ISourceSettingsSectionContribution> SettingsSectionsFor(SourceId source) =>
        _settingsSections.Where(s => s.Source == source).ToList();
}

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

    /// <summary>Shell route the module registered (e.g. <c>"SomeSourceInsightsPage"</c>), navigated via <see cref="MostaqlK.Core.Navigation.AppRoute"/>.</summary>
    public string Route { get; }

    /// <summary>Rail label in Arabic.</summary>
    public string TitleAr { get; }

    /// <summary>Single-glyph rail icon, same convention as <c>ISourceDescriptor.RailGlyph</c>.</summary>
    public string Glyph { get; }

    /// <summary>Order among the active source's own contributions (standard nav items always lead).</summary>
    public int Order { get; }
}

/// <summary>
/// The settings rows ONE source contributes (plan decision 6: sources grow independently).
/// <see cref="Adds"/> introduces rows this source owns — its module-local keys stay module-local —
/// and <see cref="Overrides"/> patches fields of an existing row (a label, a bound) without
/// restating it. <c>SettingsCatalog.Compose</c> folds both onto the base layer, so the settings page
/// renders whatever the active source contributes with zero shared-code edits.
/// </summary>
public interface ISourceSettingsContribution
{
    public SourceId Source { get; }

    public IReadOnlyList<SettingsDescriptor> Adds { get; }

    public IReadOnlyList<SettingsDescriptorPatch> Overrides { get; }
}

/// <summary>
/// Index of all registered per-source contributions, keyed like <see cref="SourceRegistry"/>.
/// Built from DI collections, so adding a contribution is only a module-side registration —
/// zero shared-code edits (the independent-growth gate in the v3 contract §10).
/// </summary>
public sealed class SourceContributionRegistry
{
    private readonly IReadOnlyList<ISourcePageContribution> _pages;
    private readonly IReadOnlyList<ISourceSettingsContribution> _settings;

    public SourceContributionRegistry(
        IEnumerable<ISourcePageContribution> pages,
        IEnumerable<ISourceSettingsContribution> settings)
    {
        _pages = pages.OrderBy(p => p.Order).ToList();
        _settings = settings.ToList();
    }

    public IReadOnlyList<ISourcePageContribution> PagesFor(SourceId source) =>
        _pages.Where(p => p.Source == source).ToList();


    public IReadOnlyList<ISourceSettingsContribution> SettingsContribsFor(SourceId source) =>
        _settings.Where(s => s.Source == source).ToList();
}

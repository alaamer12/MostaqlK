using Microsoft.Extensions.DependencyInjection;
using MostaqlK.Core.Sources;
using MostaqlK.Sources.Nafezly.Contributions;

namespace MostaqlK.Sources.Nafezly;

/// <summary>
/// Composition entry point for the nafezly source. Everything nafezly-specific the container knows
/// about is registered here and nowhere else (per-source module rule in structure.md). Registering
/// the descriptor + discovery + enrichment is all it takes for the pipeline orchestrator to build a
/// full nafezly engine; the contributed settings section is the first consumer of the contribution
/// seam (backend-ui-contract §10).
/// </summary>
public sealed class NafezlyModule : ISourceModule
{
    public ISourceDescriptor Descriptor => NafezlySource.Instance;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<NafezlyFetcher>();
        // NafezlyListingStrategy / NafezlyDetailStrategy are stateless and constructed directly by
        // the discovery/enrichment services below — deliberately NOT registered as the shared
        // IListingStrategy / IDetailStrategy interfaces. A second source registering the same
        // interface would, under DI's last-wins single injection, cross-wire MOSTAQL's discovery onto
        // NAFEZLY's parser. The strategies are module-internal seams (shared code only ever sees the
        // ISourceDiscovery / ISourceEnrichment collections), so isolation is enforced by not
        // registering them here at all.
        services.AddSingleton<ISourceDiscovery, NafezlyDiscovery>();
        services.AddSingleton<ISourceEnrichment, NafezlyEnrichment>();
        // First real consumer of the contribution seam: one per-source settings section.
        services.AddSingleton<ISourceSettingsSectionContribution, NafezlySettingsSectionContribution>();
    }
}

public static class NafezlySourceRegistration
{
    /// <summary>Registers the nafezly source module (descriptor + services). Idempotent per app build.</summary>
    public static IServiceCollection AddNafezlySource(this IServiceCollection services)
    {
        services.AddSingleton<ISourceDescriptor>(NafezlySource.Instance);
        services.AddSingleton<ISourceModule, NafezlyModule>();
        new NafezlyModule().Register(services);
        return services;
    }
}

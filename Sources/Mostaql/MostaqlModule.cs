using Microsoft.Extensions.DependencyInjection;
using MostaqlK.Core.Sources;

namespace MostaqlK.Sources.Mostaql;

/// <summary>
/// Composition entry point for the Mostaql source. Everything Mostaql-specific the
/// container knows about is registered here and nowhere else.
/// </summary>
public sealed class MostaqlModule : ISourceModule
{
    public ISourceDescriptor Descriptor => MostaqlSource.Instance;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<MostaqlFetcher>();
        services.AddSingleton<IListingStrategy, MostaqlListingStrategy>();
        services.AddSingleton<IDetailStrategy, MostaqlDetailStrategy>();
        services.AddSingleton<ISourceDiscovery, MostaqlDiscovery>();
        services.AddSingleton<ISourceEnrichment, MostaqlEnrichment>();
    }
}

public static class MostaqlSourceRegistration
{
    /// <summary>Registers the Mostaql source module (descriptor + services). Idempotent per app build.</summary>
    public static IServiceCollection AddMostaqlSource(this IServiceCollection services)
    {
        services.AddSingleton<ISourceDescriptor>(MostaqlSource.Instance);
        services.AddSingleton<ISourceModule, MostaqlModule>();
        new MostaqlModule().Register(services);
        return services;
    }
}

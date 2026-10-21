using Microsoft.Extensions.DependencyInjection;

namespace MostaqlK.Core.Sources;

/// <summary>
/// A source's composition entry point: carries its descriptor and registers its
/// own services (discovery, enrichment, strategies). Adding a website to the app
/// means writing one module and one <c>builder.Services.AddXxxSource()</c> line —
/// nothing in shared code changes.
/// </summary>
public interface ISourceModule
{
    ISourceDescriptor Descriptor { get; }

    void Register(IServiceCollection services);
}

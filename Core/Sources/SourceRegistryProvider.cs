namespace MostaqlK.Core.Sources;

/// <summary>
/// Resolution point for the live <see cref="SourceRegistry"/> from types that are constructed
/// without DI (data-bound view-models, content-view components). Same container-instance
/// lookup the radar/dashboard already use; returns <c>null</c> only in design-time hosts, so
/// callers must degrade to a neutral placeholder, never to a hardcoded site.
/// </summary>
public static class SourceRegistryProvider
{
    public static SourceRegistry? Current =>
        IPlatformApplication.Current?.Services?.GetService<SourceRegistry>();

    public static ISourceDescriptor? DescriptorFor(SourceId source)
    {
        var registry = Current;
        return registry is not null && registry.TryGet(source, out var descriptor) ? descriptor : null;
    }
}

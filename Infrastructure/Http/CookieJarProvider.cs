using MostaqlK.Core.Sources;

namespace MostaqlK.Infrastructure.Http;

/// <summary>
/// Hands out the <see cref="ICookieJar"/> for a given source so request-building code (the fetcher,
/// the asset downloader) never stores a site's session or reaches for a global one. One jar is
/// created per registered source and reused, keeping sessions isolated between sources.
/// </summary>
public interface ICookieJarProvider
{
    /// <summary>The cookie jar for <paramref name="source"/>; throws if that source is not registered.</summary>
    public ICookieJar For(SourceId source);
}

/// <inheritdoc cref="ICookieJarProvider"/>
public sealed class CookieJarProvider : ICookieJarProvider
{
    private readonly Dictionary<SourceId, ICookieJar> _jars = new();

    public CookieJarProvider(IEnumerable<ISourceDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            _jars[descriptor.Id] = new CookieJar(descriptor);
        }
    }

    [Core.ErrorOutcome(Core.ErrorOutcome.Rethrown, Label = "Unregistered source id throws KeyNotFoundException")]
    public ICookieJar For(SourceId source) => _jars.TryGetValue(source, out var jar)
        ? jar
        : throw new KeyNotFoundException($"No cookie jar registered for source '{source}'.");
}

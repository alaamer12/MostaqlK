using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Database;
using MostaqlK.Infrastructure.Http;

namespace MostaqlK.Services;

/// <summary>
/// Hands out the <see cref="CookieStore"/> for a given source so session management (the Settings
/// cookie card) and the fetcher/asset downloader never share one store across sources. One store is
/// created per registered <see cref="ISourceDescriptor"/> and reused, exactly mirroring
/// <see cref="ICookieJarProvider"/>: a store is bound to its own descriptor's <c>CookieSecretKey</c>
/// (and legacy key) and installs its decrypted header into that source's <see cref="ICookieJar"/>
/// only. The Settings page resolves the ACTIVE source's store, so switching the rail morphs the
/// cookie card to manage the other session (backend-ui-contract §5/§10).
/// </summary>
public interface ICookieStoreProvider
{
    /// <summary>The session store for <paramref name="source"/>; throws if that source is not registered.</summary>
    public CookieStore For(SourceId source);

    /// <summary>Every registered store (one per source), for startup initialization.</summary>
    public IReadOnlyCollection<CookieStore> Stores { get; }

    /// <summary>
    /// Loads every source's stored session into its jar, so the first poll cycle is already
    /// authenticated for each pipeline rather than silently anonymous until Settings is opened.
    /// Runs the per-source loads concurrently; each store logs its own read/migration failures and
    /// stays anonymous on error, so one source cannot fail the others.
    /// </summary>
    public Task InitializeAllAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICookieStoreProvider"/>
public sealed class CookieStoreProvider : ICookieStoreProvider
{
    private readonly Dictionary<SourceId, CookieStore> _stores = new();

    public CookieStoreProvider(IEnumerable<ISourceDescriptor> descriptors, ISecretRepository secrets, ICookieJarProvider cookieJars)
    {
        foreach (var descriptor in descriptors)
        {
            _stores[descriptor.Id] = new CookieStore(secrets, descriptor, cookieJars);
        }
    }

    public IReadOnlyCollection<CookieStore> Stores => _stores.Values.ToList();

    [Core.ErrorOutcome(Core.ErrorOutcome.Rethrown, Label = "Unregistered source id throws KeyNotFoundException")]
    public CookieStore For(SourceId source) => _stores.TryGetValue(source, out var store)
        ? store
        : throw new KeyNotFoundException($"No cookie store registered for source '{source}'.");

    public Task InitializeAllAsync(CancellationToken cancellationToken = default)
        => Task.WhenAll(_stores.Values.Select(store => store.InitializeAsync(cancellationToken)));
}

using MostaqlK.Core.Sources;

namespace MostaqlK.Core.Platform;

/// <summary>
/// <see cref="ISettingsRepository"/> backed by <see cref="FilePreferences"/> containers: each
/// source's per-source keys live in a container whose <c>sharedName</c> is the descriptor's
/// <see cref="ISourceDescriptor.SettingsContainerId"/>, while deliberately-global keys (dark mode,
/// startup, polling-active) and the pre-namespacing fallback keep using the flat (default)
/// container exactly as before.
/// <para>
/// Placed in <c>Core/Platform</c> next to the <see cref="FilePreferences"/> it wraps and
/// <see cref="AppPaths"/> it ultimately persists through: like those it is a platform storage
/// primitive, not a business service, and it depends only on the source contracts plus MAUI's
/// <see cref="Microsoft.Maui.Storage.IPreferences"/>.
/// </para>
/// <para>
/// Existing installs need no migration step: a namespaced read that misses falls through to the
/// old flat key and returns whatever is stored there, and the first write moves the value into the
/// per-source container (lazy migration). Data is never lost or rewritten behind the user's back on
/// a read.
/// </para>
/// </summary>
public sealed class FilePreferencesSettingsRepository : ISettingsRepository
{
    private readonly Microsoft.Maui.Storage.IPreferences _preferences;
    private readonly SourceRegistry _registry;

    public FilePreferencesSettingsRepository(Microsoft.Maui.Storage.IPreferences preferences, SourceRegistry registry)
    {
        _preferences = preferences;
        _registry = registry;
    }

    public T Get<T>(SourceId source, string key, T fallback)
    {
        var container = _registry.Get(source).SettingsContainerId;

        // Namespaced value wins once the user has touched the setting.
        if (_preferences.ContainsKey(key, container))
        {
            return _preferences.Get(key, fallback, container);
        }

        // Read-through: fall back to the legacy flat (global) key so an existing install keeps the
        // value it already has, untouched, until the first write migrates it.
        if (_preferences.ContainsKey(key))
        {
            return _preferences.Get(key, fallback);
        }

        return fallback;
    }

    public void Set<T>(SourceId source, string key, T value)
    {
        // Writing only the namespaced key is the lazy half of the migration: the legacy flat copy
        // becomes dead data the next read ignores (the namespaced hit wins), with no separate step.
        _preferences.Set(key, value, _registry.Get(source).SettingsContainerId);
    }

    public bool Contains(SourceId source, string key)
    {
        var container = _registry.Get(source).SettingsContainerId;
        return _preferences.ContainsKey(key, container) || _preferences.ContainsKey(key);
    }

    public T GetGlobal<T>(string key, T fallback) => _preferences.Get(key, fallback);

    public void SetGlobal<T>(string key, T value) => _preferences.Set(key, value);

    public bool ContainsGlobal(string key) => _preferences.ContainsKey(key);
}

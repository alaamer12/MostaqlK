namespace MostaqlK.Core.Sources;

/// <summary>
/// The ONE seam that turns a setting's <see cref="SettingsScope"/> into a storage container:
/// <see cref="SettingsScope.Global"/> is the flat container, <see cref="SettingsScope.PerSource"/>
/// is the source's namespaced one. Before this, every caller picked <c>Get</c> vs <c>GetGlobal</c>
/// by hand, which is how a row could say "on all platforms" in its copy while its value lived in one
/// platform's container (<c>settings_notification_grouping_enabled</c>) — and how a scope change in
/// the catalog silently left a reader on the old container.
/// <para>
/// Callers that hold a <see cref="SettingsDescriptor"/> pass it; callers that only hold a key pass
/// the scope they mean explicitly, and <c>TestSettingsCallSiteScopesMatchCatalog</c> fails if that
/// literal ever disagrees with the catalog. The descriptor is the authority; this is the only path
/// that writes.
/// </para>
/// </summary>
public static class ScopedSettings
{
    public static T ReadScoped<T>(this ISettingsRepository repository, SettingsScope scope, SourceId source, string key, T fallback) =>
        scope switch
        {
            SettingsScope.Global => repository.GetGlobal(key, fallback),
            _ => repository.Get(source, key, fallback),
        };

    public static T ReadScoped<T>(this ISettingsRepository repository, SettingsDescriptor descriptor, SourceId source, T fallback) =>
        repository.ReadScoped(descriptor.Scope, source, descriptor.Key, fallback);

    /// <summary>Immediate (non-deferred) write. UI keystroke paths use <c>DeferredPreferences.SetScoped</c> instead.</summary>
    public static void SaveScoped<T>(this ISettingsRepository repository, SettingsScope scope, SourceId source, string key, T value)
    {
        if (scope == SettingsScope.Global)
        {
            repository.SetGlobal(key, value);
        }
        else
        {
            repository.Set(source, key, value);
        }
    }

    public static void SaveScoped<T>(this ISettingsRepository repository, SettingsDescriptor descriptor, SourceId source, T value) =>
        repository.SaveScoped(descriptor.Scope, source, descriptor.Key, value);

    /// <summary>
    /// True when the container this scope actually reads from already holds the key. Scope matters
    /// here: <see cref="ISettingsRepository.Contains"/> also reports the legacy flat key, so a
    /// tri-state seed (dark mode: "unset" vs "set to the default") must ask through the same scope
    /// the value is read through, or it answers a question about a different container.
    /// </summary>
    public static bool HasValueScoped(this ISettingsRepository repository, SettingsScope scope, SourceId source, string key) =>
        scope switch
        {
            SettingsScope.Global => repository.ContainsGlobal(key),
            _ => repository.Contains(source, key),
        };

    public static bool HasValueScoped(this ISettingsRepository repository, SettingsDescriptor descriptor, SourceId source) =>
        repository.HasValueScoped(descriptor.Scope, source, descriptor.Key);
}

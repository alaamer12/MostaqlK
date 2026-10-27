namespace MostaqlK.Core.Sources;

/// <summary>What the caller asked for and whether the system allowed it.</summary>
/// <param name="Applied">False when the write was refused (the invariant below).</param>
/// <param name="ReasonKey">
/// A stable key the UI maps to Arabic copy — never a user-facing sentence, because the row that owns
/// the copy is a descriptor, not this class.
/// </param>
public readonly record struct PlatformEnablementResult(bool Applied, string? ReasonKey);

/// <summary>
/// Which platforms are switched on, and the one invariant that keeps the app from switching itself
/// out of existence: <b>at least one platform stays enabled.</b>
/// <para>
/// Storage is a single flat key holding the <em>disabled</em> ids
/// (<see cref="SettingsKeys.DisabledPlatforms"/>), not one boolean per platform. The invariant is a
/// property of the SET, so the set is written atomically — a per-platform flag would let a crash
/// between two writes leave every platform off. Absent/empty means every registered platform is
/// enabled, which is also what makes a newly added source enabled by default without touching the
/// key, and what makes an id left behind by a removed source inert instead of fatal.
/// </para>
/// <para>
/// Disabling a platform is <em>network silence</em>, not deletion: its stored projects, its routes
/// and deep links, its feed, filters, archive, unread badge and notification history all keep working.
/// What stops is discovery, detail/asset enrichment and toasts — and the queue holds its items so
/// re-enabling resumes them. That ruling lives here and in
/// <c>.repertoire/.steering/v1/product/configuration-reference.md</c>, and nowhere else.
/// </para>
/// </summary>
public sealed class PlatformEnablement
{
    private const char Separator = ',';
    public const string ReasonLastEnabledPlatform = "last-enabled-platform";

    private readonly ISettingsRepository _settings;
    private readonly SourceRegistry _registry;

    public PlatformEnablement(ISettingsRepository settings, SourceRegistry registry)
    {
        _settings = settings;
        _registry = registry;
    }

    /// <summary>Every registered platform id, in registry order — the rows the settings switcher renders.</summary>
    public IReadOnlyList<SourceId> AllSources => _registry.All.Select(descriptor => descriptor.Id).ToList();

    public bool IsEnabled(SourceId source) => !ReadDisabled().Contains(source);

    /// <summary>The single enabled platform when the user has one left, else null.</summary>
    public SourceId? LockedSource
    {
        get
        {
            var enabled = AllSources.Where(IsEnabled).ToList();
            return enabled.Count == 1 ? enabled[0] : null;
        }
    }

    /// <summary>
    /// Enables or disables one platform. Refuses the write that would leave zero enabled platforms —
    /// the UI also renders that switch locked, so this is the belt for a stale view or a second window.
    /// </summary>
    public PlatformEnablementResult TrySetEnabled(SourceId source, bool enabled)
    {
        var disabled = ReadDisabled();

        if (!enabled && !disabled.Contains(source))
        {
            if (disabled.Count >= AllSources.Count - 1)
            {
                return new PlatformEnablementResult(false, ReasonLastEnabledPlatform);
            }

            disabled.Add(source);
        }
        else if (enabled)
        {
            disabled.Remove(source);
        }
        else
        {
            return new PlatformEnablementResult(true, null); // already disabled: nothing to write
        }

        _settings.SetGlobal(SettingsKeys.DisabledPlatforms, string.Join(Separator, disabled.Select(id => id.Value).Order(StringComparer.Ordinal)));
        return new PlatformEnablementResult(true, null);
    }

    private HashSet<SourceId> ReadDisabled()
    {
        var raw = _settings.GetGlobal(SettingsKeys.DisabledPlatforms, string.Empty);
        var known = new HashSet<SourceId>(AllSources);
        var disabled = new HashSet<SourceId>();

        foreach (var token in raw.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Unknown ids are skipped, not fatal: a platform that no longer exists must not take the
            // settings page (or the pipeline) down, and a typo in this key is invisible to users.
            if (SourceId.TryFrom(token, out var source) && known.Contains(source))
            {
                disabled.Add(source);
            }
        }

        return disabled;
    }
}

using MostaqlK.Core.Sources;

namespace MostaqlK.Features.Settings;

/// <summary>
/// One-time harvest of the values that moved from a platform's container to the system's.
/// <para>
/// <see cref="ISettingsRepository"/> reads a per-source key as "namespaced container, then the legacy
/// flat key" — that read-through is what made the original flat→container move need no migration step.
/// The opposite direction has no such fallback: <c>GetGlobal</c> only ever looks at the flat
/// container, so a row that becomes <see cref="SettingsScope.Global"/> would silently start answering
/// with its default while the value the user actually set sat unread in a platform container. This
/// copies those values across exactly once, before anything reads them.
/// </para>
/// <para>
/// Precedence, in the order that decides: an explicit global value always wins (nothing is overwritten);
/// otherwise the active platform's container, because that is the value the user last saw on screen;
/// otherwise any other platform that has one, so a setting tuned on a single platform is not lost just
/// because the user happened to be on another when this build first ran. The first launch stamps the
/// marker, so the decision is never re-made — and the losing containers stay on disk untouched, because
/// deleting a user's stored values is not this class's call to make.
/// </para>
/// </summary>
public static class SettingsScopeHarvest
{
    /// <summary>Stamped once the harvest has run; its presence is the whole "did this already happen".</summary>
    public const string MarkerKey = "settings_scope_harvest_v1";

    /// <summary>
    /// The keys that moved <see cref="SettingsScope.PerSource"/> → <see cref="SettingsScope.Global"/>
    /// with the System/Platform split, each with the CLR kind the repository must read it as
    /// (<see cref="IPreferences"/>-style generic reads need the type; the catalog's
    /// <see cref="SettingsValueKind"/> is where the row declares it).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, SettingsValueKind> MovedToSystem =
        new Dictionary<string, SettingsValueKind>(StringComparer.Ordinal)
        {
            [SettingsKeys.PollIntervalSeconds] = SettingsValueKind.Int,
            [SettingsKeys.SafeRequests] = SettingsValueKind.Bool,
            [SettingsKeys.MaxConcurrentDetailFetches] = SettingsValueKind.Int,
            [SettingsKeys.NotificationGroupingEnabled] = SettingsValueKind.Bool,
            [SettingsKeys.GroupingMode] = SettingsValueKind.Enum,
            [SettingsKeys.GroupingThreshold] = SettingsValueKind.Int,
        };

    public static bool ShouldRun(ISettingsRepository settings) => !settings.ContainsGlobal(MarkerKey);

    /// <summary>
    /// Runs the harvest and returns what moved ("key&lt;source"), for the startup log. Idempotent: the
    /// marker is stamped on the first call, and a key that already has a global value is skipped even
    /// when the marker is absent (a user who changed the system row before this ran wins).
    /// </summary>
    public static IReadOnlyList<string> Run(
        ISettingsRepository settings,
        SourceId active,
        IReadOnlyList<SourceId> allSources)
    {
        var moved = new List<string>();

        foreach (var (key, kind) in MovedToSystem)
        {
            if (settings.ContainsGlobal(key))
            {
                continue;
            }

            foreach (var source in OrderFirst(active, allSources))
            {
                // Contains() also reports the legacy flat key, but ContainsGlobal(key) was just false,
                // so a hit here can only have come from this source's own container.
                if (!settings.Contains(source, key))
                {
                    continue;
                }

                settings.SetGlobal(key, ReadAs(settings, kind, source, key));
                moved.Add($"{key}<{source.Value}");
                break;
            }
        }

        settings.SetGlobal(MarkerKey, true);
        return moved;
    }

    /// <summary>Active platform first, then every other registered source in registry order.</summary>
    private static IEnumerable<SourceId> OrderFirst(SourceId active, IReadOnlyList<SourceId> allSources)
    {
        yield return active;
        foreach (var source in allSources)
        {
            if (source != active)
            {
                yield return source;
            }
        }
    }

    private static object ReadAs(ISettingsRepository settings, SettingsValueKind kind, SourceId source, string key) =>
        kind switch
        {
            SettingsValueKind.Int => settings.Get(source, key, 0),
            SettingsValueKind.Bool => settings.Get(source, key, false),
            // Grouping mode is stored as the enum NAME (nameof(NotificationGroupingMode.…)), i.e. a string.
            _ => settings.Get(source, key, string.Empty),
        };
}

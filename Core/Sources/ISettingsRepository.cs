namespace MostaqlK.Core.Sources;

/// <summary>
/// Per-source settings access. Reads fall back to the legacy flat (global) key so
/// existing installs keep their values untouched, and migrate lazily on first write.
/// Implementation wraps FilePreferences containers (sharedName = descriptor's
/// SettingsContainerId).
/// </summary>
public interface ISettingsRepository
{
    public T Get<T>(SourceId source, string key, T fallback);

    public void Set<T>(SourceId source, string key, T value);

    /// <summary>Global (not per-source) access — dark mode, startup, etc.</summary>
    public T GetGlobal<T>(string key, T fallback);

    public void SetGlobal<T>(string key, T value);
}

/// <summary>
/// Key names shared between the settings UI and the pipeline. The <c>settings_*</c>
/// values equal the legacy flat Preferences keys so the read-through fallback finds
/// existing installs' data; only their storage location becomes per-source.
/// </summary>
public static class SettingsKeys
{
    public const string PollIntervalSeconds = "settings_poll_interval_seconds";
    public const string MaxRequestsPerMinute = "settings_max_requests_per_minute";
    public const string SafeRequests = "settings_safe_requests";
    public const string QueryParams = "settings_query_params";
    public const string IncludeAssets = "settings_include_assets";
    public const string MaxConcurrentDetailFetches = "settings_max_concurrent_detail_fetches";
    public const string NotificationGroupingEnabled = "settings_notification_grouping_enabled";
    public const string GroupingMode = "settings_grouping_mode";
    public const string GroupingThreshold = "settings_grouping_threshold";

    // Deliberately global (app behavior, not per-source):
    public const string IsDarkMode = "settings_is_dark_mode";
    public const string StartupEnabled = "settings_startup_enabled";
    public const string IsPollingActive = "settings_is_polling_active";
}

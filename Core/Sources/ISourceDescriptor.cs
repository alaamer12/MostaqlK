namespace MostaqlK.Core.Sources;

/// <summary>
/// Everything the rest of the app is allowed to know about one monitored website:
/// identity strings, URL construction, defaults, and capability flags. Shared code
/// (pipeline, UI, settings) reads this instead of hardcoding a site; the concrete
/// implementations live inside <c>Sources/&lt;Name&gt;/</c> modules.
/// </summary>
public interface ISourceDescriptor
{
    public SourceId Id { get; }

    /// <summary>Arabic display name used in labels and toasts ("مستقل").</summary>
    public string DisplayNameAr { get; }

    /// <summary>Host without scheme ("mostaql.com"); also the cookie domain filter.</summary>
    public string Domain { get; }

    /// <summary>Single-letter rail tile glyph ("م").</summary>
    public string RailGlyph { get; }

    public BrandTokenSet Brands { get; }

    public int DefaultPollIntervalSeconds { get; }

    public int DefaultRequestsPerMinute { get; }

    /// <summary>Suffix appended to parsed budgets ("ر.س"); currency is a source fact.</summary>
    public string CurrencySuffix { get; }

    /// <summary>
    /// Owner meta shown when a source's listing has no scraped owner location/join-year.
    /// Today this is the fabricated Mostaql card string — kept as data so the UI stays
    /// source-blind until real per-source owner fields arrive.
    /// </summary>
    public string? OwnerFallbackMeta { get; }

    public SourceCapabilities Capabilities { get; }

    /// <summary>Key into <c>app_secrets</c> for this source's session cookie ("cookie_mostaql").</summary>
    public string CookieSecretKey { get; }

    /// <summary>Preferences container (FilePreferences sharedName) holding this source's settings.</summary>
    public string SettingsContainerId { get; }

    /// <summary>The projects-listing URL this source polls, with the source-shaped query appended.</summary>
    public string BuildListingUrl(string? queryParams);

    /// <summary>Absolute project URL from the site's own numeric id.</summary>
    public string BuildProjectUrl(long siteProjectId);

    /// <summary>Prefixed with this site's scheme+host when the parser found a root-relative href.</summary>
    public string BuildAbsoluteUrl(string pathOrUrl);

    /// <summary>True when the URL points at this site (replaces <c>Contains("mostaql.com")</c> sniffs).</summary>
    public bool IsSameSiteUrl(string url);
}

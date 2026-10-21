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

    /// <summary>Arabic display name used in labels and toasts — the site's own name, never hardcoded by callers.</summary>
    public string DisplayNameAr { get; }

    /// <summary>
    /// Latin-script transliteration of the site's name for bilingual user copy (e.g. a section
    /// header that shows the Arabic name plus "(Xxx Session Cookie)"). UI text composes this
    /// through the descriptor instead of embedding a site name.
    /// </summary>
    public string DisplayNameLatin { get; }

    /// <summary>Host without scheme (e.g. "example.com"); also the cookie domain filter.</summary>
    public string Domain { get; }

    /// <summary>Single-letter rail tile glyph ("م").</summary>
    public string RailGlyph { get; }

    public BrandTokenSet Brands { get; }

    public int DefaultPollIntervalSeconds { get; }

    public int DefaultRequestsPerMinute { get; }

    /// <summary>Suffix appended to parsed budgets; currency is a source fact.</summary>
    public string CurrencySuffix { get; }

    /// <summary>
    /// Owner meta shown when a source's listing has no scraped owner location/join-year.
    /// Today this is the fabricated Mostaql card string — kept as data so the UI stays
    /// source-blind until real per-source owner fields arrive.
    /// </summary>
    public string? OwnerFallbackMeta { get; }

    public SourceCapabilities Capabilities { get; }

    /// <summary>Key into <c>app_secrets</c> for this source's session cookie.</summary>
    public string CookieSecretKey { get; }

    /// <summary>
    /// The pre-namespacing <c>app_secrets</c> key this source used before sessions became
    /// per-source. <see cref="CookieStore"/> reads it when the new key is
    /// absent and rewrites the value under <see cref="CookieSecretKey"/>, so an existing user's
    /// uploaded session keeps working with no re-upload. It is only ever a migration input.
    /// </summary>
    public string LegacyCookieSecretKey { get; }

    /// <summary>
    /// DEBUG-only inline cookie env var kept working for backward compatibility with the old
    /// developer workflow. The primary env var is derived generically from the
    /// source id (<c>MOSTAQLK_COOKIE_{SOURCE}</c>); this is the legacy alias, so it belongs to the
    /// module rather than the shared HTTP plumbing.
    /// </summary>
    public string LegacyCookieEnvInlineVar { get; }

    /// <summary>
    /// DEBUG-only cookie-file env var kept working as the legacy alias for older dev exports.
    /// The primary env var is <c>MOSTAQLK_COOKIE_{SOURCE}_FILE</c>.
    /// </summary>
    public string LegacyCookieEnvFileVar { get; }

    /// <summary>Preferences container (FilePreferences sharedName) holding this source's settings.</summary>
    public string SettingsContainerId { get; }

    /// <summary>
    /// <c>User-Agent</c> sent to this site. Mostaql sits behind a bot filter that 403s a header-less
    /// request before producing HTML, so this must be a real browser UA; it describes what the
    /// SCRAPED SITE sees and is therefore intentionally identical on every OS the app runs on.
    /// </summary>
    public string UserAgent { get; }

    /// <summary><c>Accept</c> request header for this source's HTML pages.</summary>
    public string AcceptHeader { get; }

    /// <summary><c>Accept-Language</c> request header for this source's HTML pages.</summary>
    public string AcceptLanguageHeader { get; }

    /// <summary>
    /// Offset added to a site-local numeric id to get the app's globally-unique internal
    /// row id (projects/owners/backlog keys). 0 means "internal == site id" — Mostaql keeps
    /// it so every pre-v2 row, child-table reference and FTS rowid stays valid untouched.
    /// Later sources get a band so high that their ids can never collide with a Mostaql id.
    /// </summary>
    public long InternalIdBand { get; }

    /// <summary>The projects-listing URL this source polls, with the source-shaped query appended.</summary>
    public string BuildListingUrl(string? queryParams);
    /// <summary>True when the URL points at this site (replaces hardcoded host-<c>Contains</c> sniffs in shared code).</summary>
    /// <summary>Absolute project URL from the site's own numeric id.</summary>
    public string BuildProjectUrl(long siteProjectId);

    /// <summary>Prefixed with this site's scheme+host when the parser found a root-relative href.</summary>
    public string BuildAbsoluteUrl(string pathOrUrl);

    /// <summary>True when the URL points at this site (replaces hardcoded host-name <c>Contains</c> sniffs in shared code).</summary>
    public bool IsSameSiteUrl(string url);
}

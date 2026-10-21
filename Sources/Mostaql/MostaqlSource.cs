using MostaqlK.Core.Sources;

namespace MostaqlK.Sources.Mostaql;

/// <summary>
/// Every Mostaql-specific fact the shared app is allowed to know: identity strings,
/// URLs, defaults, capabilities. Bodies that read these never branch on the source
/// name — they just use what the descriptor says.
/// </summary>
public sealed class MostaqlSource : ISourceDescriptor
{
    public static MostaqlSource Instance { get; } = new();

    public SourceId Id => SourceId.Mostaql;

    public string DisplayNameAr => "مستقل";
    public string DisplayNameLatin => "Mostaql";

    public string Domain => "mostaql.com";

    public string RailGlyph => "م";

    // Seeded to the exact colors the app renders today (DesignTokens/inline hexes);
    // the UI phase will swap in the real per-source brand palette.
    public BrandTokenSet Brands { get; } = new("#2386C8", "#1A6DA6", "#EFF6FB", "#ffffff", "#2563EB", "#60A5FA");

    public int DefaultPollIntervalSeconds => 30;

    public int DefaultRequestsPerMinute => 2;

    public string CurrencySuffix => "ر.س";

    public string? OwnerFallbackMeta => "السعودية  •  عضو منذ 2021";

    public SourceCapabilities Capabilities { get; } = new(
        HasExecutorInfo: false,
        HasSupervisedFlag: false,
        NeedsSessionForFullEnrichment: true);

    public string CookieSecretKey => "cookie_mostaql";

    // Name of the site's own session cookie (Laravel default wiring); lets Settings wrap a bare
    // pasted value into a valid header. Source fact, so it lives here, not in shared UI.
    public string? SessionCookieName => "mostaql_session";

    // Pre-namespacing secret key (see ISourceDescriptor.LegacyCookieSecretKey): the row an older
    // build wrote. CookieStore rewrites it under CookieSecretKey on first load, then stops using it.
    public string LegacyCookieSecretKey => "mostaql_cookie";

    // Legacy DEBUG env aliases for the developer cookie workflow; the primary names are derived
    // generically from the source id (MOSTAQLK_COOKIE_MOSTAQL / _FILE).
    public string LegacyCookieEnvInlineVar => "MOSTAQL_COOKIE";

    public string LegacyCookieEnvFileVar => "MOSTAQL_COOKIE_FILE";

    public string SettingsContainerId => "mostaql";

    // mostaql.com sits behind a bot filter that answers a *header-less* request with HTTP 403
    // before any HTML is produced. A bare HttpClient sends no User-Agent at all, so every poll
    // cycle failed at the listing fetch - verified directly against the endpoint: no User-Agent
    // => 403 (118 bytes), a normal browser User-Agent => 200 (~165 KB). This UA impersonates a
    // Windows desktop Chrome to satisfy that filter; it describes what the SCRAPED SITE sees, not
    // the OS this app runs on, so it is intentionally identical on every target (Android included)
    // and does not need per-platform extraction.
    public string UserAgent =>
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    public string AcceptHeader => "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    public string AcceptLanguageHeader => "ar,en-US;q=0.9,en;q=0.8";

    // Internal row ids stay equal to mostaql.com's own project/owner ids, so every
    // pre-v2 DB row, child-table reference and FTS rowid remains valid without rewriting.
    public long InternalIdBand => 0;

    public string BuildListingUrl(string? queryParams)
    {
        var url = "https://mostaql.com/projects";
        if (!string.IsNullOrWhiteSpace(queryParams))
        {
            var normalized = queryParams.Trim();
            if (!normalized.StartsWith('?'))
            {
                normalized = "?" + normalized;
            }

            url += normalized;
        }

        return url;
    }

    public string BuildProjectUrl(long siteProjectId) => $"https://mostaql.com/project/{siteProjectId}";

    public string BuildAbsoluteUrl(string pathOrUrl) =>
        pathOrUrl.StartsWith('/') ? "https://mostaql.com" + pathOrUrl : pathOrUrl;

    public bool IsSameSiteUrl(string url) =>
        url.Contains("mostaql.com", StringComparison.OrdinalIgnoreCase);
}

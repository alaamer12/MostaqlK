using MostaqlK.Core.Parsing;
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

    // The site's host, stated once for the whole module: every URL this class builds and every
    // parser absolutization goes through these two constants.
    private const string Host = "mostaql.com";
    private const string SiteRoot = "https://" + Host;

    public string Domain => Host;

    public string RailGlyph => "م";

    public string? RailIconImageName => "icon_mostaql";

    // Seeded to the exact colors the app renders today (DesignTokens/inline hexes);
    // the UI phase will swap in the real per-source brand palette.
    public BrandTokenSet Brands { get; } = new("#2386C8", "#1A6DA6", "#EFF6FB", "#ffffff", "#2563EB", "#60A5FA", "#5CA8DE", "#5CA8DE", "#245CA8DE", "#0b1c2a");

    public int DefaultPollIntervalSeconds => 30;

    public int DefaultRequestsPerMinute => 2;

    public string CurrencySuffix => "ر.س";

    public string? OwnerFallbackMeta => "السعودية  •  عضو منذ 2021";

    public SourceCapabilities Capabilities { get; } = new(
        HasExecutorInfo: false,
        HasSupervisedFlag: false,
        NeedsSessionForFullEnrichment: true);

    // Analysis vocabulary and DOM selectors the shared Core/Parsing engines consume for this
    // source (byte-copied from the pre-A1 parser constants; see the data files for provenance).
    public ParseProfile Profile => MostaqlParseProfile.Profile;

    public SelectorMap Selectors => MostaqlSelectors.Selectors;

    /// <summary>
    /// Seeds the CURRENT field order the desktop details/card UI assumes (title + description
    /// first, then the "بطاقة المشروع" meta rows in panel order, skills, attachments, and the
    /// owner card rows top-to-bottom). Schema-driven rendering consumes this in Phase B4;
    /// nothing rewired yet, so behavior is unchanged.
    /// </summary>
    public IReadOnlyList<FieldSpec> SchemaMap { get; } =
    [
        new("title", "summary", 0),
        new("description", "summary", 1),
        new("project_status", "project", 2),
        new("published_date", "project", 3),
        new("budget", "project", 4),
        new("duration", "project", 5),
        new("proposal_count", "project", 6),
        new("skills", "skills", 7),
        new("attachments", "attachments", 8),
        new("owner_name", "owner", 9),
        new("registration_date", "owner", 10),
        new("hire_rate", "owner", 11),
        new("open_projects_count", "owner", 12),
        new("in_progress_count", "owner", 13),
        new("ongoing_conversations", "owner", 14),
    ];

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
        var url = SiteRoot + "/projects";
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

    public string BuildProjectUrl(long siteProjectId) => $"{SiteRoot}/project/{siteProjectId}";

    /// <summary>
    /// Scheme-rooted absolutization for hrefs the parsers found: absolute URLs pass through,
    /// root-relative paths get the host, bare paths get host + separator. Shared between the
    /// module's own static parsers (via <see cref="Instance"/>) and any consumer holding the
    /// descriptor, so every URL this site emits is built one way.
    /// </summary>
    public string BuildAbsoluteUrl(string pathOrUrl) =>
        pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? pathOrUrl
            : SiteRoot + (pathOrUrl.StartsWith('/') ? "" : "/") + pathOrUrl;

    public bool IsSameSiteUrl(string url) =>
        url.Contains(Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Mostaql has no typed facet vocabulary: its filtering today is the raw preset/suffix string
    /// from onboarding/Settings (contract §8), so the shared FilterModal renders nothing for this
    /// source and the string keeps flowing to <see cref="BuildListingUrl"/> untouched.
    /// </summary>
    public IReadOnlyList<FilterFacetSpec> FilterFacets => [];

    /// <summary>
    /// Legacy passthrough (today's behavior IS the spec): mostaql's "selection" is its raw query
    /// string, so <see cref="ParseFacetSelection"/> echoes each '&amp;' segment as an opaque key and
    /// this re-emits exactly those segments — byte-identical for any stored string, including a
    /// leading '?' and values the parser never interprets (verified byte-for-byte in ParserTests).
    /// </summary>
    public string BuildQueryFromFacets(IReadOnlyDictionary<string, IReadOnlyList<string>> selections) =>
        string.Join("&", selections.SelectMany(kv => kv.Value.Select(_ => kv.Key)));

    /// <summary>Opaque echo-parse of the legacy suffix; see <see cref="BuildQueryFromFacets"/>.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ParseFacetSelection(string? queryParams)
    {
        var segments = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var raw = queryParams?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return segments;
        }

        // Split WITHOUT removing empties, so "a&&b" and "a=1&" echo back byte-identically too.
        foreach (var segment in raw.Split('&'))
        {
            segments[segment] = segments.TryGetValue(segment, out var seen)
                ? [.. seen, string.Empty]
                : [string.Empty];
        }

        return segments;
    }
}

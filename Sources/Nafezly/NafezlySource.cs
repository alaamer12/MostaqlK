using MostaqlK.Core.Parsing;
using MostaqlK.Core.Sources;

namespace MostaqlK.Sources.Nafezly;

/// <summary>
/// Every Nafezly-specific fact the shared app is allowed to know: identity strings, URLs,
/// defaults, capabilities, and the parse vocabulary the shared <c>Core/Parsing</c> engines
/// consume for this source. Bodies that read these never branch on the source name — they
/// just use what the descriptor says (backend-ui-contract §1). This mirrors
/// <see cref="Mostaql.MostaqlSource"/> exactly: thin data + URL construction, zero logic.
/// </summary>
public sealed class NafezlySource : ISourceDescriptor
{
    public static NafezlySource Instance { get; } = new();

    public SourceId Id => SourceId.From("nafezly");

    public string DisplayNameAr => "نفذلي";
    public string DisplayNameLatin => "Nafezly";

    // The site's host, stated once for the whole module: every URL this class builds and every
    // parser absolutization goes through these two constants.
    private const string Host = "nafezly.com";
    private const string SiteRoot = "https://" + Host;

    public string Domain => Host;

    public string RailGlyph => "ن";

    // Seeded to the LIGHT per-source brand set the capture manifest/README lock in for nafezly
    // (brand #FFA200, AA-safe ink #9A6100 for ≤14px text, soft wash #FFF4DE, ink-on-brand #3a2a00).
    // The record still carries only the light fields — the four dark counterparts arrive with the
    // B1 hex sweep (backend-ui-contract §3), so this class does NOT extend BrandTokenSet now.
    public BrandTokenSet Brands { get; } = new("#FFA200", "#9A6100", "#FFF4DE", "#3a2a00", "#9A6100", "#FFB84D");

    // Cloudflare rate-limits a burst (> ~6 req/min from one client) with a 429/1015; 60s cadence
    // and 8 rpm keep well under that ceiling (capture-manifest "Rate-limit truth").
    public int DefaultPollIntervalSeconds => 60;

    public int DefaultRequestsPerMinute => 8;

    // Nafezly money is USD; the display strings already carry a trailing "$" ("10 - 25 $"), so the
    // BudgetFormatter suffix stays "$" and renders LTR-isolated inside the RTL page (§7).
    public string CurrencySuffix => "$";

    // Nafezly renders a real owner name + country per project, so unlike Mostaql there is no
    // fabricated fallback card string — leave it null and let the UI use the scraped owner.
    public string? OwnerFallbackMeta => null;

    // Executor name is present in the anonymous HTML (behind the app-gate for the full profile),
    // so the executor UI section is gated on this. The supervised/verified facet is NOT server-
    // rendered for anonymous sessions (capture-manifest §1.10) → false. Full enrichment does not
    // need a session: title/budget/state/skills/offers/owner all arrive anonymously.
    public SourceCapabilities Capabilities { get; } = new(
        HasExecutorInfo: true,
        HasSupervisedFlag: false,
        NeedsSessionForFullEnrichment: false);

    // Analysis vocabulary and DOM selectors the shared Core/Parsing engines consume for this
    // source — derived from the A2 capture manifest, see the data files for provenance.
    public ParseProfile Profile => NafezlyParseProfile.Profile;

    public SelectorMap Selectors => NafezlySelectors.Selectors;

    /// <summary>
    /// Seeds the CURRENT field order nafezly's details/card UI will assume: title + description
    /// first, then the بطاقة المشروع meta rows in panel order (status/published/duration/budget/
    /// proposals), skills, then the poster and (nafezly-only) executor blocks. Schema-driven
    /// rendering consumes this in Phase B4; nothing rewired yet, so behavior is unchanged.
    /// </summary>
    public IReadOnlyList<FieldSpec> SchemaMap { get; } =
    [
        new("title", "summary", 0),
        new("description", "summary", 1),
        new("project_status", "project", 2),
        new("published_date", "project", 3),
        new("duration", "project", 4),
        new("budget", "project", 5),
        new("proposal_count", "project", 6),
        new("skills", "skills", 7),
        new("owner_name", "owner", 8),
        new("executor_name", "executor", 9),
    ];

    public string CookieSecretKey => "cookie_nafezly";

    // Nafezly's session cookie name is not confirmed from the anonymous capture; null means the
    // Settings flow requires a full "Cookie:"-style paste rather than auto-wrapping a bare value.
    public string? SessionCookieName => null;

    // No pre-namespacing secret key existed (nafezly is a brand-new source, never shipped under a
    // flat key). CookieStore only consults this as a migration input, so an empty value = nothing
    // to migrate.
    public string LegacyCookieSecretKey => string.Empty;

    // No legacy developer alias either: the primary env names are derived generically from the
    // source id (MOSTAQLK_COOKIE_NAFEZLY / _FILE), and those are the names the module uses.
    public string LegacyCookieEnvInlineVar => "MOSTAQLK_COOKIE_NAFEZLY";

    public string LegacyCookieEnvFileVar => "MOSTAQLK_COOKIE_NAFEZLY_FILE";

    public string SettingsContainerId => "nafezly";

    // The A2 capture used exactly this browser UA (+ Accept / Accept-Language) and every request
    // returned HTTP 200; a header-less request trips Cloudflare's bot filter. Identical on every
    // OS by design (it describes what the SCRAPED SITE sees, not the host this app runs on).
    public string UserAgent =>
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    public string AcceptHeader => "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    public string AcceptLanguageHeader => "ar,en-US;q=0.9,en;q=0.8";

    // A band high enough that no nafezly site id can ever collide with a mostaql internal id
    // (mostaql keeps band 0). Chosen from the A3 brief.
    public long InternalIdBand => 1_000_000_000_000;

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
    /// root-relative paths get the host, bare paths get host + separator. Nafezly emits absolute
    /// project/skill hrefs today, but this keeps every URL built one way regardless.
    /// </summary>
    public string BuildAbsoluteUrl(string pathOrUrl) =>
        pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? pathOrUrl
            : SiteRoot + (pathOrUrl.StartsWith('/') ? "" : "/") + pathOrUrl;

    public bool IsSameSiteUrl(string url) =>
        url.Contains(Host, StringComparison.OrdinalIgnoreCase);
}

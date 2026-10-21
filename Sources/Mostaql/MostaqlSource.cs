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

    public string Domain => "mostaql.com";

    public string RailGlyph => "م";

    // Seeded to the exact colors the app renders today (DesignTokens/inline hexes);
    // the UI phase will swap in the real per-source brand palette.
    public BrandTokenSet Brands { get; } = new("#2386C8", "#1A6DA6", "#EFF6FB", "#ffffff");

    public int DefaultPollIntervalSeconds => 30;

    public int DefaultRequestsPerMinute => 2;

    public string CurrencySuffix => "ر.س";

    public string? OwnerFallbackMeta => "السعودية  •  عضو منذ 2021";

    public SourceCapabilities Capabilities { get; } = new(
        HasExecutorInfo: false,
        HasSupervisedFlag: false,
        NeedsSessionForFullEnrichment: true);

    public string CookieSecretKey => "cookie_mostaql";

    public string SettingsContainerId => "mostaql";

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

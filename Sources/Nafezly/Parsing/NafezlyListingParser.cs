using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MostaqlK.Core.Formatting;
using MostaqlK.Core.Parsing;
using MostaqlK.Models;

namespace MostaqlK.Sources.Nafezly.Parsing;

/// <summary>
/// Parses the nafezly projects-listing page into <see cref="ProjectSummary"/> rows. This is only the
/// ORDER OF OPERATIONS over the shared <see cref="HtmlStructureEngine"/> text helpers + the
/// nafezly <see cref="NafezlySelectors"/>/<see cref="NafezlyParseProfile"/> data — every row shape
/// and meta-icon signal comes from the A2 capture manifest (§2), so a nafezly redesign that renames
/// a class is a data edit, not a logic change here.
/// </summary>
/// <remarks>
/// Row grammar (verbatim per manifest): the 20 <c>div.project-box</c> cards, each with a title
/// <c>a[href="https://nafezly.com/project/{id}-{slug}"]</c>, an <c>h3.naskh</c> snippet, and a meta
/// column whose facts are icon spans — <c>fa-usd-circle</c> (budget), <c>fa-business-time</c>
/// (duration), <c>fa-ballot</c> (proposals "{n} عروض"), <c>fa-clock</c> (publish "منذ …"),
/// <c>fa-check-circle</c> (state, shown verbatim; state colour is never read into the data).
/// </remarks>
public static class NafezlyListingParser
{
    private static ParseProfile Profile => NafezlyParseProfile.Profile;
    private static SelectorMap S => NafezlySelectors.Selectors;

    // Naive state colours are ignored — the manifest is explicit that only the scraped TEXT is the
    // truth (contract §7); this set only documents the observed vocabulary and a defensive مغلق.
    internal static readonly IReadOnlyList<string> StateVocabulary = ["مفتوح", "تحت التنفيذ", "مكتمل", "مغلق"];

    private static readonly Regex ProjectIdRegex =
        new(S.ProjectIdUrlPattern, RegexOptions.Compiled);

    public static IReadOnlyList<ProjectSummary> Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw NafezlyParseErrors.EmptyHtml(nameof(NafezlyListingParser));
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var summaries = new List<ProjectSummary>();
        var seenIds = new HashSet<long>();

        foreach (var row in HtmlStructureEngine.SelectByClassContains(root, "div", S.ListingRowClass))
        {
            var summary = ParseRow(row);
            if (summary is not null && summary.ProjectId > 0 && seenIds.Add(summary.ProjectId))
            {
                summaries.Add(summary);
            }
        }

        if (summaries.Count == 0)
        {
            // Identifier-blind redesign fallback (same idea as the Mostaql parser): a nafezly
            // redesign may drop the project-box class while keeping canonical /project/{id} links.
            foreach (var link in root.SelectNodes($"//a[contains(@href, '{S.ProjectHrefPathSegment}')]") ?? Enumerable.Empty<HtmlNode>())
            {
                var href = link.GetAttributeValue("href", string.Empty);
                var id = ExtractProjectIdFromUrl(href);
                var title = HtmlStructureEngine.Normalize(HtmlEntity.DeEntitize(link.InnerText));
                if (id <= 0 || string.IsNullOrWhiteSpace(title) || !seenIds.Add(id))
                {
                    continue;
                }

                summaries.Add(new ProjectSummary
                {
                    ProjectId = id,
                    Title = title,
                    Url = href,
                    DiscoveredAt = DateTimeOffset.UtcNow,
                });
            }
        }

        if (summaries.Count == 0)
        {
            throw NafezlyParseErrors.NoProjectRows();
        }

        return summaries;
    }

    private static ProjectSummary? ParseRow(HtmlNode row)
    {
        var titleLink = row.SelectSingleNode(".//a[contains(@href, '/project/')]");
        if (titleLink is null)
        {
            return null;
        }

        var href = titleLink.GetAttributeValue("href", string.Empty);
        var projectId = ExtractProjectIdFromUrl(href);
        var title = HtmlStructureEngine.Normalize(HtmlEntity.DeEntitize(titleLink.InnerText));
        if (projectId <= 0 || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        // Snippet / brief (h3.naskh, truncated to the visible card height).
        var brief = HtmlStructureEngine.Normalize(
            HtmlEntity.DeEntitize(row.SelectSingleNode($".//*[contains(concat(' ', normalize-space(@class), ' '), ' {S.ListingBriefClass} ')]")?.InnerText ?? string.Empty));

        // Meta-column facts, each located by its FontAwesome icon and read from the wrapping text span.
        var budget = MetaIconText(row, "fa-usd-circle");
        var duration = MetaIconText(row, "fa-business-time");
        var proposals = MetaIconText(row, "fa-ballot");
        var publish = MetaIconText(row, "fa-clock");
        var status = MetaIconText(row, "fa-check-circle");

        var (proposalNum, proposalText) = ArabicProposalParser.Parse(proposals);
        var publishNum = IsRelativeTime(publish) ? ArabicRelativeTime.ParseRelativeNumber(publish) : 0;

        return new ProjectSummary
        {
            ProjectId = projectId,
            Title = title,
            Url = href,
            Description = brief,
            Budget = string.IsNullOrEmpty(budget) ? null : budget,
            ProjectStatus = string.IsNullOrEmpty(status) ? null : status,
            ProposalCount = proposalNum,
            ProposalCountText = proposalText ?? proposals,
            PublishTimeNumber = publishNum,
            PublishTimeText = publish,
            // DeliveryDays stays null on the listing (the detail page supplies the authoritative
            // JSON-LD workHours); the duration text is not a structured day count here.
            DeliveryDays = null,
            DiscoveredAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Reads one meta-column fact: the nafezly markup nests an empty icon <c>&lt;span class="fal fa-ballot"&gt;</c>
    /// inside a text <c>&lt;span class="d-inline-block kufi"&gt;… value</c>, so the value is the icon
    /// span's parent text.
    /// </summary>
    private static string MetaIconText(HtmlNode row, string iconClass)
    {
        var icon = row.SelectSingleNode($".//span[contains(@class, '{iconClass}')]");
        var container = icon?.ParentNode;
        if (container is null)
        {
            return string.Empty;
        }

        return HtmlStructureEngine.Normalize(HtmlEntity.DeEntitize(container.InnerText));
    }

    private static bool IsRelativeTime(string text) =>
        !string.IsNullOrEmpty(text) && Profile.RelativeTimeWords.Any(word => text.Contains(word, StringComparison.Ordinal));

    /// <summary>
    /// Nafezly project URLs are "https://nafezly.com/project/{id}-{arabic-slug}". The id is the first
    /// numeric run right after "/project/", never the trailing slug digits — a slug ending in a
    /// number ("…-بالai-…-2024") would otherwise hijack the id (the exact class of bug the shared
    /// Mostaql parser documents). Falls back to the first standalone numeric segment if the prefix
    /// is ever absent.
    /// </summary>
    private static long ExtractProjectIdFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return 0;
        }

        var match = ProjectIdRegex.Match(url);
        if (match.Success && long.TryParse(match.Groups[1].Value, out var matchedId))
        {
            return matchedId;
        }

        var firstSegment = url.TrimEnd('/').Split('/', '-').FirstOrDefault(s => s.Length > 0 && s.All(char.IsDigit));
        return firstSegment is not null && long.TryParse(firstSegment, out var id) ? id : 0;
    }

    /// <summary>
    /// Prev/next pagination (capture-manifest §2): <c>ul.pagination li.page-item a.page-link[rel=prev|next]</c>.
    /// Nafezly's next href echoes the FULL query (specialize/skills/pricing, commas %2C-encoded, page
    /// last), so the poller and B3 UI read the site's own link rather than rebuilding the query. The
    /// disabled prev is a <c>&lt;span&gt;</c> (no <c>a</c>), so it reads back as null. Newest-first
    /// ordering means the feed itself does not depend on this — it is exposed for the paging seam.
    /// </summary>
    public static (string? Prev, string? Next) ParsePagination(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return (null, null);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        string? Read(string rel)
        {
            var link = root.SelectSingleNode($"//ul[contains(@class, 'pagination')]//a[@rel='{rel}']");
            var href = link?.GetAttributeValue("href", string.Empty);
            // Attribute values arrive HTML-entity-encoded (&amp;amp; between facets) — the crawl
            // feeds these back as request URLs, so decode before returning (stress-audit P1).
            return string.IsNullOrEmpty(href) ? null : HtmlEntity.DeEntitize(href);
        }

        return (Read("prev"), Read("next"));
    }
}

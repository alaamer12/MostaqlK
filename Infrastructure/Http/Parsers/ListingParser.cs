using HtmlAgilityPack;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Http.Parsers;

/// <summary>
/// Parses the Mostaql projects listing page HTML into a collection of <see cref="ProjectSummary"/>.
/// Mirrors the "projects_list" branch of the Python prototype `analyzer.py`
/// (see .repertoire/progress/python/parser/scratch/analyzer.py).
/// </summary>
public static class ListingParser
{
    public static IReadOnlyList<ProjectSummary> Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw ParseErrors.EmptyHtml(nameof(ListingParser));
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var rows = root.SelectNodes("//tr[contains(concat(' ', normalize-space(@class), ' '), ' project-row ')]");

        var summaries = new List<ProjectSummary>();

        if (rows is not null && rows.Count > 0)
        {
            foreach (var row in rows)
            {
                var summary = ParseRow(row);
                if (summary is not null)
                {
                    summaries.Add(summary);
                }
            }
        }
        else
        {
            // Fallback observed shape: div.project-item cards instead of table rows.
            var items = root.SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' project-item ')]");
            if (items is not null)
            {
                foreach (var item in items)
                {
                    var summary = ParseRow(item);
                    if (summary is not null)
                    {
                        summaries.Add(summary);
                    }
                }
            }
        }

        if (summaries.Count == 0 && rows is null)
        {
            // Neither shape found at all - likely not a listing page / structure changed drastically.
            throw ParseErrors.NoProjectRows();
        }

        return summaries;
    }

    private static ProjectSummary? ParseRow(HtmlNode row)
    {
        // Title link is realistically an <h2><a>...</a></h2> per analyzer.py's sample_list_title.
        var titleLink = row.SelectSingleNode(".//h2/a") ?? row.SelectSingleNode(".//a");
        if (titleLink is null)
        {
            return null;
        }

        var title = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(titleLink.InnerText));
        var url = titleLink.GetAttributeValue("href", string.Empty);

        // ASSUMPTION: no explicit data-project-id attribute has been observed in the sample
        // pages, so the numeric project id is parsed from the trailing digits of the project
        // URL (Mostaql project URLs end in "-<id>" or "/<id>"), falling back to 0 when absent.
        var projectId = ExtractProjectIdFromUrl(url);

        var meta = row.SelectSingleNode(".//ul[contains(concat(' ', normalize-space(@class), ' '), ' project__meta ')]");

        string clientName = string.Empty;
        string publishTimeText = string.Empty;
        int publishTimeNumber = 0;
        int proposalCount = 0;

        if (meta is not null)
        {
            var metaItems = meta.SelectNodes("./li");
            if (metaItems is not null)
            {
                // ASSUMPTION: metaItems commonly appear in the order [client name, posted
                // relative time, proposal count] as separate <li> entries; adapt defensively
                // since the exact order/count is not guaranteed across page variants.
                foreach (var li in metaItems)
                {
                    var text = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(li.InnerText));
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    var digitsOnly = new string(text.Where(char.IsDigit).ToArray());
                    if (digitsOnly.Length > 0 && int.TryParse(digitsOnly, out var count)
                        && !string.IsNullOrEmpty(clientName) && proposalCount == 0 && LooksLikeProposalCount(text))
                    {
                        proposalCount = count;
                    }
                    else if (string.IsNullOrEmpty(clientName))
                    {
                        clientName = text;
                    }
                    else if (string.IsNullOrEmpty(publishTimeText))
                    {
                        publishTimeText = text;
                        // Extract number from relative time text (e.g. "منذ 7 دقائق" -> 7)
                        var numMatch = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
                        if (numMatch.Success && int.TryParse(numMatch.Value, out var num))
                        {
                            publishTimeNumber = num;
                        }
                        else if (text.Contains("ساعة")) publishTimeNumber = 1;
                        else if (text.Contains("ساعتين")) publishTimeNumber = 2;
                        else if (text.Contains("يوم")) publishTimeNumber = 1;
                        else if (text.Contains("يومان")) publishTimeNumber = 2;
                        else if (text.Contains("دقيقة")) publishTimeNumber = 1;
                        else if (text.Contains("دقيقتان")) publishTimeNumber = 2;
                        else if (text.Contains("لحظات")) publishTimeNumber = 0;
                    }
                }
            }
        }

        return new ProjectSummary
        {
            ProjectId = projectId,
            Title = title,
            Url = url,
            ClientName = clientName,
            PublishTimeNumber = publishTimeNumber,
            PublishTimeText = publishTimeText,
            ProposalCount = proposalCount,
            DiscoveredAt = DateTimeOffset.UtcNow,
        };
    }

    private static bool LooksLikeProposalCount(string text) =>
        text.Contains("عرض") || text.Contains("عروض") || text.Contains("تسليم");

    private static readonly System.Text.RegularExpressions.Regex ProjectIdRegex =
        new(@"/project/(\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Mostaql project URLs are "/project/{id}-{arabic-slug}". The id is therefore the FIRST
    /// numeric segment after "/project/", not the last numeric run in the URL - a slug that
    /// happens to contain a number ("...-canva-2024", "...-logo-3d") used to hijack the id and
    /// silently key the whole record to the wrong project. Falls back to the first standalone
    /// numeric segment when the "/project/" prefix is absent (e.g. a relative/redesigned URL).
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

        var trimmed = url.TrimEnd('/');
        var firstSegment = trimmed.Split('/', '-').FirstOrDefault(s => s.Length > 0 && s.All(char.IsDigit));
        return firstSegment is not null && long.TryParse(firstSegment, out var id) ? id : 0;
    }
}

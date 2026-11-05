using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MostaqlK.Core.Sources;
using MostaqlK.Models;
using MostaqlK.Sources.Mostaql.Parsing;

namespace MostaqlK.Sources.Mostaql.Parsing;

/// <summary>
/// Parses the anonymous Mostaql <c>/freelancers</c> catalog page into competitor <see cref="Performer"/>
/// files (v4 R1.2 / §4.6: this catalog is public and carries fields — completion rate, live presence —
/// that the Nafezly performer path does not). It needs no session and no WAF token, so it is the
/// performer source that works today while the <c>/u/</c> profile route is still gated on the WebView2
/// spike (R1.10).
/// <para>
/// Module-side, so it names Mostaql's own DOM classes directly (like <see cref="MostaqlSelectors"/>):
/// shared code stays source-blind and never gains a platform branch. Identity is the <c>/u/</c> username
/// slug (D37 — Mostaql's numeric id is session-only), unique only within this source; the composite
/// <c>(SourceId, PerformerKey)</c> is what the store keys on.
/// </para>
/// </summary>
public static class MostaqlCatalogParser
{
    private static SourceId Source => MostaqlSource.Instance.Id;

    public static IReadOnlyList<Performer> Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw ParseErrors.EmptyHtml(nameof(MostaqlCatalogParser));
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var rows = doc.DocumentNode.SelectNodes("//tr[contains(concat(' ', normalize-space(@class), ' '), ' freelancer-row ')]");

        var performers = new List<Performer>();
        if (rows is null)
        {
            return performers;
        }

        foreach (var row in rows)
        {
            var performer = ParseRow(row);
            if (performer is not null)
            {
                performers.Add(performer);
            }
        }
        return performers;
    }

    private static Performer? ParseRow(HtmlNode row)
    {
        // Identity + profile url: the first /u/<slug> link on the card.
        var hrefRaw = row.SelectSingleNode(".//a[contains(@href, '/u/')]")?.GetAttributeValue("href", "");
        var profileUrl = string.IsNullOrEmpty(hrefRaw) ? null : hrefRaw;
        var slug = ExtractSlug(profileUrl);
        if (string.IsNullOrEmpty(slug))
        {
            return null; // a row we cannot key is dropped, never invented (facts-not-guesses, R1.9)
        }

        var name = row.SelectSingleNode(".//bdi")?.InnerText?.Trim()
                   ?? row.SelectSingleNode(".//h2[contains(@class,'text-meta')]")?.InnerText?.Trim()
                   ?? string.Empty;

        // Rating = the filled-star count the catalog prints (Mostaql shows stars, not a numeric score here).
        var stars = row.SelectNodes(".//*[contains(@class,'rating-star')]")?.Count ?? 0;
        double? rating = stars > 0 ? stars : null;

        // Live presence: the green indicator's own tooltip text, present only when online.
        string? presence = null;
        var indicator = row.SelectSingleNode(".//*[contains(@class,'usercard__indicator-top')]");
        if (indicator is not null)
        {
            presence = NonEmpty(indicator.GetAttributeValue("title", ""));
        }

        // Completion rate: the number beside the percent glyph (§4.6 — a Mostaql-only signal).
        double? completion = ExtractPercent(row);

        return new Performer
        {
            Source = Source,
            PerformerKey = slug,
            DisplayName = name,
            ProfileUrl = profileUrl,
            Rating = rating,
            CompletionRate = completion,
            LivePresence = presence,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
    }

    private static string? ExtractSlug(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }
        var m = Regex.Match(url, @"/u/([^\s""/?#]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static double? ExtractPercent(HtmlNode row)
    {
        // The percent metric is its own cell: <div class="freelancers__item-"><i class="fa fa-percent"></i> 66.67</div>.
        // Target the fa-percent glyph's own container, not the first freelancer__item* (that is the star block).
        var glyph = row.SelectSingleNode(".//i[contains(concat(' ', normalize-space(@class), ' '), ' fa-percent ')]");
        var text = glyph?.ParentNode?.InnerText;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var m = Regex.Match(text, @"(\d+(?:[.,]\d+)?)");
        if (!m.Success)
        {
            return null;
        }
        var raw = m.Groups[1].Value.Replace(',', '.');
        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

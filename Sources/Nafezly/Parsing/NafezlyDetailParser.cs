using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MostaqlK.Core.Formatting;
using MostaqlK.Core.Parsing;
using MostaqlK.Models;

namespace MostaqlK.Sources.Nafezly.Parsing;

/// <summary>
/// Parses a nafezly project detail page into <see cref="ProjectDetails"/>. Order of operations only —
/// every shared heuristic lives in <c>Core/Parsing</c>, every nafezly fact arrives as
/// <see cref="NafezlyParseProfile"/>/<see cref="NafezlySelectors"/> data.
/// </summary>
/// <remarks>
/// Resolution precedence (A2 handoff + backend-ui-contract §7): the JSON-LD <c>JobPosting</c> block is
/// the PRIMARY structured source (title, skills CSV, workHours == days, datePosted); the بطاقة المشروع
/// label rows are the fallback for status / budget / proposals / publish; the generic
/// <see cref="FieldInferenceEngine"/> is the last tier for a fact neither structured source carried.
/// Fields the anonymous HTML does NOT expose (executor success count, numeric rating, attachments,
/// supervised flag) stay null — never guessed (capture-manifest §1.9-§1.11).
/// </remarks>
public static class NafezlyDetailParser
{
    private static ParseProfile Profile => NafezlyParseProfile.Profile;
    private static SelectorMap S => NafezlySelectors.Selectors;

    // Field key → its Arabic panel label(s), derived from the profile's LabelToField synonyms.
    private static readonly Dictionary<string, string[]> FieldToLabels =
        Profile.LabelToField
            .GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToArray());

    private static readonly Regex ArabicDigitRegex = new("[٠١٢٣٤٥٦٧٨٩]", RegexOptions.Compiled);
    private static readonly Regex GroupedIntRegex = new(@"\d{1,3}(?:[.,]\d{3})+|\d+", RegexOptions.Compiled);
    private static readonly Regex ControlCharRegex = new(@"[\r\n\t]+", RegexOptions.Compiled);
    private static readonly Regex DateSqueezeRegex = new(@"\s*-\s*", RegexOptions.Compiled);
    private static readonly Regex WorkspaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static ProjectDetails Parse(long projectId, string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw NafezlyParseErrors.EmptyHtml(nameof(NafezlyDetailParser));
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var jobPosting = TryGetJobPosting(root);

        // ---- structured label rows (بطاقة المشروع) + identifier-blind adjacency fallback. ----
        var metaCard = FindMetaCard(root);
        var structural = metaCard is not null
            ? HtmlStructureEngine.ExtractMetaFields(metaCard, Profile, S)
            : new Dictionary<string, string>();
        var labelDriven = HtmlStructureEngine.LabelDrivenExtract(root, Profile.KnownLabels);

        Dictionary<string, FieldInferenceResult>? inferenceResults = null;
        var fields = new Dictionary<string, FieldResolution>();
        var mismatches = new List<FieldMismatch>();

        foreach (var (field, labels) in FieldToLabels)
        {
            string? sVal = null;
            foreach (var label in labels)
            {
                var key = HtmlStructureEngine.NormalizeLabel(label);
                if (structural.TryGetValue(key, out var sv) || labelDriven.TryGetValue(key, out sv))
                {
                    sVal = sv;
                    break;
                }
            }

            var sOk = SanityOk(field, sVal);
            string? value;
            string source;
            double confidence;

            if (sOk)
            {
                value = sVal;
                source = "structural";
                confidence = 1.0;
            }
            else
            {
                inferenceResults ??= FieldInferenceEngine.InferFields(root, Profile.FieldProfiles);
                var infRes = inferenceResults.GetValueOrDefault(field);
                value = infRes?.Value;
                confidence = infRes?.Confidence ?? 0.0;
                source = value is not null ? "inference" : "none";
            }

            if (inferenceResults is not null && sVal is not null)
            {
                var infVal = inferenceResults.GetValueOrDefault(field)?.Value;
                if (infVal is not null && !ValuesAgree(sVal, infVal))
                {
                    mismatches.Add(new FieldMismatch(field, sVal, infVal));
                    if (!sOk)
                    {
                        value = infVal;
                    }
                }
            }

            if (IsPlaceholder(value))
            {
                value = null;
            }

            fields[field] = new FieldResolution(value, source, confidence);
        }

        // ---- title: JSON-LD primary → h1 → og:title → <title> (site suffix stripped). ----
        var ldTitle = GetString(jobPosting, "title");
        var title = FirstNonEmpty(
            ldTitle,
            ExtractH1(root),
            ExtractMetaContent(root, "//meta[@property='og:title' or @name='og:title']"),
            StripSiteSuffix(HtmlStructureEngine.Normalize(HtmlEntity.DeEntitize(
                root.SelectSingleNode("//title")?.InnerText ?? string.Empty)))) ?? string.Empty;

        if (string.IsNullOrEmpty(title))
        {
            throw NafezlyParseErrors.MissingTitle(projectId);
        }

        // ---- skills: JSON-LD skills CSV primary → skill-anchor href shape fallback. ----
        var skills = ExtractSkills(jobPosting, root);

        // ---- description: JSON-LD description primary → densest body prose fallback. ----
        var description = FirstNonEmpty(
            GetString(jobPosting, "description"),
            DensestProse(root)) ?? string.Empty;

        // ---- duration (days): JSON-LD workHours (which is actually DAYS on nafezly) primary, and
        //      recorded as the provenance source when it wins. ----
        int? ldWorkHours = ParseIntSafe(GetString(jobPosting, "workHours"));
        int? deliveryDays = ldWorkHours ?? ParseLeadingInt(fields.GetValueOrDefault("duration")?.Value);
        if (ldWorkHours is not null)
        {
            fields["duration"] = new FieldResolution($"{ldWorkHours}", "jsonld", 1.0);
        }

        // ---- budget: meta الميزانية (display string verbatim, e.g. "10 - 25 $"). ----
        var budget = fields.GetValueOrDefault("budget")?.Value;

        // ---- state: meta حالة المشروع, scraped text verbatim (no colour → no retint). ----
        var projectStatus = fields.GetValueOrDefault("project_status")?.Value;

        // ---- proposals: deterministic offer-row count == عدد المتقدمين; else the label value. ----
        var offerCount = HtmlStructureEngine.SelectByClassContains(root, "div", "offer-order").Count();
        var proposalLabelText = fields.GetValueOrDefault("proposal_count")?.Value;
        int proposalNum;
        string proposalText;
        if (offerCount > 0)
        {
            proposalNum = offerCount;
            proposalText = $"{offerCount}{Profile.ProposalCountSuffix}";
            fields["proposal_count"] = new FieldResolution(proposalText, "structural", 1.0);
        }
        else
        {
            var (n, t) = ArabicProposalParser.Parse(proposalLabelText);
            proposalNum = n;
            proposalText = t ?? proposalLabelText ?? string.Empty;
        }

        // ---- publish: relative text from the meta panel (JSON-LD datePosted has no relative form;
        //      the panel is the only display-shape source for "منذ …"). ----
        var publishedText = fields.GetValueOrDefault("published_date")?.Value ?? string.Empty;
        var publishNumber = ArabicRelativeTime.ParseRelativeNumber(publishedText);

        // ---- owner (poster): app-gated, so read the owner anchor text + avatar directly. ----
        var owner = ExtractOwner(root);

        // ---- executor (منفذ المشروع): present only on awarded projects; the name is the
        //      freelancer anchor in the بطاقة المشروع person block (capture-manifest §1.9). The
        //      success-count / rating / profile URL are app-gated → never read here (name only). ----
        var executorName = ExtractExecutorName(root);

        // Attachments are ABSENT for anonymous nafezly (capture-manifest §1.11) → empty, never guessed.
        var attachments = new List<Asset>();

        // EnrichmentStatus: a page that yielded a title but none of the core card facts (a
        // session/app-gated or error page) degrades to summary-only + Failed — surfaced honestly and
        // never retried into a poisoned queue (the module's enrichment returns this as Ok so the
        // worker stores the summary and moves on). Otherwise Enriched.
        var hasAnyCoreFact = budget is not null || projectStatus is not null || proposalNum > 0 || deliveryDays is not null;
        var status = hasAnyCoreFact ? EnrichmentStatus.Enriched : EnrichmentStatus.Failed;

        return new ProjectDetails
        {
            ProjectId = projectId,
            Title = title,
            Url = string.Empty,
            Description = description,
            Budget = budget,
            DeliveryDays = deliveryDays,
            ProjectStatus = projectStatus,
            PublishTimeNumber = publishNumber,
            PublishTimeText = publishedText,
            ProposalCount = proposalNum,
            ProposalCountText = proposalText,
            Skills = skills,
            Owner = owner,
            ExecutorName = executorName,
            Attachments = attachments,
            EnrichmentStatus = status,
            DiscoveredAt = DateTimeOffset.UtcNow,
            // Only stamp EnrichedAt when enrichment truly succeeded (the feed sorts on it).
            EnrichedAt = status == EnrichmentStatus.Enriched ? DateTimeOffset.UtcNow : null,
            FieldProvenance = fields,
            Mismatches = mismatches,
        };
    }

    // ----------------------------------------------------------------- JSON-LD

    /// <summary>
    /// Finds the <c>JobPosting</c> object among the page's <c>application/ld+json</c> script blocks.
    /// nafezly's LD carries literal newlines inside string values (e.g. <c>datePosted</c>), which a
    /// strict JSON reader rejects, so the block text is control-char-sanitised first; a per-field
    /// regex fallback covers a page whose LD is malformed beyond that.
    /// </summary>
    private static JsonElement? TryGetJobPosting(HtmlNode root)
    {
        foreach (var script in root.SelectNodes("//script[@type='application/ld+json']") ?? Enumerable.Empty<HtmlNode>())
        {
            var raw = script.InnerText;
            if (raw is null || raw.IndexOf("JobPosting", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            var sanitized = ControlCharRegex.Replace(raw, " ");
            try
            {
                using var parsed = JsonDocument.Parse(sanitized);
                var found = FindJobPosting(parsed.RootElement);
                if (found is not null)
                {
                    return found.Value.Clone();
                }
            }
            catch (JsonException)
            {
                // Leave this block; the regex fallback in GetString/ParseIntSafe still applies per field.
            }
        }

        return null;
    }

    private static JsonElement? FindJobPosting(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryGetStringProperty(element, "@type", out var type) && type == "JobPosting")
                {
                    return element;
                }
                foreach (var prop in element.EnumerateObject())
                {
                    var nested = FindJobPosting(prop.Value);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }
                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindJobPosting(item);
                    if (nested is not null)
                    {
                        return nested;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    private static string? GetString(JsonElement? jobPosting, string field)
    {
        if (jobPosting is not null && jobPosting.Value.ValueKind == JsonValueKind.Object
            && jobPosting.Value.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : WorkspaceRegex.Replace(text, " ").Trim();
        }
        return null;
    }

    private static bool TryGetStringProperty(JsonElement obj, string name, out string value)
    {
        if (obj.TryGetProperty(name, out var el))
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                value = el.GetString() ?? string.Empty;
                return true;
            }
            if (el.ValueKind == JsonValueKind.Array && el.GetArrayLength() > 0)
            {
                value = el[0].GetString() ?? string.Empty;
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    // ----------------------------------------------------------------- structured meta card

    /// <summary>
    /// Locates the بطاقة المشروع fact panel by climbing from its header to the nearest ancestor that
    /// actually contains the meta rows, so <see cref="HtmlStructureEngine.ExtractMetaFields"/> is
    /// scoped to just that card (nafezly reuses the generic box class all over the page).
    /// </summary>
    private static HtmlNode? FindMetaCard(HtmlNode root)
    {
        foreach (var header in HtmlStructureEngine.FindLabelElements(root, "بطاقة المشروع"))
        {
            var ancestor = header.ParentNode;
            while (ancestor is not null && ancestor.NodeType == HtmlNodeType.Element)
            {
                var text = ancestor.InnerText;
                if (text.Contains("الميزانية", StringComparison.Ordinal)
                    && text.Contains("عدد المتقدمين", StringComparison.Ordinal))
                {
                    return ancestor;
                }
                ancestor = ancestor.ParentNode;
            }
        }
        return null;
    }

    // ----------------------------------------------------------------- field extraction

    private static List<ProjectSkill> ExtractSkills(JsonElement? jobPosting, HtmlNode root)
    {
        var result = new List<ProjectSkill>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Primary: JSON-LD skills CSV.
        var csv = GetString(jobPosting, "skills");
        if (!string.IsNullOrWhiteSpace(csv))
        {
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var name = HtmlStructureEngine.Normalize(part);
                if (name.Length > 0 && seen.Add(name))
                {
                    result.Add(new ProjectSkill { Name = name, Url = null });
                }
            }
            if (result.Count > 0)
            {
                return result;
            }
        }

        // Fallback: skill-taxonomy anchors (a[href*="/projects/skill/"]), label = text after the tag icon.
        foreach (var a in root.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = a.GetAttributeValue("href", string.Empty);
            if (!S.SkillHrefSubstrings.Any(fragment => href.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var name = HtmlStructureEngine.Normalize(HtmlEntity.DeEntitize(a.InnerText));
            if (name.Length is > 0 and <= 60 && seen.Add(name))
            {
                result.Add(new ProjectSkill { Name = name, Url = href });
            }
        }
        return result;
    }

    private static Owner ExtractOwner(HtmlNode root)
    {
        var owner = new Owner
        {
            Source = NafezlySource.Instance.Id,
            Name = string.Empty,
        };

        // Every person anchor is a js-profile-app-gate button; the poster carries data-profile-role="owner".
        var ownerAnchor = root.SelectSingleNode("//a[contains(@class, 'js-profile-app-gate')][@data-profile-role='owner']")
                          ?? root.SelectSingleNode("//a[@data-profile-role='owner']");
        if (ownerAnchor is null)
        {
            return owner;
        }

        var name = HtmlStructureEngine.Normalize(HtmlEntity.DeEntitize(ownerAnchor.InnerText));
        // The avatar <img alt> mirrors the name; prefer the anchor's own text, fall back to alt.
        if (string.IsNullOrEmpty(name))
        {
            name = ownerAnchor.GetAttributeValue("aria-label", string.Empty) == "user"
                ? FindNearbyAvatarAlt(ownerAnchor)
                : string.Empty;
        }
        if (string.IsNullOrEmpty(name))
        {
            name = FindNearbyAvatarAlt(ownerAnchor);
        }

        owner.Name = name;
        // Profile URLs do not exist in anonymous HTML (href="javascript:void(0)") → null, not guessed.
        owner.ProfileUrl = null;
        owner.AvatarUrl = FindNearbyAvatarUrl(ownerAnchor);
        // No numeric owner id is exposed anonymously → a stable hash of the name keeps the row keyed.
        owner.OwnerId = name.Length > 0 ? StableHash(name) : 0;
        // rating / completed-projects-count are app-gated → stay null.
        return owner;
    }

    /// <summary>
    /// Reads the awarded freelancer's name (منفذ المشروع) from the بطاقة المشروع person block. The
    /// label sits in its own <c>col-12</c> wrapper and the person row is the following sibling block,
    /// so the freelancer anchors are located inside that row — deliberately NOT the (far more numerous)
    /// <c>data-profile-role="freelancer"</c> offer-submitter anchors, which live earlier in the page in
    /// <c>#project-load-offers</c>. Open projects carry no <c>منفذ المشروع</c> block at all (capture-
    /// manifest §1.9) → the method returns <c>null</c> and the executor row is simply absent. Name only:
    /// the success-count / rating / profile URL are app-gated and never present in anonymous HTML.
    /// </summary>
    private static string? ExtractExecutorName(HtmlNode root)
    {
        foreach (var label in HtmlStructureEngine.FindLabelElements(root, "منفذ المشروع"))
        {
            // The label's own wrapper (col-12) is followed by the person-row block holding the anchor.
            var sibling = label.ParentNode?.NextSibling;
            while (sibling is not null)
            {
                var anchors = sibling.SelectSingleNode(".//a[@data-profile-role='freelancer']");
                if (anchors is not null)
                {
                    break;
                }

                sibling = sibling.NextSibling;
            }

            var row = sibling;
            if (row is null)
            {
                continue;
            }

            // Prefer the anchor whose own text is the name; fall back to the avatar <img alt> (mirrors
            // the owner path) when the text anchor is empty.
            foreach (var anchor in row.SelectNodes(".//a[@data-profile-role='freelancer']") ?? Enumerable.Empty<HtmlNode>())
            {
                var name = HtmlStructureEngine.Normalize(HtmlEntity.DeEntitize(anchor.InnerText));
                if (name.Length > 0)
                {
                    return name;
                }
            }

            foreach (var anchor in row.SelectNodes(".//a[@data-profile-role='freelancer']") ?? Enumerable.Empty<HtmlNode>())
            {
                var alt = FindNearbyAvatarAlt(anchor);
                if (alt.Length > 0)
                {
                    return alt;
                }
            }
        }

        return null;
    }

    private static string FindNearbyAvatarAlt(HtmlNode anchor)
    {
        var scope = anchor.ParentNode;
        var img = scope?.SelectSingleNode(".//img") ?? anchor.SelectSingleNode(".//ancestor::div[1]//img");
        return img is null ? string.Empty : HtmlStructureEngine.Normalize(img.GetAttributeValue("alt", string.Empty));
    }

    private static string? FindNearbyAvatarUrl(HtmlNode anchor)
    {
        var scope = anchor.ParentNode;
        var img = scope?.SelectSingleNode(".//img")
                  ?? anchor.SelectSingleNode(".//img")
                  ?? anchor.SelectSingleNode(".//ancestor::div[1]//img");
        var src = img?.GetAttributeValue("src", string.Empty);
        return string.IsNullOrEmpty(src) ? null : src;
    }

    private static string ExtractH1(HtmlNode root)
    {
        var h1 = root.SelectSingleNode("//h1");
        return h1 is null ? string.Empty : StripSiteSuffix(HtmlStructureEngine.Normalize(HtmlEntity.DeEntitize(h1.InnerText)));
    }

    private static string ExtractMetaContent(HtmlNode root, string xpath)
    {
        var meta = root.SelectSingleNode(xpath);
        return HtmlStructureEngine.Normalize(HtmlEntity.DeEntitize(meta?.GetAttributeValue("content", string.Empty) ?? string.Empty));
    }

    private static string StripSiteSuffix(string title)
    {
        foreach (var sep in Profile.TitleSuffixSeparators)
        {
            var idx = title.LastIndexOf(sep, StringComparison.Ordinal);
            if (idx > 0)
            {
                var suffix = title[(idx + sep.Length)..];
                if (Profile.TitleSiteKeywords.Any(k => suffix.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    title = title[..idx].Trim();
                }
            }
        }
        foreach (var keyword in Profile.TitleSiteKeywords)
        {
            if (title.EndsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                title = title[..^keyword.Length].Trim();
            }
        }
        return title;
    }

    private static string DensestProse(HtmlNode root)
    {
        HtmlNode? best = null;
        var bestLength = 200;
        foreach (var node in root.SelectNodes("//div|//article|//section") ?? Enumerable.Empty<HtmlNode>())
        {
            if (node.SelectNodes("./div|./article|./section")?.Count > 2)
            {
                continue;
            }
            var text = HtmlStructureEngine.Normalize(HtmlEntity.DeEntitize(node.InnerText));
            if (text.Length > bestLength)
            {
                best = node;
                bestLength = text.Length;
            }
        }
        return best is null ? string.Empty : HtmlStructureEngine.NormalizeMultiline(best);
    }

    // ----------------------------------------------------------------- sanity / numeric helpers

    private static bool SanityOk(string field, string? value)
    {
        if (value is null)
        {
            return false;
        }
        var v = value.Trim();
        if (v.Length == 0)
        {
            return false;
        }
        if (IsPlaceholder(v))
        {
            return true;
        }
        if (Profile.NumericFields.Contains(field))
        {
            return v.Any(char.IsDigit) || ArabicDigitRegex.IsMatch(v);
        }
        return true;
    }

    private static bool IsPlaceholder(string? value) =>
        value is not null && Profile.NotCalculatedMarkers.Any(value.Contains);

    private static bool ValuesAgree(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return true;
        }
        var an = a.Trim();
        var bn = b.Trim();
        return an == bn || bn.Contains(an, StringComparison.Ordinal) || an.Contains(bn, StringComparison.Ordinal);
    }

    private static int? ParseLeadingInt(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var ascii = HtmlStructureEngine.ToAsciiDigits(text);
        var match = GroupedIntRegex.Match(ascii);
        if (!match.Success)
        {
            return null;
        }
        var digits = match.Value.Replace(",", string.Empty).Replace(".", string.Empty);
        return int.TryParse(digits, out var value) ? value : null;
    }

    private static int? ParseIntSafe(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var ascii = HtmlStructureEngine.ToAsciiDigits(text).Trim();
        return int.TryParse(ascii, out var value) ? value : ParseLeadingInt(ascii);
    }

    private static long StableHash(string s)
    {
        long hash = 17;
        foreach (var c in s)
        {
            hash = hash * 31 + c;
        }
        return Math.Abs(hash);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v;
            }
        }
        return null;
    }
}

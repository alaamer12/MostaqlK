using HtmlAgilityPack;
using MostaqlK.Core.Formatting;
using MostaqlK.Models;

namespace MostaqlK.Infrastructure.Http.Parsers;

/// <summary>
/// Parses a Mostaql project detail page HTML into a fully populated <see cref="ProjectDetails"/>.
/// Mirrors pipeline.py's <c>parse_project()</c> combinator (see
/// .repertoire/progress/python/parser/scratch/pipeline.py): per meta-row field, prefer the
/// structural (class/id) value when it passes a sanity check, otherwise fall back to
/// <see cref="InferenceEngine"/>, cross-validate whenever both are available, and enforce the
/// nullable-by-design rules for the "completed only" fields. Title/skills/description/
/// attachments stay structural-only, exactly as in pipeline.py.
/// </summary>
public static class DetailParser
{
    /// <summary>Arabic label -> inference.py field key. Mirrors pipeline.py's LABEL_TO_FIELD.</summary>
    private static readonly Dictionary<string, string> LabelToField = new()
    {
        ["حالة المشروع"] = "project_status",
        ["تاريخ النشر"] = "published_date",
        ["الميزانية"] = "budget",
        ["مدة التنفيذ"] = "duration",
        ["تاريخ التسجيل"] = "registration_date",
        ["معدل التوظيف"] = "hire_rate",
        ["المشاريع المفتوحة"] = "open_projects_count",
        ["مشاريع قيد التنفيذ"] = "in_progress_count",
        ["مشاريع منجزة"] = "completed_projects_count",
        ["التواصلات الجارية"] = "ongoing_conversations",
        ["بدأ تنفيذه منذ"] = "started_since",
        ["تاريخ الصفقة"] = "deal_date",
        ["موعد التسليم"] = "delivery_date",
        ["عدد العروض"] = "proposal_count",
        ["عدد المقترحات"] = "proposal_count",
    };

    private static readonly Dictionary<string, string> FieldToLabel =
        LabelToField
            .GroupBy(kv => kv.Value)
            .ToDictionary(group => group.Key, group => group.First().Value);

    private static readonly HashSet<string> CompletedOnlyFields = ["started_since", "deal_date", "delivery_date"];
    private const string CompletedStatusText = "مكتمل";

    private static readonly HashSet<string> NumericFields =
        ["hire_rate", "budget", "duration", "open_projects_count", "in_progress_count", "completed_projects_count", "ongoing_conversations", "proposal_count"];

    // Mirrors analyzer.NOT_CALCULATED_MARKERS / ARABIC_DIGIT_RE used by pipeline.py's
    // _is_placeholder/_sanity_ok.
    private static readonly string[] NotCalculatedMarkers = ["لم يحسب بعد", "غير محدد", "N/A", "لا يوجد"];
    private static readonly System.Text.RegularExpressions.Regex ArabicDigitRegex =
        new("[٠١٢٣٤٥٦٧٨٩]", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static ProjectDetails Parse(long projectId, string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw ParseErrors.EmptyHtml(nameof(DetailParser));
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var title = ExtractTitle(root);
        if (string.IsNullOrEmpty(title))
        {
            throw ParseErrors.MissingTitle(projectId);
        }

        var description = ExtractDescription(root);
        var skills = ExtractSkills(root);

        // ---- structural-first / inference-fallback combinator per label/field. ----
        var structural = StructuralExtractor.ExtractMetaFields(root);
        var labelDriven = StructuralExtractor.LabelDrivenExtract(root);

        Dictionary<string, FieldInferenceResult>? inferenceResults = null; // computed lazily, once per page.
        var fields = new Dictionary<string, FieldResolution>();
        var mismatches = new List<FieldMismatch>();

        foreach (var (label, field) in LabelToField)
        {
            // Prefer the structural extraction, but fall back to the label-driven
            // (identifier-blind) DOM-adjacency heuristic when the meta panel/table selector
            // itself didn't yield anything for this label.
            var labelKey = StructuralExtractor.NormalizeLabel(label);
            var sVal = structural.TryGetValue(labelKey, out var sv) ? sv : labelDriven.GetValueOrDefault(labelKey);
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
                inferenceResults ??= InferenceEngine.InferFields(root);
                var infRes = inferenceResults.GetValueOrDefault(field);
                value = infRes?.Value;
                confidence = infRes?.Confidence ?? 0.0;
                source = value is not null ? "inference" : "none";
            }

            // Cross-validate even when the structural fast path was trusted, as long as
            // inference has already been computed (cheap - it runs once for the whole page).
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

        // Enforce nullable-by-design completed-only fields.
        // (1) If the field's Arabic label text is not literally present anywhere on the page,
        // an inference-sourced value has nothing genuine to latch onto - force null.
        // Compared through NormalizeLabel so an orthographic variant of the label on the page
        // (trailing colon, diacritics, alef/ya spelling) still counts as "the label is present".
        var pageText = StructuralExtractor.NormalizeLabel(HtmlEntity.DeEntitize(root.InnerText));
        foreach (var f in CompletedOnlyFields)
        {
            if (fields.TryGetValue(f, out var res) && res.Source == "inference")
            {
                var label = FieldToLabel.GetValueOrDefault(f) is { } l ? StructuralExtractor.NormalizeLabel(l) : null;
                if (label is not null && !pageText.Contains(label, StringComparison.Ordinal))
                {
                    fields[f] = new FieldResolution(null, "none", 0.0);
                }
            }
        }

        // (2) Regardless of (1), these fields are only meaningful when the project is
        // actually completed.
        var statusValue = fields.GetValueOrDefault("project_status")?.Value;
        if (statusValue is null || !statusValue.Contains(CompletedStatusText, StringComparison.Ordinal))
        {
            foreach (var f in CompletedOnlyFields)
            {
                if (fields.TryGetValue(f, out var res))
                {
                    fields[f] = res with { Value = null };
                }
            }
        }

        var owner = new Owner
        {
            OwnerId = ExtractOwnerId(root),
            Name = ExtractOwnerName(root, labelDriven) ?? string.Empty,
            ProfileUrl = ExtractOwnerProfileUrl(root),
            HiringRatePercent = ParsePercent(fields.GetValueOrDefault("hire_rate")?.Value),
            CompletedProjectsCount = ParseLeadingInt(fields.GetValueOrDefault("completed_projects_count")?.Value),
            RegisteredAt = fields.GetValueOrDefault("registration_date")?.Value,
            OpenProjectsCount = ParseLeadingInt(fields.GetValueOrDefault("open_projects_count")?.Value),
            InProgressProjectsCount = ParseLeadingInt(fields.GetValueOrDefault("in_progress_count")?.Value),
            OngoingCommunicationsCount = ParseLeadingInt(fields.GetValueOrDefault("ongoing_conversations")?.Value),
        };

        var attachmentCandidates = StructuralExtractor.ExtractAttachments(root);
        var attachments = attachmentCandidates.Select(a => new Asset
        {
            ProjectId = projectId,
            FileName = a.Filename,
            Url = a.Url ?? string.Empty,
            Extension = a.Extension,
            RawUrl = a.RawUrl,
            RequiresAuth = a.RequiresAuth,
            SizeText = a.SizeText,
        }).ToList();

        var proposalText = fields.GetValueOrDefault("proposal_count")?.Value;
        var (proposalNum, proposalTxt) = ArabicProposalParser.Parse(proposalText);

        return new ProjectDetails
        {
            ProjectId = projectId,
            Title = title,
            Url = string.Empty,
            Description = description,
            Budget = fields.GetValueOrDefault("budget")?.Value,
            DeliveryDays = ParseLeadingInt(fields.GetValueOrDefault("duration")?.Value),
            ProjectStatus = fields.GetValueOrDefault("project_status")?.Value,
            PublishTimeText = fields.GetValueOrDefault("published_date")?.Value ?? string.Empty,
            PublishTimeNumber = ParseLeadingInt(fields.GetValueOrDefault("published_date")?.Value) ?? 0,
            ProposalCount = proposalNum,
            ProposalCountText = proposalTxt,
            Skills = skills,
            Owner = owner,
            Attachments = attachments,
            EnrichmentStatus = EnrichmentStatus.Enriched,
            EnrichedAt = DateTimeOffset.UtcNow,
            FieldProvenance = fields,
            Mismatches = mismatches,
        };
    }

    /// <summary>
    /// Mirrors pipeline.py's _sanity_ok: cheap type-shape check on the structural fast-path
    /// value; if it fails, the selector result isn't trusted and the caller falls back to
    /// inference.
    /// </summary>
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
            // a recognized placeholder is a VALID (nullable) resolution, not a sanity failure.
            return true;
        }
        var hasDigit = v.Any(char.IsDigit) || ArabicDigitRegex.IsMatch(v);
        if (NumericFields.Contains(field))
        {
            return hasDigit;
        }
        // dates/status/free text: any non-empty structural value is acceptable.
        return true;
    }

    /// <summary>Mirrors pipeline.py's _is_placeholder.</summary>
    private static bool IsPlaceholder(string? value) =>
        value is not null && NotCalculatedMarkers.Any(value.Contains);

    /// <summary>Mirrors pipeline.py's _values_agree.</summary>
    private static bool ValuesAgree(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return true;
        }
        var aNorm = a.Trim();
        var bNorm = b.Trim();
        return aNorm == bNorm || bNorm.Contains(aNorm, StringComparison.Ordinal) || aNorm.Contains(bNorm, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the page title through a fallback chain instead of the prototype's single
    /// <c>//h1</c> lookup: h1 -> <c>og:title</c> meta -> <c>&lt;title&gt;</c> (with Mostaql's
    /// " - مستقل" site suffix stripped). A redesign that demotes the project title to an
    /// <c>h2</c>/styled div used to make the whole parse throw <see cref="ParseErrors.MissingTitle"/>
    /// even though the title was plainly available in the document head.
    /// </summary>
    private static string ExtractTitle(HtmlNode root)
    {
        // 1. Structural: The main project header inside the details section.
        var h1 = root.SelectSingleNode("//h1");
        if (h1 is not null)
        {
            var text = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(h1.InnerText));
            if (text.Length > 0)
            {
                var stripped = StripSiteSuffix(text);
                if (!string.IsNullOrEmpty(stripped)) return stripped;
            }
        }

        // 2. OpenGraph: Usually contains the project title.
        var og = root.SelectSingleNode("//meta[@property='og:title' or @name='og:title']");
        var ogTitle = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(og?.GetAttributeValue("content", string.Empty) ?? string.Empty));
        if (ogTitle.Length > 0)
        {
            var stripped = StripSiteSuffix(ogTitle);
            if (!string.IsNullOrEmpty(stripped)) return stripped;
        }

        // 3. Fallback: The <title> tag.
        var titleTag = root.SelectSingleNode("//title");
        var docTitle = titleTag is not null
            ? StructuralExtractor.Normalize(HtmlEntity.DeEntitize(titleTag.InnerText))
            : string.Empty;
        return StripSiteSuffix(docTitle);
    }

    private static readonly string[] SiteSuffixSeparators = [" - ", " | ", " – "];
    private static readonly string[] SiteKeywords = ["مستقل", "Mostaql", "Mostaqlk"];

    private static string StripSiteSuffix(string title)
    {
        // Remove common site name suffixes from the end
        foreach (var sep in SiteSuffixSeparators)
        {
            var idx = title.LastIndexOf(sep, StringComparison.Ordinal);
            if (idx > 0)
            {
                var suffix = title[(idx + sep.Length)..];
                if (SiteKeywords.Any(k => suffix.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    title = title[..idx].Trim();
                }
            }
        }

        // Also handle cases where the keyword is just part of the string without a separator
        foreach (var keyword in SiteKeywords)
        {
            if (title.EndsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                title = title[..^keyword.Length].Trim();
            }
        }

        return title;
    }

    /// <summary>
    /// Mirrors pipeline.py's description resolution: <c>#projectDetailsTab</c> is unique and
    /// always wraps the real description (a review comment/proposal reuse the same
    /// "text-wrapper-div" class elsewhere), so scope the lookup inside it first, only falling
    /// back to a page-wide search when the tab itself is absent.
    /// </summary>
    private static string ExtractDescription(HtmlNode root)
    {
        var detailsTab = root.SelectSingleNode("//*[@id='projectDetailsTab']");
        HtmlNode? desc;
        if (detailsTab is not null)
        {
            desc = SelectByClassContains(detailsTab, "div", "text-wrapper-div") ?? detailsTab;
        }
        else
        {
            desc = SelectByClassContains(root, "div", "text-wrapper-div");
        }
        // Uses NormalizeMultiline (not Normalize) so the description keeps its paragraph/line
        // structure - Normalize collapses ALL whitespace (including newlines) into a single
        // space, which is exactly what was flattening Mostaql's bullet-style briefs
        // ("المهام:\n\n...") into one run-on sentence.
        if (desc is not null)
        {
            var text = StructuralExtractor.NormalizeMultiline(desc);
            if (text.Length > 0)
            {
                return text;
            }
        }

        // Identifier-blind fallbacks, in descending order of trust - none of which the Python
        // prototype had. If "text-wrapper-div" ever gets renamed, the description would
        // previously come back as an empty string with no error at all (a silent data loss).
        var og = root.SelectSingleNode("//meta[@property='og:description' or @name='description']");
        var ogText = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(og?.GetAttributeValue("content", string.Empty) ?? string.Empty));

        var densest = FindDensestTextBlock(root);
        if (densest is not null)
        {
            var text = StructuralExtractor.NormalizeMultiline(densest);
            // Only prefer the heuristic block over og:description when it is meaningfully
            // richer - og:description is usually a truncated teaser of the real brief.
            if (text.Length > ogText.Length)
            {
                return text;
            }
        }

        return ogText;
    }

    /// <summary>
    /// Last-resort description heuristic: the element carrying the largest amount of *own*
    /// prose (paragraph-ish text not attributable to a nested block), which on a project page
    /// is overwhelmingly the brief itself. Deliberately ignores classes/ids entirely.
    /// </summary>
    private static HtmlNode? FindDensestTextBlock(HtmlNode root)
    {
        HtmlNode? best = null;
        var bestLength = 200; // ignore short nav/footer blurbs entirely

        foreach (var node in root.SelectNodes("//div|//article|//section") ?? Enumerable.Empty<HtmlNode>())
        {
            // Skip containers that merely wrap other block containers - we want the innermost
            // element that actually owns the prose.
            if (node.SelectNodes("./div|./article|./section")?.Count > 2)
            {
                continue;
            }

            var text = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(node.InnerText));
            if (text.Length > bestLength)
            {
                best = node;
                bestLength = text.Length;
            }
        }

        return best;
    }

    private static HtmlNode? SelectByClassContains(HtmlNode root, string tag, string classSubstring) =>
        (root.SelectNodes($".//{tag}") ?? Enumerable.Empty<HtmlNode>())
            .FirstOrDefault(n => n.GetAttributeValue("class", string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(c => c.Contains(classSubstring, StringComparison.OrdinalIgnoreCase)));

    private static List<ProjectSkill> ExtractSkills(HtmlNode root)
    {
        var skillsList = root.SelectSingleNode("//ul[contains(concat(' ', normalize-space(@class), ' '), ' skills ')]")
                         ?? SelectByClassContains(root, "ul", "skills");

        var result = new List<ProjectSkill>();
        if (skillsList is not null)
        {
            foreach (var li in skillsList.SelectNodes("./li") ?? Enumerable.Empty<HtmlNode>())
            {
                var name = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(li.InnerText));
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var link = li.SelectSingleNode(".//a");
                result.Add(new ProjectSkill
                {
                    Name = name,
                    Url = link?.Attributes["href"]?.Value,
                });
            }
        }

        if (result.Count > 0)
        {
            return result;
        }

        // Identifier-blind fallback (not present in the Python prototype, which returned an
        // empty list the moment "ul.skills" disappeared): every skill on Mostaql is a link
        // into the skill taxonomy, so recognize them by their href shape rather than by the
        // class of the list that happens to wrap them today.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in root.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = a.GetAttributeValue("href", string.Empty);
            if (!LooksLikeSkillHref(href))
            {
                continue;
            }

            var name = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(a.InnerText));
            if (name.Length == 0 || name.Length > 60 || !seen.Add(name))
            {
                continue;
            }

            result.Add(new ProjectSkill { Name = name, Url = href });
        }

        return result;
    }

    private static bool LooksLikeSkillHref(string href) =>
        href.Contains("/skills/", StringComparison.OrdinalIgnoreCase)
        || href.Contains("skill=", StringComparison.OrdinalIgnoreCase)
        || href.Contains("/tag/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the project owner's display name through a fallback chain. The prototype only
    /// looked for <c>div.profile_card h5.profile__name</c>, so a single class rename blanked
    /// the whole owner card (the exact symptom reported against this parser). Order: the
    /// original selector -> any element whose class mentions "profile__name"/"profile-name"
    /// anywhere on the page -> the identifier-blind "صاحب المشروع" label walk -> the anchor
    /// text of the first user-profile link (<c>/u/{username}</c>).
    /// </summary>
    private static string? ExtractOwnerName(HtmlNode root, Dictionary<string, string> labelDriven)
    {
        // 1. Look specifically within the profile card/owner section
        var ownerCard = StructuralExtractor.FindOwnerCard(root);

        if (ownerCard is not null)
        {
            // The name is typically in an h5 or h3 or inside a link.
            var nameNode = ownerCard.SelectSingleNode(".//h5[contains(@class, 'name')]")
                        ?? ownerCard.SelectSingleNode(".//h3[contains(@class, 'name')]")
                        ?? ownerCard.SelectSingleNode(".//a[contains(@href, '/u/')]")
                        ?? ownerCard.SelectSingleNode(".//h5")
                        ?? ownerCard.SelectSingleNode(".//h3");
            
            if (nameNode is not null)
            {
                var text = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(nameNode.InnerText));
                if (text.Length > 0) return text;
            }
        }

        // 2. Fallback to the label-driven approach if identified as "صاحب المشروع"
        var labelled = labelDriven.GetValueOrDefault(StructuralExtractor.NormalizeLabel("صاحب المشروع"));
        if (!string.IsNullOrEmpty(labelled) && labelled.Length <= 80)
        {
            return labelled;
        }

        // 3. Last resort: find any link to a user profile within the details area and take its text
        var detailsArea = root.SelectSingleNode("//*[@id='projectDetailsTab']") 
                       ?? root.SelectSingleNode("//div[contains(@class, 'project-details')]")
                       ?? root.SelectSingleNode("//article");

        var profileLink = ((detailsArea ?? root).SelectNodes(".//a[contains(@href, '/u/')]") ?? Enumerable.Empty<HtmlNode>())
            .FirstOrDefault(a => 
            {
                var text = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(a.InnerText));
                return text.Length is > 0 and <= 60;
            });

        return profileLink is not null
            ? StructuralExtractor.Normalize(HtmlEntity.DeEntitize(profileLink.InnerText))
            : null;
    }

    /// <summary>
    /// Extracts the owner's Profile URL from the project page.
    /// Mostaql profile URLs typically follow the pattern https://mostaql.com/u/username.
    /// </summary>
    private static string? ExtractOwnerProfileUrl(HtmlNode root)
    {
        var ownerCard = StructuralExtractor.FindOwnerCard(root);
        if (ownerCard is not null)
        {
            var profileLink = ownerCard.SelectSingleNode(".//a[contains(@href, '/u/')]");
            if (profileLink is not null)
            {
                var url = profileLink.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(url))
                {
                    if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        url = "https://mostaql.com" + (url.StartsWith('/') ? "" : "/") + url;
                    }
                    return url;
                }
            }
        }

        // Fallback: search anywhere for /u/ links if owner card lookup failed, but scoped to details
        var detailsArea = root.SelectSingleNode("//*[@id='projectDetailsTab']") 
                       ?? root.SelectSingleNode("//div[contains(@class, 'project-details')]")
                       ?? root.SelectSingleNode("//article")
                       ?? root;

        var anyProfileLink = detailsArea.SelectSingleNode(".//a[contains(@href, '/u/')]");
        var fallbackUrl = anyProfileLink?.GetAttributeValue("href", string.Empty);
        if (!string.IsNullOrEmpty(fallbackUrl) && !fallbackUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
             fallbackUrl = "https://mostaql.com" + (fallbackUrl.StartsWith('/') ? "" : "/") + fallbackUrl;
        }
        return string.IsNullOrEmpty(fallbackUrl) ? null : fallbackUrl;
    }

    /// <summary>
    /// Extracts a unique numeric ID for the owner if available.
    /// Mostaql uses usernames in URLs, but some data-attributes might contain a numeric ID.
    /// If not found, we might need a fallback or use a hash of the username if the DB requires an INT.
    /// </summary>
    private static long ExtractOwnerId(HtmlNode root)
    {
        var ownerCard = StructuralExtractor.FindOwnerCard(root);
        string? username = null;

        if (ownerCard is not null)
        {
            // Look for data-user-id or similar attributes
            var idAttr = ownerCard.GetAttributeValue("data-user-id", string.Empty);
            if (string.IsNullOrEmpty(idAttr))
            {
                idAttr = ownerCard.SelectSingleNode(".//*[@data-user-id]")?.GetAttributeValue("data-user-id", string.Empty);
            }
            
            if (long.TryParse(idAttr, out var id)) return id;

            // Try to find it in links
            var profileLink = ownerCard.SelectSingleNode(".//a[contains(@href, '/u/')]");
            var href = profileLink?.GetAttributeValue("href", string.Empty) ?? string.Empty;
            if (!string.IsNullOrEmpty(href))
            {
                // Mostaql URLs are usually /u/username
                var parts = href.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var uIdx = Array.IndexOf(parts, "u");
                if (uIdx >= 0 && uIdx + 1 < parts.Length)
                {
                    username = parts[uIdx + 1];
                }
            }
        }

        if (username == null)
        {
            // Fallback: search within the project details area, NOT the whole root which includes header/nav
            var detailsArea = root.SelectSingleNode("//*[@id='projectDetailsTab']") 
                           ?? root.SelectSingleNode("//div[contains(@class, 'project-details')]")
                           ?? root.SelectSingleNode("//article")
                           ?? root;
            
            var anyProfileLink = detailsArea.SelectSingleNode(".//a[contains(@href, '/u/')]");
            var href = anyProfileLink?.GetAttributeValue("href", string.Empty) ?? string.Empty;
            if (!string.IsNullOrEmpty(href))
            {
                var parts = href.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var uIdx = Array.IndexOf(parts, "u");
                if (uIdx >= 0 && uIdx + 1 < parts.Length)
                {
                    username = parts[uIdx + 1];
                }
            }
        }

        if (username != null)
        {
            // Generate a stable hash for the username to use as a numeric ID if none found.
            // Using a simple Fowler-Noll-Vo hash or similar stable numeric reduction.
            long hash = 0;
            foreach (char c in username)
            {
                hash = (hash * 31) + c;
            }
            return Math.Abs(hash);
        }

        // Last resort: if we have a name, hash it
        var nameNode = (StructuralExtractor.FindOwnerCard(root) ?? root).SelectSingleNode(".//h5[contains(@class, 'name')]")
                    ?? (StructuralExtractor.FindOwnerCard(root) ?? root).SelectSingleNode(".//h3[contains(@class, 'name')]")
                    ?? (StructuralExtractor.FindOwnerCard(root) ?? root).SelectSingleNode(".//a[contains(@href, '/u/')]")
                    ?? (StructuralExtractor.FindOwnerCard(root) ?? root).SelectSingleNode(".//h5")
                    ?? (StructuralExtractor.FindOwnerCard(root) ?? root).SelectSingleNode(".//h3");
        
        var ownerName = nameNode != null ? StructuralExtractor.Normalize(HtmlEntity.DeEntitize(nameNode.InnerText)) : null;
        if (!string.IsNullOrEmpty(ownerName))
        {
            long hash = 0;
            foreach (char c in ownerName)
            {
                hash = (hash * 31) + c;
            }
            return Math.Abs(hash);
        }

        return 0;
    }

    private static readonly System.Text.RegularExpressions.Regex PercentNumberRegex =
        new(@"\d+(?:[.,]\d+)?", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Parses a percentage like "36.36%" into a rounded whole-number percent (36). Mostaql's
    /// hire-rate stat is a real decimal (e.g. "36.36%"), so a naive "keep every digit character"
    /// approach (the previous implementation) mangles it into 3636 - stripping the decimal point
    /// instead of interpreting it. Matches the leading numeric run (with an optional decimal
    /// separator) and rounds it to the nearest integer instead.
    /// </summary>
    private static int? ParsePercent(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = PercentNumberRegex.Match(StructuralExtractor.ToAsciiDigits(text));
        if (!match.Success)
        {
            return null;
        }

        var normalized = match.Value.Replace(',', '.');
        return double.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? (int)Math.Round(value, MidpointRounding.AwayFromZero)
            : null;
    }

    /// <summary>
    /// First integer appearing in the text, tolerant of Arabic-Indic numerals and of thousands
    /// separators ("1,250" / "1.250" both read as 1250) - the prototype's plain digit-run scan
    /// returned 1 for either of those and null for any Arabic-Indic number.
    /// </summary>
    private static int? ParseLeadingInt(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var ascii = StructuralExtractor.ToAsciiDigits(text);
        var match = GroupedIntRegex.Match(ascii);
        if (!match.Success)
        {
            return null;
        }

        var digits = match.Value.Replace(",", string.Empty).Replace(".", string.Empty);
        return int.TryParse(digits, out var value) ? value : null;
    }

    private static readonly System.Text.RegularExpressions.Regex GroupedIntRegex =
        new(@"\d{1,3}(?:[.,]\d{3})+|\d+", System.Text.RegularExpressions.RegexOptions.Compiled);
}

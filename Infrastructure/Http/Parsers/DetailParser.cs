using HtmlAgilityPack;
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
        ["التواصلات الجارية"] = "ongoing_conversations",
        ["بدأ تنفيذه منذ"] = "started_since",
        ["تاريخ الصفقة"] = "deal_date",
        ["موعد التسليم"] = "delivery_date",
    };

    private static readonly Dictionary<string, string> FieldToLabel =
        LabelToField.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly HashSet<string> CompletedOnlyFields = ["started_since", "deal_date", "delivery_date"];
    private const string CompletedStatusText = "مكتمل";

    private static readonly HashSet<string> NumericFields =
        ["hire_rate", "budget", "duration", "open_projects_count", "in_progress_count", "ongoing_conversations"];

    // Mirrors analyzer.NOT_CALCULATED_MARKERS / ARABIC_DIGIT_RE used by pipeline.py's
    // _is_placeholder/_sanity_ok.
    private static readonly string[] NotCalculatedMarkers = ["لم يحسب بعد", "غير محدد", "N/A", "لا يوجد"];
    private static readonly System.Text.RegularExpressions.Regex ArabicDigitRegex =
        new("[٠١٢٣٤٥٦٧٨٩]", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static ProjectDetails Parse(long projectId, string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new ParseException("DetailParser.Parse received empty HTML.");
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        var titleNode = root.SelectSingleNode("//h1");
        if (titleNode is null)
        {
            throw new ParseException($"DetailParser.Parse could not locate a title (h1) for project {projectId}.");
        }
        var title = StructuralExtractor.Normalize(HtmlEntity.DeEntitize(titleNode.InnerText));

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
            var sVal = structural.TryGetValue(label, out var sv) ? sv : labelDriven.GetValueOrDefault(label);
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
        var pageText = HtmlEntity.DeEntitize(root.InnerText) ?? string.Empty;
        foreach (var f in CompletedOnlyFields)
        {
            if (fields.TryGetValue(f, out var res) && res.Source == "inference")
            {
                var label = FieldToLabel.GetValueOrDefault(f);
                if (label is not null && !pageText.Contains(label, StringComparison.Ordinal))
                {
                    fields[f] = new FieldResolution(null, "none", 0.0);
                }
            }
        }

        // (2) Regardless of (1), these fields are only meaningful when the project is
        // actually completed.
        var statusValue = fields.GetValueOrDefault("project_status")?.Value;
        if (statusValue != CompletedStatusText)
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
            Name = ExtractOwnerName(root) ?? string.Empty,
            HiringRatePercent = ParsePercent(fields.GetValueOrDefault("hire_rate")?.Value),
            CompletedProjectsCount = ParseLeadingInt(fields.GetValueOrDefault("in_progress_count")?.Value),
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

        return new ProjectDetails
        {
            ProjectId = projectId,
            Title = title,
            Url = string.Empty,
            Description = description,
            Budget = fields.GetValueOrDefault("budget")?.Value,
            DeliveryDays = ParseLeadingInt(fields.GetValueOrDefault("duration")?.Value),
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
        return desc is not null
            ? StructuralExtractor.Normalize(HtmlEntity.DeEntitize(desc.InnerText))
            : string.Empty;
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
        if (skillsList is null)
        {
            return [];
        }

        var result = new List<ProjectSkill>();
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
                Url = link?.GetAttributeValue("href", null),
            });
        }
        return result;
    }

    private static string? ExtractOwnerName(HtmlNode root)
    {
        var ownerCard = SelectByClassContains(root, "div", "profile_card");

        var nameNode = ownerCard?.SelectNodes(".//h5")
            ?.FirstOrDefault(n => (n.GetAttributeValue("class", string.Empty))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(c => c.Contains("profile__name", StringComparison.OrdinalIgnoreCase)));

        return nameNode is not null
            ? StructuralExtractor.Normalize(HtmlEntity.DeEntitize(nameNode.InnerText))
            : null;
    }

    private static int? ParsePercent(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var digits = new string(text.Where(char.IsDigit).ToArray());
        return digits.Length > 0 && int.TryParse(digits, out var value) ? value : null;
    }

    private static int? ParseLeadingInt(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var digits = new string(text.TakeWhile(c => !char.IsDigit(c)).Any()
            ? text.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray()
            : text.TakeWhile(char.IsDigit).ToArray());

        return digits.Length > 0 && int.TryParse(digits, out var value) ? value : null;
    }
}

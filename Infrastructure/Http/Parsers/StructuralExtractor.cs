using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace MostaqlK.Infrastructure.Http.Parsers;

/// <summary>
/// A single candidate attachment link discovered on a project detail page, before it is
/// mapped into <see cref="MostaqlK.Models.Asset"/> by <see cref="DetailParser"/>.
/// </summary>
public sealed record AttachmentCandidate(
    string Filename,
    string? Extension,
    string? Url,
    string? RawUrl,
    bool RequiresAuth,
    string? SizeText);

/// <summary>
/// Ports the structural (class/id based) and label-driven (identifier-blind) extraction
/// strategies from the Python prototype <c>analyzer.py</c> to C#/HtmlAgilityPack, plus the
/// attachment-link scanning heuristic. See
/// <c>.repertoire/progress/python/parser/scratch/analyzer.py</c> for the original.
/// </summary>
public static class StructuralExtractor
{
    /// <summary>
    /// Known Arabic labels expected somewhere on a project-detail page, independent of
    /// whichever element currently wraps them. Mirrors analyzer.py's KNOWN_LABELS.
    /// </summary>
    public static readonly string[] KnownLabels =
    [
        "حالة المشروع",
        "تاريخ النشر",
        "الميزانية",
        "مدة التنفيذ",
        "المهارات",
        "تاريخ التسجيل",
        "معدل التوظيف",
        "المشاريع المفتوحة",
        "مشاريع قيد التنفيذ",
        "التواصلات الجارية",
        "بدأ تنفيذه منذ",
        "تاريخ الصفقة",
        "موعد التسليم",
        "صاحب المشروع",
    ];

    /// <summary>
    /// Simplified, hand-picked list of common file extensions used to recognize attachment
    /// links, in lieu of Python's full `mimetypes` DB (which .NET has no equivalent single
    /// source for). Covers the extensions realistically seen on Mostaql project pages.
    /// </summary>
    private static readonly HashSet<string> KnownFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "docx", "doc", "pdf", "zip", "rar", "7z", "xlsx", "xls", "pptx", "ppt",
        "psd", "ai", "png", "jpg", "jpeg", "gif", "svg", "txt", "csv", "json",
        "sql", "sketch", "fig", "mp4", "mp3", "rtf",
    };

    private static readonly Regex FilenameExtRegex = new(@"\.([A-Za-z0-9]{2,5})$", RegexOptions.Compiled);

    /// <summary>Collapse runs of whitespace to a single space and trim, mirroring Python's normalize().</summary>
    public static string Normalize(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>Text belonging directly to this node, not to nested children (mirrors own_text()).</summary>
    private static string OwnText(HtmlNode node)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                sb.Append(((HtmlTextNode)child).Text);
            }
        }
        return sb.ToString().Trim();
    }

    private static string GetText(HtmlNode node) => Normalize(HtmlEntity.DeEntitize(node.InnerText));

    // -----------------------------------------------------------------
    // Structural (class/id based) extraction
    // -----------------------------------------------------------------

    /// <summary>
    /// Mirrors structural_meta_extract: reads the meta panel rows and, when present, the
    /// owner profile card's stat table, keyed by their visible Arabic label text.
    /// </summary>
    public static Dictionary<string, string> ExtractMetaFields(HtmlNode root)
    {
        var results = new Dictionary<string, string>();

        var card = root.SelectSingleNode("//div[@id='project-meta-panel']")
                   ?? SelectByClassContains(root, "div", "meta-container").FirstOrDefault();

        if (card is not null)
        {
            foreach (var row in SelectByClassContains(card, "div", "meta-row"))
            {
                var label = SelectByClassContains(row, "div", "meta-label").FirstOrDefault();
                var value = SelectByClassContains(row, "div", "meta-value").FirstOrDefault();
                if (label is not null && value is not null)
                {
                    results[GetText(label)] = GetText(value);
                }
            }
        }

        var ownerCard = SelectByClassContains(root, "div", "profile_card").FirstOrDefault();
        if (ownerCard is not null)
        {
            var ownerStats = SelectByClassContains(ownerCard, "table", "table").FirstOrDefault();
            if (ownerStats is not null)
            {
                foreach (var tr in ownerStats.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
                {
                    var tdList = (tr.SelectNodes("./td") ?? Enumerable.Empty<HtmlNode>())
                        .Where(n => n.Name == "td").ToList();
                    if (tdList.Count == 2)
                    {
                        results[GetText(tdList[0])] = GetText(tdList[1]);
                    }
                }
            }
        }

        return results;
    }

    /// <summary>Finds descendants (self included) of the given tag whose class attribute contains the given substring.</summary>
    private static IEnumerable<HtmlNode> SelectByClassContains(HtmlNode root, string tag, string classSubstring)
    {
        var nodes = root.SelectNodes($".//{tag}") ?? Enumerable.Empty<HtmlNode>();
        foreach (var node in nodes)
        {
            var cls = node.GetAttributeValue("class", string.Empty);
            if (cls.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(c => c.Contains(classSubstring, StringComparison.OrdinalIgnoreCase)))
            {
                yield return node;
            }
        }
    }

    // -----------------------------------------------------------------
    // Label-driven (identifier-blind) extraction
    // -----------------------------------------------------------------

    /// <summary>
    /// Elements whose own/leaf text matches `label` exactly, regardless of tag/class/id.
    /// Mirrors find_label_elements.
    /// </summary>
    public static List<HtmlNode> FindLabelElements(HtmlNode root, string label)
    {
        var matches = new List<HtmlNode>();
        foreach (var el in root.SelectNodes(".//*") ?? Enumerable.Empty<HtmlNode>())
        {
            if (Normalize(OwnText(el)) == label)
            {
                matches.Add(el);
            }
            else
            {
                var fullText = GetText(el);
                if (fullText == label && !el.ChildNodes.Any(c => c.NodeType == HtmlNodeType.Element))
                {
                    matches.Add(el);
                }
            }
        }
        return matches;
    }

    /// <summary>Next sibling element of the given node, skipping non-element nodes (text/comments).</summary>
    private static HtmlNode? NextSiblingElement(HtmlNode node)
    {
        var sib = node.NextSibling;
        while (sib is not null && sib.NodeType != HtmlNodeType.Element)
        {
            sib = sib.NextSibling;
        }
        return sib;
    }

    /// <summary>
    /// Identifier-blind heuristic to find the value paired with a label, purely via DOM
    /// adjacency (not by class name). Mirrors walk_to_value.
    /// </summary>
    public static (string? Value, string? Method) WalkToValue(HtmlNode labelEl)
    {
        var sib = NextSiblingElement(labelEl);
        if (sib is not null)
        {
            var text = GetText(sib);
            if (!string.IsNullOrEmpty(text))
            {
                return (text, "next_sibling_of_label");
            }
        }

        if (labelEl.Name == "td")
        {
            var nextTd = NextSiblingElement(labelEl);
            while (nextTd is not null && nextTd.Name != "td")
            {
                nextTd = NextSiblingElement(nextTd);
            }
            if (nextTd is not null)
            {
                var text = GetText(nextTd);
                if (!string.IsNullOrEmpty(text))
                {
                    return (text, "next_td");
                }
            }
        }

        var parent = labelEl.ParentNode;
        if (parent is not null)
        {
            var pSib = NextSiblingElement(parent);
            if (pSib is not null)
            {
                var text = GetText(pSib);
                if (!string.IsNullOrEmpty(text))
                {
                    return (text, "parent_next_sibling");
                }
            }

            var parentText = GetText(parent);
            var labelText = GetText(labelEl);
            if (parentText.StartsWith(labelText, StringComparison.Ordinal) && parentText != labelText)
            {
                var remainder = Normalize(parentText[labelText.Length..]);
                if (!string.IsNullOrEmpty(remainder))
                {
                    return (remainder, "parent_text_minus_label");
                }
            }
        }

        return (null, null);
    }

    /// <summary>Mirrors label_driven_extract: resolves every known label to a value via WalkToValue.</summary>
    public static Dictionary<string, string> LabelDrivenExtract(HtmlNode root)
    {
        var results = new Dictionary<string, string>();
        foreach (var label in KnownLabels)
        {
            var els = FindLabelElements(root, label);
            if (els.Count == 0)
            {
                continue;
            }

            foreach (var el in els)
            {
                var (value, _) = WalkToValue(el);
                if (!string.IsNullOrEmpty(value))
                {
                    results[label] = value;
                    break;
                }
            }
        }
        return results;
    }

    // -----------------------------------------------------------------
    // Attachment extraction
    // -----------------------------------------------------------------

    /// <summary>
    /// Scans every &lt;a&gt; tag on the page and recognizes an attachment link via any of
    /// three independent signals - a data-file-type attribute, a sibling badge whose class
    /// merely contains "ext-file", or the file extension in the filename/title itself.
    /// Mirrors extract_attachments/_attachment_from_link.
    /// </summary>
    public static List<AttachmentCandidate> ExtractAttachments(HtmlNode root)
    {
        var attachments = new List<AttachmentCandidate>();
        var seenKeys = new HashSet<string>();

        foreach (var link in root.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
        {
            var att = AttachmentFromLink(link);
            if (att is null)
            {
                continue;
            }

            var key = att.RawUrl ?? att.Filename;
            if (!seenKeys.Add(key))
            {
                continue;
            }

            attachments.Add(att);
        }

        return attachments;
    }

    private static AttachmentCandidate? AttachmentFromLink(HtmlNode link)
    {
        var url = link.Attributes["href"]?.Value;
        var titleAttr = link.Attributes["title"]?.Value;
        var filename = Normalize(!string.IsNullOrEmpty(titleAttr) ? titleAttr : HtmlEntity.DeEntitize(link.InnerText));
        if (string.IsNullOrEmpty(filename))
        {
            return null;
        }

        var fileType = link.Attributes["data-file-type"]?.Value;

        // The <a> is nested inside an inner <li> (its own list-meta item); the badge/size
        // siblings live in OTHER list-meta items one level up, under the outer attachment
        // <li>. Prefer an ancestor <li> whose class mentions "attachment" (identifier-blind:
        // substring match, not exact), falling back to the immediate <li>/parent if none found.
        string? extBadge = null;
        var container = FindAncestorLiWithClassContaining(link, "attachment")
                        ?? FindAncestorLi(link)
                        ?? link.ParentNode;

        if (container is not null)
        {
            var badge = (container.SelectNodes(".//bdi") ?? Enumerable.Empty<HtmlNode>())
                .FirstOrDefault(t =>
                {
                    var cls = t.GetAttributeValue("class", string.Empty);
                    return cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Any(c => c.Contains("ext-file", StringComparison.OrdinalIgnoreCase));
                });
            if (badge is not null)
            {
                extBadge = GetText(badge).ToLowerInvariant();
            }
        }

        string? extFromName = null;
        var m = FilenameExtRegex.Match(filename);
        if (m.Success)
        {
            extFromName = m.Groups[1].Value.ToLowerInvariant();
        }

        var extension = fileType ?? extBadge ?? extFromName;
        if (string.IsNullOrEmpty(extension) || !KnownFileExtensions.Contains(extension))
        {
            // Not actually a downloadable-file link (e.g. a plain text link) - unless a
            // data-file-type/ext badge explicitly said so, skip it.
            if (string.IsNullOrEmpty(fileType) && string.IsNullOrEmpty(extBadge))
            {
                return null;
            }
        }

        string? sizeText = null;
        if (container is not null)
        {
            var sizeTag = container.SelectSingleNode(".//small");
            if (sizeTag is not null)
            {
                sizeText = GetText(sizeTag);
            }
        }

        var requiresAuth = !string.IsNullOrEmpty(url) && (url.Contains("/register") || url.Contains("/login"));

        return new AttachmentCandidate(
            Filename: filename,
            Extension: extension,
            Url: requiresAuth ? null : url,
            RawUrl: url,
            RequiresAuth: requiresAuth,
            SizeText: sizeText);
    }

    private static HtmlNode? FindAncestorLiWithClassContaining(HtmlNode node, string classSubstring)
    {
        var current = node.ParentNode;
        while (current is not null)
        {
            if (current.Name == "li")
            {
                var cls = current.GetAttributeValue("class", string.Empty);
                if (cls.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(c => c.Contains(classSubstring, StringComparison.OrdinalIgnoreCase)))
                {
                    return current;
                }
            }
            current = current.ParentNode;
        }
        return null;
    }

    private static HtmlNode? FindAncestorLi(HtmlNode node)
    {
        var current = node.ParentNode;
        while (current is not null)
        {
            if (current.Name == "li")
            {
                return current;
            }
            current = current.ParentNode;
        }
        return null;
    }
}

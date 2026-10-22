using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MostaqlK.Core.Utilities;

namespace MostaqlK.Core.Parsing;

/// <summary>
/// A single candidate attachment link discovered on a project detail page, before it is
/// mapped into the module's <c>Models.Asset</c> by its detail parser.
/// </summary>
public sealed record AttachmentCandidate(
    string Filename,
    string? Extension,
    string? Url,
    string? RawUrl,
    bool RequiresAuth,
    string? SizeText);

/// <summary>
/// The generic structural (class/id based) and label-driven (identifier-blind) extraction
/// strategies, ported from the Python prototype <c>analyzer.py</c> to C#/HtmlAgilityPack,
/// plus the attachment-link scanning heuristic. See
/// <c>.repertoire/progress/python/parser/scratch/analyzer.py</c> for the original.
/// Site-specific vocabulary and DOM selectors are injected as <see cref="ParseProfile"/> /
/// <see cref="SelectorMap"/> data so every source reuses this engine without copying logic.
/// </summary>
public static class HtmlStructureEngine
{
    /// <summary>Collapse runs of whitespace to a single space and trim, mirroring Python's normalize().</summary>
    public static string Normalize(string? s) => StringNormalization.Normalize(s);

    /// <summary>
    /// Converts Arabic-Indic (٠-٩) and extended/Persian Arabic-Indic (۰-۹) digits to ASCII.
    /// Sites render numbers in either form depending on the visitor's locale, so every
    /// numeric parse must run through this first - otherwise a perfectly valid "١٥ يوما"
    /// silently parses as null. The Python prototype only did this inside its
    /// analyzer/inference scoring, never in the value parsers.
    /// </summary>
    public static string ToAsciiDigits(string? s) => StringNormalization.ToAsciiDigits(s);

    /// <summary>
    /// Canonical form used to compare an on-page label against a source's known-label set.
    /// The Python prototype compared raw text for *exact* equality, so a page that renders
    /// a trailing colon, a ta-marbuta typed as ha, diacritics, or an alef variant matched
    /// nothing at all. Normalizing both sides makes label matching survive those purely
    /// orthographic variations, which is the single most common way a redesign/CMS change
    /// breaks label-driven extraction.
    /// </summary>
    public static string NormalizeLabel(string? s) => StringNormalization.NormalizeLabel(s);

    /// <summary>Block-level tags whose boundaries should become a line break when walking a node's text.</summary>
    private static readonly HashSet<string> BlockLevelTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "p", "div", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "ul", "ol",
    };

    /// <summary>
    /// Extracts a node's text the same way a browser would visually render it: unlike
    /// <c>InnerText</c> (which just concatenates every text node with no regard for block-level
    /// boundaries), this walks the DOM and turns every <c>&lt;br&gt;</c>/paragraph/div/list-item
    /// boundary into a real line break, so a scraped description with a bullet-style brief keeps
    /// its paragraph structure instead of collapsing into one run-on sentence. Fixes the
    /// description-newlines-turn-into-spaces bug. Horizontal whitespace within a line is
    /// still collapsed, and 3+ consecutive blank lines are folded down to a single paragraph gap.
    /// </summary>
    public static string NormalizeMultiline(HtmlNode node)
    {
        var sb = new System.Text.StringBuilder();
        AppendMultilineText(node, sb);

        var text = HtmlEntity.DeEntitize(sb.ToString());
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"[ \t]*\r?\n[ \t]*", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static void AppendMultilineText(HtmlNode node, System.Text.StringBuilder sb)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                sb.Append(((HtmlTextNode)child).Text);
            }
            else if (child.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append('\n');
            }
            else if (child.NodeType == HtmlNodeType.Element)
            {
                AppendMultilineText(child, sb);
                if (BlockLevelTags.Contains(child.Name))
                {
                    sb.Append('\n');
                }
            }
        }
    }

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
    /// owner profile card's stat table. Keys are <see cref="NormalizeLabel"/>-canonicalized
    /// (not the raw visible text like the Python prototype) so a page that renders a label
    /// with a trailing colon still resolves against <c>profile.KnownLabels</c>.
    /// </summary>
    public static Dictionary<string, string> ExtractMetaFields(HtmlNode root, ParseProfile profile, SelectorMap selectors)
    {
        var knownLabels = profile.KnownLabels;
        var results = new Dictionary<string, string>();

        var card = root.SelectSingleNode($"//div[@id='{selectors.MetaPanelId}']")
                   ?? SelectByClassContains(root, "div", selectors.MetaContainerClassSubstring).FirstOrDefault();

        if (card is not null)
        {
            foreach (var row in SelectByClassContains(card, "div", selectors.MetaRowClassSubstring))
            {
                var label = SelectByClassContains(row, "div", selectors.MetaLabelClassSubstring).FirstOrDefault();
                var value = SelectByClassContains(row, "div", selectors.MetaValueClassSubstring).FirstOrDefault();
                if (label is not null && value is not null)
                {
                    results[NormalizeLabel(GetText(label))] = GetText(value);
                }
            }
        }

        var ownerCard = FindOwnerCard(root, selectors, profile.OwnerLabel);
        if (ownerCard is not null)
        {
            var ownerStats = SelectByClassContains(ownerCard, "table", selectors.OwnerStatsTableClassSubstring).FirstOrDefault();
            if (ownerStats is not null)
            {
                foreach (var tr in ownerStats.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
                {
                    var tdList = (tr.SelectNodes("./td") ?? Enumerable.Empty<HtmlNode>())
                        .Where(n => n.Name == "td").ToList();
                    if (tdList.Count == 2)
                    {
                        results[NormalizeLabel(GetText(tdList[0]))] = GetText(tdList[1]);
                    }
                }
            }
            else
            {
                // REDESIGN: some sites moved away from <table> for profile stats to a flex/grid
                // of div.justify-between or similar. Find all elements that look like 
                // label-value pairs inside the profile card.
                foreach (var row in ownerCard.SelectNodes($".//*[contains(@class, '{selectors.OwnerStatRowClassSubstring}')]") ?? Enumerable.Empty<HtmlNode>())
                {
                    // Find all element children. If we have exactly 2, it's likely [label, value].
                    // If more or less, try a different heuristic.
                    var children = row.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element).ToList();
                    if (children.Count == 2)
                    {
                        var label = GetText(children[0]);
                        var value = GetText(children[1]);
                        if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(value))
                        {
                            results[NormalizeLabel(label)] = value;
                        }
                    }
                    else if (children.Count == 0)
                    {
                        // Some structures might put label and value in the same element's text but with spans inside.
                        // Or if children are empty but there is text, it's not a pair.
                    }
                }

                // SECONDARY HEURISTIC: Sometimes the labels are not direct children of the stat-row
                // class, or they are concatenated with their values in the same node's text.
                // We always run this to fill in any missing labels that the primary heuristic missed.
                foreach (var node in ownerCard.SelectNodes(".//*") ?? Enumerable.Empty<HtmlNode>())
                {
                    var text = GetText(node);
                    if (string.IsNullOrEmpty(text)) continue;

                    // Check for exact matches of labels
                    var norm = NormalizeLabel(text);
                    if (knownLabels.Any(l => NormalizeLabel(l) == norm))
                    {
                        if (!results.ContainsKey(norm))
                        {
                            // This node is a label. Its value is likely the next sibling element or parent's second child.
                            var next = NextSiblingElement(node);
                            if (next != null)
                            {
                                results[norm] = GetText(next);
                            }
                        }
                    }
                    else
                    {
                        // Check for concatenated label + value (e.g. a rate label glued to its percentage)
                        foreach (var label in knownLabels)
                        {
                            var normalizedLabel = NormalizeLabel(label);
                            var normalizedText = NormalizeLabel(text);

                            if (!results.ContainsKey(normalizedLabel) && normalizedText.StartsWith(normalizedLabel) && normalizedText.Length > normalizedLabel.Length)
                            {
                                // Try to extract the value from the remaining text
                                var rawText = text.Trim();
                                // Find where the label ends in the raw text. 
                                // This is tricky with Arabic, so we'll just take the part that isn't the label.
                                if (rawText.Contains(label))
                                {
                                    var value = rawText.Replace(label, "").Trim(StringNormalization.LabelTrimChars);
                                    if (!string.IsNullOrEmpty(value))
                                    {
                                        results[normalizedLabel] = value;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Attempts to locate the owner profile card/section on the page using both class-based
    /// and label-driven heuristics.
    /// </summary>
    public static HtmlNode? FindOwnerCard(HtmlNode root, SelectorMap selectors, string ownerLabel)
    {
        // 1. Structural: sites often use specific profile-card classes.
        HtmlNode? card = null;
        foreach (var substring in selectors.OwnerCardClassSubstrings)
        {
            card = SelectByClassContains(root, "div", substring).FirstOrDefault();
            if (card is not null) break;
        }
        if (card is not null) return card;

        // 2. Semantic: Look for the owner-section header (the profile-owner label).
        var labels = FindLabelElements(root, ownerLabel);
        foreach (var label in labels)
        {
            // Walk up to find a container that has siblings (the stats).
            // Usually it's the immediate parent div in the new design.
            var candidate = label.Ancestors("div").FirstOrDefault();
            if (candidate != null)
            {
                // Ensure this container actually has content other than the label
                var descendantCount = candidate.SelectNodes(".//*")?.Count ?? 0;
                if (descendantCount > 1)
                {
                    return candidate;
                }
            }
        }

        // 3. Last resort: If we're inside the project details area, and there's a card 
        // that looks like a profile, use it.
        var detailsArea = root.SelectSingleNode($"//*[@id='{selectors.DetailsTabId}']")
                       ?? root.SelectSingleNode($"//div[contains(@class, '{selectors.ProjectDetailsClassSubstring}')]")
                       ?? root.SelectSingleNode("//article");

        if (detailsArea != null)
        {
            var profileLink = detailsArea.SelectSingleNode($".//a[contains(@href, '{selectors.ProfileUrlPathSegment}')]");
            if (profileLink != null)
            {
                // Walk up to find the card wrapping this link
                var cardCandidate = profileLink.Ancestors("div")
                    .FirstOrDefault(d =>
                    {
                        var cls = d.GetAttributeValue("class", string.Empty);
                        return selectors.OwnerCardWrapperClassSubstrings.Any(sub => cls.Contains(sub));
                    });
                if (cardCandidate != null) return cardCandidate;
            }
        }

        return null;
    }

    /// <summary>Finds descendants (self included) of the given tag whose class attribute contains the given substring.</summary>
    public static IEnumerable<HtmlNode> SelectByClassContains(HtmlNode root, string tag, string classSubstring)
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
        // Compared through NormalizeLabel on BOTH sides (see its docs) so trailing colons,
        // diacritics and alef/ya/ta-marbuta spelling variants still match - the Python
        // prototype's raw exact-equality check missed all of those.
        var target = NormalizeLabel(label);
        var matches = new List<HtmlNode>();
        foreach (var el in root.SelectNodes(".//*") ?? Enumerable.Empty<HtmlNode>())
        {
            if (NormalizeLabel(OwnText(el)) == target)
            {
                matches.Add(el);
            }
            else
            {
                var fullText = NormalizeLabel(GetText(el));
                if (fullText == target && !el.ChildNodes.Any(c => c.NodeType == HtmlNodeType.Element))
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
                var remainder = Normalize(parentText[labelText.Length..].TrimStart(StringNormalization.LabelTrimChars));
                if (!string.IsNullOrEmpty(remainder))
                {
                    return (remainder, "parent_text_minus_label");
                }
            }

            // Beyond the Python prototype: when the label and its value are laid out as two
            // cells of the SAME row wrapper (label is the first child, value the second) but
            // the label element itself is wrapped one level deeper - a very common redesign
            // shape - neither next_sibling nor parent_next_sibling sees it. Walk the label's
            // grandparent children and take the first element that isn't the label's own
            // ancestor chain.
            var grandparent = parent.ParentNode;
            if (grandparent is not null)
            {
                foreach (var child in grandparent.ChildNodes)
                {
                    if (child.NodeType != HtmlNodeType.Element || ReferenceEquals(child, parent))
                    {
                        continue;
                    }
                    var text = GetText(child);
                    if (!string.IsNullOrEmpty(text) && NormalizeLabel(text) != NormalizeLabel(labelText))
                    {
                        return (text, "grandparent_sibling_cell");
                    }
                }
            }
        }

        return (null, null);
    }

    /// <summary>Mirrors label_driven_extract: resolves every known label to a value via WalkToValue.</summary>
    public static Dictionary<string, string> LabelDrivenExtract(HtmlNode root, IReadOnlyList<string> knownLabels)
    {
        var results = new Dictionary<string, string>();
        foreach (var label in knownLabels)
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
                    results[NormalizeLabel(label)] = value;
                    break;
                }
            }
        }
        return results;
    }

    // -----------------------------------------------------------------
    // Attachment extraction
    // -----------------------------------------------------------------

    private static readonly Regex FilenameExtRegex = new(@"\.([A-Za-z0-9]{2,5})$", RegexOptions.Compiled);

    /// <summary>
    /// Scans every &lt;a&gt; tag on the page and recognizes an attachment link via any of
    /// three independent signals - a data-file-type attribute, a sibling badge whose class
    /// merely contains the extension-badge substring, or the file extension in the
    /// filename/title itself. Mirrors extract_attachments/_attachment_from_link.
    /// </summary>
    public static List<AttachmentCandidate> ExtractAttachments(HtmlNode root, ParseProfile profile, SelectorMap selectors)
    {
        var attachments = new List<AttachmentCandidate>();
        var seenKeys = new HashSet<string>();

        foreach (var link in root.SelectNodes(".//a") ?? Enumerable.Empty<HtmlNode>())
        {
            var att = AttachmentFromLink(link, profile, selectors);
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

    private static AttachmentCandidate? AttachmentFromLink(HtmlNode link, ParseProfile profile, SelectorMap selectors)
    {
        var url = link.Attributes["href"]?.Value;
        var titleAttr = link.Attributes["title"]?.Value;
        var filename = Normalize(!string.IsNullOrEmpty(titleAttr) ? titleAttr : HtmlEntity.DeEntitize(link.InnerText));
        if (string.IsNullOrEmpty(filename))
        {
            return null;
        }

        var fileType = link.Attributes[selectors.AttachmentFileTypeAttribute]?.Value;

        // The <a> is nested inside an inner <li> (its own list-meta item); the badge/size
        // siblings live in OTHER list-meta items one level up, under the outer attachment
        // <li>. Prefer an ancestor <li> whose class mentions the attachment substring
        // (identifier-blind: substring match, not exact), falling back to the immediate
        // <li>/parent if none found.
        string? extBadge = null;
        var container = FindAncestorLiWithClassContaining(link, selectors.AttachmentItemClassSubstring)
                        ?? FindAncestorLi(link)
                        ?? link.ParentNode;

        if (container is not null)
        {
            var badge = (container.SelectNodes(".//bdi") ?? Enumerable.Empty<HtmlNode>())
                .FirstOrDefault(t =>
                {
                    var cls = t.GetAttributeValue("class", string.Empty);
                    return cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Any(c => c.Contains(selectors.AttachmentBadgeClassSubstring, StringComparison.OrdinalIgnoreCase));
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
        if (string.IsNullOrEmpty(extension) || !profile.KnownFileExtensions.Contains(extension))
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

        var requiresAuth = !string.IsNullOrEmpty(url) &&
            profile.AttachmentAuthUrlSubstrings.Any(fragment => url.Contains(fragment));

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

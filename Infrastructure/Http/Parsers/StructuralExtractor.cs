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
    // ==========================================
    // 1. DOCUMENTS & OFFICE SUITES
    // ==========================================
    // Microsoft Office & Standards
    "pdf", "doc", "docx", "docm", "dot", "dotx", "dotm", "rtf", "txt", "odt", "ott", "md", "markdown", "log", "tex", "latex", "wps", "pages",
    // Spreadsheets
    "xls", "xlsx", "xlsm", "xltx", "xltm", "xlsb", "xlam", "csv", "tsv", "ods", "ots", "numbers",
    // Presentations
    "ppt", "pptx", "pptm", "pot", "potx", "potm", "pps", "ppsx", "ppsm", "odp", "otp", "key",

    // ==========================================
    // 2. RASTER GRAPHICS & DIGITAL PHOTOS
    // ==========================================
    // Standard Raster & Web
    "jpg", "jpeg", "png", "gif", "webp", "bmp", "tif", "tiff", "ico", "cur", "avif", "heic", "heif", "jxl", "pbm", "pgm", "ppm", "pnm",
    // Camera RAW Formats
    "raw", "cr2", "cr3", "nef", "nrw", "arw", "srf", "sr2", "dng", "orf", "rw2", "pef", "raf", "kdc", "erf", "mrw",

    // ==========================================
    // 3. VECTOR & BRAND DESIGN
    // ==========================================
    "svg", "svgz", "ai", "eps", "psd", "psb", "sketch", "fig", "xd", "indd", "indt", "idml", "cdr", "cmx", "wmf", "emf",
    // Affinity Suite
    "afdesign", "afphoto", "afpub",

    // ==========================================
    // 4. VIDEO & MOTION GRAPHICS
    // ==========================================
    // Video Containers
    "mp4", "mov", "avi", "mkv", "webm", "flv", "f4v", "wmv", "m4v", "3gp", "3g2", "ogv", "vob", "mts", "m2ts", "ts", "asf", "rm", "rmvb",
    // Video Project Files
    "aep", "aepx", "prproj", "drp", "veg", "vf", // Premiere, After Effects, DaVinci, Vegas

    // ==========================================
    // 5. AUDIO & MUSIC PRODUCTION
    // ==========================================
    // Compressed & Uncompressed Audio
    "mp3", "wav", "aac", "flac", "ogg", "oga", "m4a", "wma", "aiff", "aif", "alac", "opus", "mid", "midi", "amr",
    // DAW Project Files
    "als", "flp", "logicx", "cpr", "ptx", "rg", // Ableton, FL Studio, Logic, Cubase, Pro Tools

    // ==========================================
    // 6. 3D MODELING, ANIMATION & CAD
    // ==========================================
    "dwg", "dxf", "stl", "obj", "fbx", "gltf", "glb", "blend", "c4d", "max", "3ds",
    "step", "stp", "iges", "igs", "ply", "dae", "usdz", "usda", "usdc", "skp", "3dm", "x3d",

    // ==========================================
    // 7. SOURCE CODE, SCRIPTS & WEB DEVELOPMENT
    // ==========================================
    // Web Development
    "html", "htm", "xhtml", "css", "scss", "sass", "less", "js", "mjs", "cjs", "ts", "jsx", "tsx", "vue", "svelte", "php",
    // Programming Languages
    "py", "pyw", "cs", "csx", "cpp", "cxx", "cc", "c", "h", "hpp", "java", "class", "go", "rs", "rb", "swift", "kt", "kts",
    "dart", "lua", "r", "scala", "clj", "ex", "exs", "erl", "hs", "pl", "pm", "sh", "bash", "zsh", "bat", "cmd", "ps1", "psm1", "vbs",

    // ==========================================
    // 8. DATA, CONFIGURATIONS & DATABASES
    // ==========================================
    // Data Formats
    "json", "json5", "jsonl", "xml", "yaml", "yml", "ini", "env", "toml", "conf", "config", "properties",
    // Databases & Dumps
    "sql", "sqlite", "sqlite3", "db", "db3", "mdb", "accdb", "bak", "dump", "mdf", "ldf",

    // ==========================================
    // 9. ARCHIVES, COMPRESSION & DISK IMAGES
    // ==========================================
    "zip", "rar", "7z", "tar", "gz", "tgz", "bz2", "tbz2", "xz", "txz", "z", "iso", "img", "vmdk", "vhd", "vhdx", "cab", "deb", "rpm",

    // ==========================================
    // 10. EXECUTABLES, INSTALLERS & PACKAGES
    // ==========================================
    "exe", "msi", "apk", "aab", "ipa", "dmg", "pkg", "appimage", "flatpak", "snap", "jar", "war", "ear", "dll", "so", "dylib",

    // ==========================================
    // 11. GIS, MAPPING & GEOSPATIAL
    // ==========================================
    "kml", "kmz", "gpx", "geojson", "shp", "shx", "dbf", "prj",

    // ==========================================
    // 12. DIGITAL PUBLISHING, FONTS & MISC
    // ==========================================
    // E-Books & Documents
    "epub", "mobi", "azw", "azw3", "djvu", "cbz", "cbr",
    // Fonts
    "ttf", "otf", "woff", "woff2", "eot", "fon"
};
    private static readonly Regex FilenameExtRegex = new(@"\.([A-Za-z0-9]{2,5})$", RegexOptions.Compiled);

    /// <summary>Collapse runs of whitespace to a single space and trim, mirroring Python's normalize().</summary>
    public static string Normalize(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : Regex.Replace(s, @"\s+", " ").Trim();

    private const string ArabicIndicDigits = "٠١٢٣٤٥٦٧٨٩";
    private const string ExtendedArabicIndicDigits = "۰۱۲۳۴۵۶۷۸۹";

    /// <summary>
    /// Converts Arabic-Indic (٠-٩) and extended/Persian Arabic-Indic (۰-۹) digits to ASCII.
    /// Mostaql renders numbers in either form depending on the visitor's locale, so every
    /// numeric parse in <see cref="DetailParser"/> runs through this first - otherwise a
    /// perfectly valid "١٥ يوما" silently parses as null. The Python prototype only did this
    /// inside its analyzer/inference scoring, never in the value parsers.
    /// </summary>
    public static string ToAsciiDigits(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            var idx = ArabicIndicDigits.IndexOf(c);
            if (idx < 0)
            {
                idx = ExtendedArabicIndicDigits.IndexOf(c);
            }
            sb.Append(idx >= 0 ? (char)('0' + idx) : c);
        }
        return sb.ToString();
    }

    private static readonly Regex ArabicDiacriticsRegex = new(@"[\u064B-\u065F\u0670\u0640]", RegexOptions.Compiled);
    private static readonly char[] LabelTrimChars = [':', '\uFF1A', '\u061B', ';', '.', '\u060C', ',', '-', '\u2013', '\u2014', ' '];

    /// <summary>
    /// Canonical form used to compare an on-page label against <see cref="KnownLabels"/>.
    /// The Python prototype compared raw text for *exact* equality, so a page that renders
    /// "الميزانية:" (trailing colon), "الميزانيه" (ta-marbuta typed as ha), "الميزانيّة"
    /// (with diacritics) or "إلميزانية" (alef variant) matched nothing at all. Normalizing
    /// both sides makes label matching survive those purely orthographic variations, which is
    /// the single most common way a redesign/CMS change breaks label-driven extraction.
    /// </summary>
    public static string NormalizeLabel(string? s)
    {
        var text = Normalize(s);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        text = ArabicDiacriticsRegex.Replace(text, string.Empty);
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => c,
            });
        }

        return sb.ToString().Trim(LabelTrimChars);
    }

    /// <summary>Block-level tags whose boundaries should become a line break when walking a node's text.</summary>
    private static readonly HashSet<string> BlockLevelTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "p", "div", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "ul", "ol",
    };

    /// <summary>
    /// Extracts a node's text the same way a browser would visually render it: unlike
    /// <c>InnerText</c> (which just concatenates every text node with no regard for block-level
    /// boundaries), this walks the DOM and turns every <c>&lt;br&gt;</c>/paragraph/div/list-item
    /// boundary into a real line break, so a scraped description like Mostaql's "المهام:" bullet
    /// list keeps its paragraph structure instead of collapsing into one run-on sentence. Fixes
    /// the description-newlines-turn-into-spaces bug. Horizontal whitespace within a line is
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
    /// (not the raw visible text like the Python prototype) so a page that renders
    /// "الميزانية:" still resolves against <see cref="KnownLabels"/>.
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
                    results[NormalizeLabel(GetText(label))] = GetText(value);
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
                        results[NormalizeLabel(GetText(tdList[0]))] = GetText(tdList[1]);
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
                var remainder = Normalize(parentText[labelText.Length..].TrimStart(LabelTrimChars));
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

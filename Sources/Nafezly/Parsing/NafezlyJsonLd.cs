using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MostaqlK.Core.Parsing;

namespace MostaqlK.Sources.Nafezly.Parsing;

/// <summary>
/// The machine-readable <c>schema.org/JobPosting</c> block nafezly ships in every detail page's
/// <c>&lt;head&gt;</c> (capture-manifest §1.2). This is the HIGHEST-SIGNAL source and is read FIRST,
/// before the "بطاقة المشروع" label rows or the inference engine. Nafezly emits it with embedded raw
/// newlines and whitespace-mangled date values, so this extractor tolerates that rather than trusting
/// a strict JSON reader.
/// </summary>
public sealed record NafezlyJobPosting(
    string? Title,
    string? Description,
    IReadOnlyList<string> Skills,
    int? WorkHours,
    string? DatePosted,
    string? OccupationalCategory)
{
    public static NafezlyJobPosting Empty { get; } =
        new(null, null, [], null, null, null);
}

/// <summary>
/// Pulls the <see cref="NafezlyJobPosting"/> out of a nafezly detail page's JSON-LD scripts.
/// Source-specific extraction (there is no shared engine for schema.org), so it lives entirely in
/// the module — it never duplicates any <c>Core/Parsing</c> behavior.
/// </summary>
public static class NafezlyJsonLd
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Finds the first <c>application/ld+json</c> script describing a <c>JobPosting</c> (handling both
    /// a bare object and an <c>@graph</c> array), and projects the fields the module consumes. Returns
    /// <see cref="NafezlyJobPosting.Empty"/> when no such block exists.
    /// </summary>
    public static NafezlyJobPosting Extract(HtmlNode root)
    {
        foreach (var script in root.SelectNodes("//script[@type='application/ld+json']")
                     ?? Enumerable.Empty<HtmlNode>())
        {
            var raw = script.InnerText;
            if (string.IsNullOrWhiteSpace(raw) || !raw.Contains("JobPosting", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryParseTolerant(raw, out var document))
            {
                continue;
            }

            using (document)
            {
                var posting = FindJobPosting(document!.RootElement);
                if (posting is not null)
                {
                    return posting;
                }
            }
        }

        return NafezlyJobPosting.Empty;
    }

    /// <summary>
    /// System.Text.Json rejects raw control characters (newlines/tabs) inside string LITERALS, and
    /// nafezly's <c>description</c>/<c>datePosted</c> values contain them. Before parsing we escape any
    /// unescaped control byte that appears inside a JSON string, leaving structure whitespace intact.
    /// </summary>
    private static bool TryParseTolerant(string raw, out JsonDocument? document)
    {
        document = null;
        var sanitized = EscapeInStringLiterals(raw);
        try
        {
            document = JsonDocument.Parse(sanitized, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            return true;
        }
        catch (JsonException)
        {
            document?.Dispose();
            document = null;
            return false;
        }
    }

    private static string EscapeInStringLiterals(string json)
    {
        var sb = new StringBuilder(json.Length + 16);
        var inString = false;
        var escape = false;
        foreach (var c in json)
        {
            if (inString)
            {
                if (escape)
                {
                    sb.Append(c);
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    sb.Append(c);
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append(c);
                    inString = false;
                    continue;
                }

                // Escape raw control bytes so the strict reader accepts them.
                switch (c)
                {
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("X4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static NafezlyJobPosting? FindJobPosting(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (IsType(element, "JobPosting"))
                {
                    return Project(element);
                }

                if (element.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
                {
                    foreach (var node in graph.EnumerateArray())
                    {
                        var found = FindJobPosting(node);
                        if (found is not null)
                        {
                            return found;
                        }
                    }
                }

                return null;

            case JsonValueKind.Array:
                foreach (var node in element.EnumerateArray())
                {
                    var found = FindJobPosting(node);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static bool IsType(JsonElement element, string wanted)
    {
        if (!element.TryGetProperty("@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => string.Equals(type.GetString(), wanted, StringComparison.Ordinal),
            JsonValueKind.Array => type.EnumerateArray().Any(t =>
                t.ValueKind == JsonValueKind.String && string.Equals(t.GetString(), wanted, StringComparison.Ordinal)),
            _ => false,
        };
    }

    private static NafezlyJobPosting Project(JsonElement posting)
    {
        var skills = GetString(posting, "skills");
        return new NafezlyJobPosting(
            Title: Normalize(GetString(posting, "title")),
            Description: NormalizeMultiline(GetString(posting, "description")),
            Skills: SplitSkills(skills),
            // workHours is actually DAYS on nafezly (§1.2: workHours 3 ↔ "المدة المتاحة 3 أيام").
            WorkHours: ParseInt(GetString(posting, "workHours")),
            DatePosted: NormalizeDate(GetString(posting, "datePosted")),
            OccupationalCategory: Normalize(GetString(posting, "occupationalCategory")));
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> SplitSkills(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(HtmlStructureEngine.Normalize)
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static int? ParseInt(string? text)
    {
        var ascii = HtmlStructureEngine.ToAsciiDigits(Normalize(text));
        return int.TryParse(ascii, out var value) ? value : null;
    }

    /// <summary>Collapse all whitespace (newlines + runs of spaces) into single spaces and trim.</summary>
    private static string? Normalize(string? text) =>
        text is null ? null : HtmlStructureEngine.Normalize(text);

    /// <summary>
    /// Turn nafezly's mangled <c>"\n    2026 - 10 - 22 "</c> into the compact <c>2026-10-22</c>: strip
    /// every space around the separators so the ISO date survives the site's pretty-printing (§1.2).
    /// </summary>
    private static string? NormalizeDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var compact = Whitespace.Replace(text, "");
        return compact.Length == 0 ? null : compact;
    }

    /// <summary>
    /// Keep the JSON-LD brief's paragraph structure (newlines between sections) but normalize the
    /// surrounding whitespace — mirrors the multiline handling the Mostaql description path uses.
    /// </summary>
    private static string? NormalizeMultiline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @"[ ]*\n[ ]*", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }
}

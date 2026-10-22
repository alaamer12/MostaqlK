using HtmlAgilityPack;
using MostaqlK.Core.Parsing;

namespace MostaqlK.Sources.Mostaql.Parsing;

/// <summary>
/// Thin Mostaql adapter over the generic <see cref="HtmlStructureEngine"/> (moved out to
/// <c>Core/Parsing</c> in the A1 generalization). The engines hold all heuristics; this facade
/// only injects Mostaql's <see cref="MostaqlParseProfile"/>/<see cref="MostaqlSelectors"/>
/// data, keeping the pre-generalization public surface (and its consumers, e.g. the
/// ParserTests harness) working with zero behavior difference.
/// </summary>
public static class StructuralExtractor
{
    /// <summary>
    /// Known Arabic labels expected somewhere on a project-detail page, independent of
    /// whichever element currently wraps them. Mostaql's data — see <see cref="MostaqlParseProfile"/>.
    /// </summary>
    public static IReadOnlyList<string> KnownLabels => MostaqlParseProfile.Profile.KnownLabels;

    /// <summary>Collapse runs of whitespace to a single space and trim, mirroring Python's normalize().</summary>
    public static string Normalize(string? s) => HtmlStructureEngine.Normalize(s);

    /// <summary>Converts Arabic-Indic/extended digits to ASCII (see <see cref="HtmlStructureEngine.ToAsciiDigits"/>).</summary>
    public static string ToAsciiDigits(string? s) => HtmlStructureEngine.ToAsciiDigits(s);

    /// <summary>Orthographic-canonical form of a label (see <see cref="HtmlStructureEngine.NormalizeLabel"/>).</summary>
    public static string NormalizeLabel(string? s) => HtmlStructureEngine.NormalizeLabel(s);

    /// <summary>Browser-visual text extraction with real line breaks (see <see cref="HtmlStructureEngine.NormalizeMultiline"/>).</summary>
    public static string NormalizeMultiline(HtmlNode node) => HtmlStructureEngine.NormalizeMultiline(node);

    /// <summary>Structural meta-panel + owner-card extraction using Mostaql's selector vocabulary.</summary>
    public static Dictionary<string, string> ExtractMetaFields(HtmlNode root) =>
        HtmlStructureEngine.ExtractMetaFields(root, MostaqlParseProfile.Profile, MostaqlSelectors.Selectors);

    /// <summary>Locates the owner profile card using Mostaql's selector vocabulary and owner label.</summary>
    public static HtmlNode? FindOwnerCard(HtmlNode root) =>
        HtmlStructureEngine.FindOwnerCard(root, MostaqlSelectors.Selectors, MostaqlParseProfile.Profile.OwnerLabel);

    /// <summary>Elements whose own/leaf text matches `label` exactly, regardless of tag/class/id.</summary>
    public static List<HtmlNode> FindLabelElements(HtmlNode root, string label) =>
        HtmlStructureEngine.FindLabelElements(root, label);

    /// <summary>Identifier-blind adjacency walk from a label element to its value.</summary>
    public static (string? Value, string? Method) WalkToValue(HtmlNode labelEl) =>
        HtmlStructureEngine.WalkToValue(labelEl);

    /// <summary>Resolves every known Mostaql label to a value via adjacency walking.</summary>
    public static Dictionary<string, string> LabelDrivenExtract(HtmlNode root) =>
        HtmlStructureEngine.LabelDrivenExtract(root, MostaqlParseProfile.Profile.KnownLabels);

    /// <summary>Attachment-link scan using Mostaql's file-extension set, auth URL shapes, and selectors.</summary>
    public static List<AttachmentCandidate> ExtractAttachments(HtmlNode root) =>
        HtmlStructureEngine.ExtractAttachments(root, MostaqlParseProfile.Profile, MostaqlSelectors.Selectors);
}

namespace MostaqlK.Core.Parsing;

/// <summary>
/// Per-source inference scoring profile for one field key — the data half of what
/// inference.py's <c>FIELD_PROFILES</c> dict held. Pure data, no behavior: the generic
/// <see cref="FieldInferenceEngine"/> consumes it unchanged for any source.
/// Mirrors the old InferenceEngine.FieldProfile class exactly (same members, same
/// empty-collection defaults); <see cref="ExpectedTypesWeak"/> defaults to empty just
/// like the old initializer did.
/// </summary>
public sealed record FieldProfileData
{
    public required IReadOnlyList<string> CoreStems { get; init; }
    public required IReadOnlySet<string> ExpectedTypes { get; init; }
    public IReadOnlySet<string> ExpectedTypesWeak { get; init; } = new HashSet<string>();
    public required IReadOnlyList<string> UnitHints { get; init; }
    public required bool RequiresUnit { get; init; }
}

/// <summary>One (field key, profile) pair. List order = the old dictionary's insertion order, which the engine preserves.</summary>
public sealed record FieldProfileEntry(string Field, FieldProfileData Profile);

/// <summary>
/// A source's complete field-profile table, passed into <see cref="FieldInferenceEngine.InferFields"/>.
/// Ordered list rather than a dictionary so enumeration order is contractual, matching the old
/// <c>Dictionary</c>-insertion-order iteration the scoring and resolution steps relied on.
/// </summary>
public sealed record FieldProfileTable(IReadOnlyList<FieldProfileEntry> Entries);

/// <summary>
/// Per-source text vocabulary the generic parsers match against — everything a site "says"
/// (labels, statuses, currency/proposal words, file extensions, auth URL shapes, title
/// suffixes) as immutable data. No behavior lives here; the engines and the module's thin
/// parsers read it. A second source supplies its own instance and reuses every engine as-is.
/// </summary>
/// <param name="KnownLabels">Arabic labels expected somewhere on a detail page, in the order
/// label-driven extraction walks them (was StructuralExtractor.KnownLabels).</param>
/// <param name="FieldProfiles">Inference scoring table (was InferenceEngine.FieldProfiles).</param>
/// <param name="LabelToField">Label → inference field key map (was DetailParser.LabelToField);
/// insertion order is preserved because the thin parser derives its synonym groups from it.</param>
/// <param name="CompletedOnlyFields">Field keys only meaningful on a finished project
/// (was DetailParser.CompletedOnlyFields).</param>
/// <param name="NumericFields">Field keys whose sanity check requires a digit
/// (was DetailParser.NumericFields).</param>
/// <param name="CompletedStatusText">The status substring that marks a finished project
/// (was DetailParser.CompletedStatusText).</param>
/// <param name="NotCalculatedMarkers">Placeholder texts that resolve to null
/// (was DetailParser.NotCalculatedMarkers).</param>
/// <param name="TitleSiteKeywords">Site-name suffixes stripped from document titles
/// (was DetailParser.SiteKeywords).</param>
/// <param name="TitleSuffixSeparators">Separators preceding a title site keyword
/// (was DetailParser.SiteSuffixSeparators).</param>
/// <param name="AttachmentAuthUrlSubstrings">URL fragments marking an attachment link as
/// session-gated (was the inline /register, /login, /download/ and the host-rooted
/// attachments-path check in StructuralExtractor).</param>
/// <param name="KnownFileExtensions">Extensions recognized as downloadable files
/// (was StructuralExtractor.KnownFileExtensions).</param>
/// <param name="OwnerLabel">The "project owner" section label on a detail page
/// (was the inline "صاحب المشروع" reach into FindLabelElements/labelDriven).</param>
/// <param name="ProposalCountSuffix">Suffix appended to a counted bid total
/// (was the inline " عروض" interpolation in DetailParser).</param>
/// <param name="ProposalWords">Words marking a proposal-count text on a listing row
/// (was ListingParser.LooksLikeProposalCount).</param>
/// <param name="RelativeTimeWords">Words marking a published-relative-time text on a
/// listing row (was the inline since/hour/day/moment Contains checks in ListingParser).</param>
public sealed record ParseProfile(
    IReadOnlyList<string> KnownLabels,
    FieldProfileTable FieldProfiles,
    IReadOnlyDictionary<string, string> LabelToField,
    IReadOnlyList<string> CompletedOnlyFields,
    IReadOnlySet<string> NumericFields,
    string CompletedStatusText,
    IReadOnlyList<string> NotCalculatedMarkers,
    IReadOnlyList<string> TitleSiteKeywords,
    IReadOnlyList<string> TitleSuffixSeparators,
    IReadOnlyList<string> AttachmentAuthUrlSubstrings,
    IReadOnlySet<string> KnownFileExtensions,
    string OwnerLabel,
    string ProposalCountSuffix,
    IReadOnlyList<string> ProposalWords,
    IReadOnlyList<string> RelativeTimeWords)
{
    /// <summary>
    /// An all-empty profile for non-parsing consumers (test stubs, placeholder descriptors).
    /// Never used by a live source — real sources ship their vocabulary as data.
    /// </summary>
    public static ParseProfile Empty { get; } = new(
        KnownLabels: [],
        FieldProfiles: new FieldProfileTable([]),
        LabelToField: new Dictionary<string, string>(),
        CompletedOnlyFields: [],
        NumericFields: new HashSet<string>(),
        CompletedStatusText: string.Empty,
        NotCalculatedMarkers: [],
        TitleSiteKeywords: [],
        TitleSuffixSeparators: [],
        AttachmentAuthUrlSubstrings: [],
        KnownFileExtensions: new HashSet<string>(),
        OwnerLabel: string.Empty,
        ProposalCountSuffix: string.Empty,
        ProposalWords: [],
        RelativeTimeWords: []);
}

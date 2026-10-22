using MostaqlK.Core.Parsing;

namespace MostaqlK.Sources.Nafezly;

/// <summary>
/// The Nafezly module's parse-text vocabulary as immutable <see cref="ParseProfile"/> data, derived
/// from the A2 capture manifest. The shared <c>Core/Parsing</c> engines consume it unchanged, so a
/// nafezly page is analysed through the exact same structural/label/inference machinery Mostaql
/// uses — only the words differ. The JSON-LD JobPosting block is the primary structured source
/// (parsed directly in <c>NafezlyDetailParser</c>); this profile drives the label-row fallback and
/// inference-last tiers for the meta-panel facts (status / budget / duration / proposals / publish).
/// </summary>
public static class NafezlyParseProfile
{
    /// <summary>Unit hints for duration/publish — built from the engine's own shared Arabic arrays.</summary>
    private static readonly string[] RelativeUnitHints =
        FieldInferenceEngine.RelativeDateWords.Concat(FieldInferenceEngine.DurationUnits).ToArray();

    // NOTE: static field/property initializers run in textual order, so the tables below MUST be
    // declared before Profile consumes them.

    /// <summary>Arabic label → inference field key. Insertion order preserved (DetailParser derives synonym groups from it).</summary>
    private static readonly Dictionary<string, string> LabelToFieldMap = new()
    {
        ["حالة المشروع"] = "project_status",
        ["تاريخ النشر"] = "published_date",
        ["المدة المتاحة"] = "duration",
        ["الميزانية"] = "budget",
        ["عدد المتقدمين"] = "proposal_count",
    };

    // -----------------------------------------------------------------
    // Field profiles — the same five nafezly meta-panel facts the label rows carry. Core stems
    // run through FieldInferenceEngine.Stem so the scorer sees the identical canonical forms.
    // -----------------------------------------------------------------
    private static readonly FieldProfileTable FieldProfilesTable = new(
    [
        new FieldProfileEntry("project_status", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("حالة"), FieldInferenceEngine.Stem("المشروع")],
            ExpectedTypes = new HashSet<string> { "TEXT" },
            UnitHints = [],
            RequiresUnit = false,
        }),
        new FieldProfileEntry("published_date", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("نشر"), FieldInferenceEngine.Stem("تاريخ")],
            ExpectedTypes = new HashSet<string> { "DATE" },
            ExpectedTypesWeak = new HashSet<string> { "NUMBER" },
            UnitHints = FieldInferenceEngine.MonthNames,
            RequiresUnit = false,
        }),
        new FieldProfileEntry("budget", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("ميزانية"), FieldInferenceEngine.Stem("تكلفة"), FieldInferenceEngine.Stem("سعر")],
            ExpectedTypes = new HashSet<string> { "NUMBER", "FLOAT", "RANGE", "PLACEHOLDER" },
            UnitHints = FieldInferenceEngine.CurrencySyms,
            RequiresUnit = true,
        }),
        new FieldProfileEntry("duration", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("مدة"), FieldInferenceEngine.Stem("متاحة"), FieldInferenceEngine.Stem("وقت")],
            ExpectedTypes = new HashSet<string> { "NUMBER", "FLOAT" },
            UnitHints = FieldInferenceEngine.DurationUnits,
            RequiresUnit = true,
        }),
        new FieldProfileEntry("proposal_count", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("عدد"), FieldInferenceEngine.Stem("المتقدمين"), FieldInferenceEngine.Stem("عروض")],
            ExpectedTypes = new HashSet<string> { "NUMBER" },
            UnitHints = ["عرض", "عروض"],
            RequiresUnit = false,
        }),
    ]);

    /// <summary>The assembled nafezly vocabulary consumed by the shared engines and thin parsers.</summary>
    public static ParseProfile Profile { get; } = new(
        KnownLabels: [
            "حالة المشروع",
            "تاريخ النشر",
            "المدة المتاحة",
            "الميزانية",
            "عدد المتقدمين",
        ],
        FieldProfiles: FieldProfilesTable,
        LabelToField: LabelToFieldMap,
        // Nafezly has no "completed-only" numeric stats in the anonymous HTML (executor success
        // count is app-gated, capture-manifest §1.9) — nothing to force null on an unfinished project.
        CompletedOnlyFields: [],
        NumericFields: new HashSet<string>(["budget", "duration", "proposal_count"]),
        CompletedStatusText: "مكتمل",
        // Placeholder texts that resolve to null. Deliberately excludes a bare "0": a nafezly
        // budget/number is full of zeros ("10 - 25 $"), and the shared IsPlaceholder test is a
        // substring match, so "0" here would null out every real money figure. The real
        // not-calculated markers are words/dashes, never a digit.
        NotCalculatedMarkers: ["غير محدد", "N/A", "لا يوجد", "—"],
        TitleSiteKeywords:
        [
            NafezlySource.Instance.DisplayNameAr,
            NafezlySource.Instance.DisplayNameLatin,
        ],
        TitleSuffixSeparators: [" - ", " | ", " – "],
        // Attachments are absent for anonymous nafezly (capture-manifest §1.11), so the auth-URL
        // shapes here only matter if a session ever exposes them; the app-gate stub is the known
        // gating marker on this site.
        AttachmentAuthUrlSubstrings:
        [
            "/register",
            "/login",
            "js-profile-app-gate",
        ],
        // A modest extension set: nafezly has no anon attachment list, so this rarely drives a hit,
        // but a future session-scoped brief file should still be recognised.
        KnownFileExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "odt", "csv",
            "zip", "rar", "7z", "png", "jpg", "jpeg", "gif", "webp", "svg", "psd", "ai",
            "mp4", "mov", "mp3", "wav",
        },
        // Owner is app-gated with no stable server-rendered owner-card shape, so the shared
        // owner-label scan is neutralised and NafezlyDetailParser reads the owner anchor directly.
        OwnerLabel: "nafezly-owner-label-unused",
        // Listing proposals render as "{n} عروض" (capture-manifest §2 meta column).
        ProposalCountSuffix: " عروض",
        ProposalWords: ["عرض", "عروض"],
        // Publish text is relative-only ("منذ ساعة/يوم/أيام"), matching the shared Arabic matcher.
        RelativeTimeWords: ["منذ", "ساعة", "ساعات", "يوم", "أيام", "أسبوع", "لحظات"]);
}

using MostaqlK.Core.Parsing;

namespace MostaqlK.Sources.Mostaql;

/// <summary>
/// The Mostaql module's parse-text vocabulary as immutable <see cref="ParseProfile"/> data.
/// Every string/table here is byte-copied from the constants the parsers carried before the
/// A1 generalization (StructuralExtractor.KnownLabels / KnownFileExtensions,
/// InferenceEngine.FieldProfiles, DetailParser.LabelToField / CompletedStatusText /
/// markers / SiteKeywords, ListingParser proposal &amp; relative-time words) — the shared
/// <c>Core/Parsing</c> engines consume it, so Mostaql parsing stays byte-identical while a
/// second source can reuse the same engines with its own profile.
/// </summary>
public static class MostaqlParseProfile
{
    /// <summary>Unit hints for "started_since" — mirrors the old StartedSinceUnitHints computation exactly.</summary>
    private static readonly string[] StartedSinceUnitHints =
        FieldInferenceEngine.RelativeDateWords.Concat(FieldInferenceEngine.DurationUnits).ToArray();

    // NOTE: static field/property initializers run in textual order, so the tables above MUST
    // be declared before Profile consumes them.

    /// <summary>Arabic label -> inference field key. Byte-copied from DetailParser.LabelToField; insertion order preserved.</summary>
    private static readonly Dictionary<string, string> LabelToFieldMap = new()
    {
        ["حالة المشروع"] = "project_status",
        ["تاريخ النشر"] = "published_date",
        ["الميزانية"] = "budget",
        ["مدة التنفيذ"] = "duration",
        ["تاريخ التسجيل"] = "registration_date",
        ["معدل التوظيف"] = "hire_rate",
        ["نسبة التوظيف"] = "hire_rate",
        ["المشاريع المفتوحة"] = "open_projects_count",
        ["مشاريع مفتوحة"] = "open_projects_count",
        ["مشاريع قيد التنفيذ"] = "in_progress_count",
        ["المشاريع قيد التنفيذ"] = "in_progress_count",
        ["مشاريع منجزة"] = "completed_projects_count",
        ["مشاريع مكتملة"] = "completed_projects_count",
        ["المشاريع المكتملة"] = "completed_projects_count",
        ["المشاريع المنجزة"] = "completed_projects_count",
        ["التواصلات الجارية"] = "ongoing_conversations",
        ["بدأ تنفيذه منذ"] = "started_since",
        ["تاريخ الصفقة"] = "deal_date",
        ["موعد التسليم"] = "delivery_date",
        ["عدد العروض"] = "proposal_count",
        ["عدد المقترحات"] = "proposal_count",
    };

    // -----------------------------------------------------------------
    // Field profiles - byte-copied from InferenceEngine.FieldProfiles (same field keys,
    // same core_stems/expected_types/unit_hints/requires_unit per field, same order).
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
            CoreStems = [FieldInferenceEngine.Stem("مدة"), FieldInferenceEngine.Stem("تنفيذ"), FieldInferenceEngine.Stem("وقت"), FieldInferenceEngine.Stem("لازم")],
            ExpectedTypes = new HashSet<string> { "NUMBER", "FLOAT" },
            UnitHints = FieldInferenceEngine.DurationUnits,
            RequiresUnit = true,
        }),
        new FieldProfileEntry("registration_date", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("تسجيل"), FieldInferenceEngine.Stem("تاريخ")],
            ExpectedTypes = new HashSet<string> { "DATE" },
            ExpectedTypesWeak = new HashSet<string> { "NUMBER" },
            UnitHints = FieldInferenceEngine.MonthNames,
            RequiresUnit = false,
        }),
        new FieldProfileEntry("hire_rate", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("معدل"), FieldInferenceEngine.Stem("توظيف")],
            ExpectedTypes = new HashSet<string> { "PERCENT", "NUMBER" },
            UnitHints = ["%"],
            RequiresUnit = true,
        }),
        new FieldProfileEntry("open_projects_count", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("مشاريع"), FieldInferenceEngine.Stem("مفتوحة")],
            ExpectedTypes = new HashSet<string> { "NUMBER" },
            UnitHints = [],
            RequiresUnit = false,
        }),
        new FieldProfileEntry("in_progress_count", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("مشاريع"), FieldInferenceEngine.Stem("تنفيذ")],
            ExpectedTypes = new HashSet<string> { "NUMBER" },
            UnitHints = [],
            RequiresUnit = false,
        }),
        new FieldProfileEntry("completed_projects_count", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("مشاريع"), FieldInferenceEngine.Stem("منجزة")],
            ExpectedTypes = new HashSet<string> { "NUMBER" },
            UnitHints = [],
            RequiresUnit = false,
        }),
        new FieldProfileEntry("ongoing_conversations", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("تواصلات"), FieldInferenceEngine.Stem("جارية")],
            ExpectedTypes = new HashSet<string> { "NUMBER" },
            UnitHints = [],
            RequiresUnit = false,
        }),
        new FieldProfileEntry("started_since", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("بدأ"), FieldInferenceEngine.Stem("تنفيذه"), FieldInferenceEngine.Stem("منذ")],
            ExpectedTypes = new HashSet<string> { "DATE", "NUMBER" },
            UnitHints = StartedSinceUnitHints,
            RequiresUnit = false,
        }),
        new FieldProfileEntry("deal_date", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("تاريخ"), FieldInferenceEngine.Stem("الصفقة")],
            ExpectedTypes = new HashSet<string> { "DATE" },
            ExpectedTypesWeak = new HashSet<string> { "NUMBER" },
            UnitHints = FieldInferenceEngine.MonthNames,
            RequiresUnit = false,
        }),
        new FieldProfileEntry("delivery_date", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("موعد"), FieldInferenceEngine.Stem("التسليم")],
            ExpectedTypes = new HashSet<string> { "DATE" },
            ExpectedTypesWeak = new HashSet<string> { "NUMBER" },
            UnitHints = FieldInferenceEngine.MonthNames,
            RequiresUnit = false,
        }),
        new FieldProfileEntry("proposal_count", new FieldProfileData
        {
            CoreStems = [FieldInferenceEngine.Stem("عدد"), FieldInferenceEngine.Stem("المقترحات"), FieldInferenceEngine.Stem("عروض"), FieldInferenceEngine.Stem("مقترحات")],
            ExpectedTypes = new HashSet<string> { "NUMBER" },
            UnitHints = ["عرض", "عروض", "عرضان", "عرضين"],
            RequiresUnit = false,
        }),
    ]);

    /// <summary>
    /// Simplified, hand-picked list of common file extensions used to recognize attachment
    /// links, in lieu of Python's full `mimetypes` DB (which .NET has no equivalent single
    /// source for). Covers the extensions realistically seen on the site's project pages.
    /// Byte-copied from the old StructuralExtractor.KnownFileExtensions.
    /// </summary>
    private static readonly HashSet<string> KnownFileExtensionsSet = new(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>The assembled Mostaql vocabulary consumed by the shared engines and thin parsers.</summary>
    public static ParseProfile Profile { get; } = new(
        KnownLabels: [
            "حالة المشروع",
            "تاريخ النشر",
            "الميزانية",
            "مدة التنفيذ",
            "المهارات",
            "تاريخ التسجيل",
            "معدل التوظيف",
            "المشاريع المفتوحة",
            "مشاريع قيد التنفيذ",
            "مشاريع منجزة",
            "التواصلات الجارية",
            "بدأ تنفيذه منذ",
            "تاريخ الصفقة",
            "موعد التسليم",
            "صاحب المشروع",
            "عدد العروض",
            "عدد المقترحات",
        ],
        FieldProfiles: FieldProfilesTable,
        LabelToField: LabelToFieldMap,
        CompletedOnlyFields: ["started_since", "deal_date", "delivery_date", "completed_projects_count"],
        NumericFields: new HashSet<string>(
        [
            "hire_rate", "budget", "duration", "open_projects_count", "in_progress_count",
            "completed_projects_count", "ongoing_conversations", "proposal_count",
        ]),
        CompletedStatusText: "مكتمل",
        NotCalculatedMarkers: ["لم يحسب بعد", "غير محدد", "N/A", "لا يوجد"],
        TitleSiteKeywords:
        [
            MostaqlSource.Instance.DisplayNameAr,
            MostaqlSource.Instance.DisplayNameLatin,
            "Mostaqlk", // the app's OWN display name also shows up in page titles — product identity, not a site fact
        ],
        TitleSuffixSeparators: [" - ", " | ", " – "],
        // Was the inline RequiresAuth sniff in StructuralExtractor.AttachmentFromLink; the
        // host-rooted attachments path is built from the descriptor so it follows the domain.
        AttachmentAuthUrlSubstrings:
        [
            "/register",
            "/login",
            "/download/",
            $"{MostaqlSource.Instance.Domain}/attachments/",
        ],
        KnownFileExtensions: KnownFileExtensionsSet,
        OwnerLabel: "صاحب المشروع",
        ProposalCountSuffix: " عروض",
        ProposalWords: ["عرض", "عروض"],
        RelativeTimeWords: ["منذ", "ساعة", "يوم", "لحظات"]);
}

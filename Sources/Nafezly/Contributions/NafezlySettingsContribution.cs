using MostaqlK.Core.Sources;

namespace MostaqlK.Sources.Nafezly.Contributions;

/// <summary>
/// Module-local settings keys for nafezly's contributed section. Deliberately NOT added to the shared
/// <c>SettingsKeys</c> table — a source's own preference names belong to the module (contract §10 hard
/// rule: source constants live in the module, never in <c>Core/Sources</c> defaults).
/// </summary>
public static class NafezlySettingsKeys
{
    /// <summary>Whether the pipeline should keep retrying enrichment that failed on a 403/429.</summary>
    public const string EnrichmentRetryEnabled = "nafezly_enrichment_retry_enabled";
}

/// <summary>
/// The descriptor form of the same contribution (settings plugin step 5): nafezly's module-local
/// rows are now ordinary <see cref="SettingsDescriptor"/> data folded in at the
/// <c>{source: nafezly}</c> layer, so they render through the shared <c>SettingsRow</c> unit like
/// every other row and the module never needs a view-model of its own.
/// <para>
/// The section caption is itself a row (<see cref="SettingsShape.SectionHeader"/>) rather than a
/// string the settings page would have to know about — a source that invents a section supplies
/// its header, and the shared code still names no source.
/// </para>
/// <para>
/// The session read-out is TWO mutually exclusive stat rows gated by capability flags, because the
/// wording depends on the source's own capability: contributing both with opposite
/// <see cref="SettingsDescriptor.VisibilityFlag"/>s keeps the choice in data.
/// </para>
/// </summary>
public sealed class NafezlySettingsContribution : ISourceSettingsContribution
{
    /// <summary>The module's own section key; its header row supplies the Arabic caption.</summary>
    public const string SectionKey = "nafezly";

    public SourceId Source => NafezlySource.Instance.Id;

    public IReadOnlyList<SettingsDescriptor> Adds =>
    [
        new("nafezly-section-header", SettingsShape.SectionHeader, SettingsScope.PerSource,
            "إعدادات نفذلي", SectionKey, 0),
        new(NafezlySettingsKeys.EnrichmentRetryEnabled, SettingsShape.Toggle, SettingsScope.PerSource,
            "إعادة محاولة الإثراء التلقائي", SectionKey, 10)
        {
            HintAr = "يعيد محاولة إثراء المشاريع التي فشلت بسبب حظر مؤقت (403/429)",
            Value = SettingsValueKind.Bool,
            DefaultLiteral = "true",
            AutomationId = "Settings_NafezlyRetryToggle",
            AutomationIdMobile = "Settings_Mobile_NafezlyRetryToggle",
        },
        new("nafezly-session-requires-login", SettingsShape.ReadOnlyStat, SettingsScope.PerSource,
            "جلسة الإثراء", SectionKey, 20)
        {
            HintAr = "يتطلب تسجيل الدخول لبيانات كاملة",
            VisibilityFlag = "NeedsSessionForFullEnrichment",
        },
        new("nafezly-session-anonymous", SettingsShape.ReadOnlyStat, SettingsScope.PerSource,
            "جلسة الإثراء", SectionKey, 20)
        {
            HintAr = "يعمل بدون تسجيل دخول (بيانات أساسية مجهولة المصدر)",
            VisibilityFlag = "WorksWithoutSession",
        },
    ];

    public IReadOnlyList<SettingsDescriptorPatch> Overrides => [];
}

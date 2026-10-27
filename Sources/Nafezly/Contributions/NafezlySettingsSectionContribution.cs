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
/// View-model for the nafezly settings section (session-state read-out + the enrichment-retry toggle).
/// A plain POCO — the MAUI binding wrapper and the actual Settings-page rendering are B4 work
/// (contract §4/§10 keep the shared UI blind to this). It depends only on
/// <see cref="ISettingsRepository"/> and the descriptor, so it is DI-constructible and unit-linkable
/// without the MAUI stack.
/// </summary>
public sealed class NafezlySettingsSectionViewModel : ISourceSettingsSectionViewModel
{
    private readonly ISettingsRepository? _settings;
    private readonly SourceId _source;

    public NafezlySettingsSectionViewModel(ISettingsRepository? settings, SourceId source)
    {
        _settings = settings;
        _source = source;
    }

    public string HeaderAr => "إعدادات نفذلي";

    // The section carries exactly two rows, both always present for nafezly: the enrichment-retry
    // toggle and the session-state read-out. The <c>Has*</c> flags drive the shared host's per-row
    // visibility (contract §4/§10 — an absent row is never rendered, not rendered empty).
    public bool HasRetryToggle => true;

    public string RetryToggleLabelAr => "إعادة محاولة الإثراء التلقائي";

    /// <summary>
    /// Nafezly enrichment does NOT require a session for its core facts (JSON-LD + meta rows are
    /// anonymous, contract §1 capabilities); the flag is surfaced straight from the descriptor so the
    /// UI reflects the source truth instead of hardcoding it.
    /// </summary>
    public bool RequiresSession => NafezlySource.Instance.Capabilities.NeedsSessionForFullEnrichment;

    public bool HasSessionRow => true;

    public string SessionRowLabelAr => "جلسة الإثراء";

    public string SessionStateText => RequiresSession
        ? "يتطلب تسجيل الدخول لبيانات كاملة"
        : "يعمل بدون تسجيل دخول (بيانات أساسية مجهولة المصدر)";

    /// <summary>Enrichment-retry toggle, persisted per-source. Defaults to on.</summary>
    public bool EnrichmentRetryEnabled
    {
        get => _settings?.Get(_source, NafezlySettingsKeys.EnrichmentRetryEnabled, true) ?? true;
        set => _settings?.Set(_source, NafezlySettingsKeys.EnrichmentRetryEnabled, value);
    }
}

/// <summary>
/// The ONE real consumer of the <see cref="ISourceSettingsSectionContribution"/> seam for now
/// (contract §10): nafezly contributes a settings section that the Settings page would append after the
/// descriptor's data rows ONLY while nafezly is the active source. That presence-by-contribution (not
/// an <c>if (source == nafezly)</c> branch) is exactly the "growth without branches" proof the registry
/// gives. Registered in <see cref="NafezlyModule"/>; the actual Settings wiring is B4.
/// </summary>
public sealed class NafezlySettingsSectionContribution : ISourceSettingsSectionContribution
{
    public SourceId Source => NafezlySource.Instance.Id;

    public string HeaderAr => "إعدادات نفذلي";

    public object CreateViewModel(IServiceProvider services)
    {
        var settings = services.GetService(typeof(ISettingsRepository)) as ISettingsRepository;
        return new NafezlySettingsSectionViewModel(settings, Source);
    }
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

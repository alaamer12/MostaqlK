namespace MostaqlK.Core.Sources;

/// <summary>
/// The closed set of row shapes the settings renderer knows how to build. Adding a shape means
/// adding one enum member plus one <c>SettingsRow</c> branch — never a new XAML row in a layout.
/// The catalog of every shape in every state lives in
/// <c>.repertoire/design/postmvp/v3-multisource/settings-shapes.html</c>.
/// </summary>
public enum SettingsShape
{
    Toggle,
    NumericBounded,
    TextEntry,
    EnumPicker,
    CookieEditor,
    ReadOnlyStat,
    ActionButton,
    LinkOut,
    InfoBanner,
    SectionHeader,
    Slider,
    MultiChoiceChips,
    SegmentedButtons,
    FilePicker,
}

/// <summary>Where the value persists: the flat global container or the source's namespaced one.</summary>
public enum SettingsScope { Global, PerSource }

/// <summary>The CLR type the row reads/writes through <c>ISettingsRepository</c>.</summary>
public enum SettingsValueKind { Bool, Int, String, Enum }

/// <summary>Which centralized validator runs before a write is queued.</summary>
public enum SettingsValidationKind { None, IntRange, IntMin, EnumIndex, CookieParse }

/// <summary>
/// A default supplied by the source descriptor itself instead of a literal, so cadence/rate
/// defaults live in exactly one place (the module) and the VM stops duplicating constants.
/// </summary>
public enum DefaultFact { None, PollIntervalSeconds, RequestsPerMinute }

/// <summary>
/// How the row's secondary line is painted. The mockup gives the same slot three meanings
/// (<c>.set-sub</c> neutral, <c>.clamp-note</c> brand, <c>.cookie-ok</c> positive), and which one
/// applies is data the descriptor knows, not something the renderer may infer from the text.
/// </summary>
public enum SettingsHintTone { Muted, Brand, Success }

/// <summary>
/// One settings row, fully self-describing: shape, storage, constraints, Arabic copy,
/// validation message, defaults, AutomationIds for both platforms, and visibility.
/// Descriptors are data — sources add or override them (see <see cref="SettingsDescriptorPatch"/>)
/// and the settings page renders whatever the composed list contains with zero shared-UI edits.
/// </summary>
public sealed record SettingsDescriptor(
    string Key,
    SettingsShape Shape,
    SettingsScope Scope,
    string LabelAr,
    string Section,
    int Order)
{
    public string? HintAr { get; init; }
    public string? PlaceholderAr { get; init; }

    /// <summary>Paint of <see cref="HintAr"/> — neutral note, auto-clamp notice, or saved-session success.</summary>
    public SettingsHintTone HintTone { get; init; } = SettingsHintTone.Muted;

    /// <summary>
    /// Text of the row's primary control when it is not the row label: the ActionButton's button,
    /// the LinkOut's link, the FilePicker's browse button, the CookieEditor's upload button.
    /// </summary>
    public string? ActionLabelAr { get; init; }

    /// <summary>Text of the secondary, link-styled control next to the primary one (إلغاء الربط، تغيير…).</summary>
    public string? SecondaryLabelAr { get; init; }

    /// <summary>Unit rendered after a numeric/text value (ثانية، طلب/دقيقة…).</summary>
    public string? UnitAr { get; init; }

    public int? Min { get; init; }
    public int? Max { get; init; }
    public int Step { get; init; } = 1;

    /// <summary>Choices for EnumPicker/SegmentedButtons/MultiChoiceChips (Value + Arabic label).</summary>
    public IReadOnlyList<FilterFacetOption>? Options { get; init; }

    public SettingsValueKind Value { get; init; } = SettingsValueKind.String;
    public SettingsValidationKind Validation { get; init; } = SettingsValidationKind.None;

    /// <summary>Error template; {0}=min, {1}=max, {2}=offending value.</summary>
    public string? ErrorAr { get; init; }

    public string? DefaultLiteral { get; init; }
    public DefaultFact DefaultFrom { get; init; } = DefaultFact.None;

    /// <summary>Windows AutomationId (the UI tests' selectors).</summary>
    public string AutomationId { get; init; } = "";

    /// <summary>Mobile AutomationId; the two platforms' historical names do not follow one rule.</summary>
    public string? AutomationIdMobile { get; init; }

    public bool Visible { get; init; } = true;

    /// <summary>
    /// Named condition evaluated against the live <see cref="ISourceDescriptor"/>
    /// (e.g. NeedsSessionForFullEnrichment, HasSupervisedFlag, HasCurrencySuffix).
    /// Empty means always visible.
    /// </summary>
    public string VisibilityFlag { get; init; } = "";

    /// <summary>Render the editable value LTR-isolated (URLs, cookie names, numbers).</summary>
    public bool IsLtr { get; init; }

    public bool IsPassword { get; init; }

    /// <summary>Action/stat rows bind a named command instead of a stored value.</summary>
    public string? CommandName { get; init; }
}

/// <summary>
/// A partial override of an existing descriptor: only the set fields merge onto the base row,
/// everything else (shape, bounds, ids, validation) stays global. This is how Nafezly changes
/// just the cookie row's Arabic without touching the row's behavior. Contributing a complete
/// <see cref="SettingsDescriptor"/> for the same key instead is a full replace.
/// </summary>
public sealed record SettingsDescriptorPatch(string Key)
{
    public SettingsShape? Shape { get; init; }
    public SettingsScope? Scope { get; init; }
    public string? LabelAr { get; init; }
    public string? HintAr { get; init; }
    public string? PlaceholderAr { get; init; }
    public SettingsHintTone? HintTone { get; init; }
    public string? ActionLabelAr { get; init; }
    public string? SecondaryLabelAr { get; init; }
    public string? UnitAr { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }
    public int? Step { get; init; }
    public IReadOnlyList<FilterFacetOption>? Options { get; init; }
    public SettingsValueKind? Value { get; init; }
    public SettingsValidationKind? Validation { get; init; }
    public string? ErrorAr { get; init; }
    public string? DefaultLiteral { get; init; }
    public DefaultFact? DefaultFrom { get; init; }
    public string? AutomationId { get; init; }
    public string? AutomationIdMobile { get; init; }
    public bool? Visible { get; init; }
    public string? VisibilityFlag { get; init; }
    public bool? IsLtr { get; init; }
    public bool? IsPassword { get; init; }
    public string? CommandName { get; init; }
    public string? Section { get; init; }
    public int? Order { get; init; }

    /// <summary>Field names this patch sets — the catalog uses them to detect two same-specificity
    /// layers touching one field (a collision that must fail loudly, not resolve by order).</summary>
    public IEnumerable<string> SetFields()
    {
        if (Shape is not null) yield return nameof(Shape);
        if (Scope is not null) yield return nameof(Scope);
        if (LabelAr is not null) yield return nameof(LabelAr);
        if (HintAr is not null) yield return nameof(HintAr);
        if (PlaceholderAr is not null) yield return nameof(PlaceholderAr);
        if (HintTone is not null) yield return nameof(HintTone);
        if (ActionLabelAr is not null) yield return nameof(ActionLabelAr);
        if (SecondaryLabelAr is not null) yield return nameof(SecondaryLabelAr);
        if (UnitAr is not null) yield return nameof(UnitAr);
        if (Min is not null) yield return nameof(Min);
        if (Max is not null) yield return nameof(Max);
        if (Step is not null) yield return nameof(Step);
        if (Options is not null) yield return nameof(Options);
        if (Value is not null) yield return nameof(Value);
        if (Validation is not null) yield return nameof(Validation);
        if (ErrorAr is not null) yield return nameof(ErrorAr);
        if (DefaultLiteral is not null) yield return nameof(DefaultLiteral);
        if (DefaultFrom is not null) yield return nameof(DefaultFrom);
        if (AutomationId is not null) yield return nameof(AutomationId);
        if (AutomationIdMobile is not null) yield return nameof(AutomationIdMobile);
        if (Visible is not null) yield return nameof(Visible);
        if (VisibilityFlag is not null) yield return nameof(VisibilityFlag);
        if (IsLtr is not null) yield return nameof(IsLtr);
        if (IsPassword is not null) yield return nameof(IsPassword);
        if (CommandName is not null) yield return nameof(CommandName);
        if (Section is not null) yield return nameof(Section);
        if (Order is not null) yield return nameof(Order);
    }

    public SettingsDescriptor ApplyTo(SettingsDescriptor baseDescriptor) => baseDescriptor with
    {
        Shape = Shape ?? baseDescriptor.Shape,
        Scope = Scope ?? baseDescriptor.Scope,
        LabelAr = LabelAr ?? baseDescriptor.LabelAr,
        HintAr = HintAr ?? baseDescriptor.HintAr,
        PlaceholderAr = PlaceholderAr ?? baseDescriptor.PlaceholderAr,
        HintTone = HintTone ?? baseDescriptor.HintTone,
        ActionLabelAr = ActionLabelAr ?? baseDescriptor.ActionLabelAr,
        SecondaryLabelAr = SecondaryLabelAr ?? baseDescriptor.SecondaryLabelAr,
        UnitAr = UnitAr ?? baseDescriptor.UnitAr,
        Min = Min ?? baseDescriptor.Min,
        Max = Max ?? baseDescriptor.Max,
        Step = Step ?? baseDescriptor.Step,
        Options = Options ?? baseDescriptor.Options,
        Value = Value ?? baseDescriptor.Value,
        Validation = Validation ?? baseDescriptor.Validation,
        ErrorAr = ErrorAr ?? baseDescriptor.ErrorAr,
        DefaultLiteral = DefaultLiteral ?? baseDescriptor.DefaultLiteral,
        DefaultFrom = DefaultFrom ?? baseDescriptor.DefaultFrom,
        AutomationId = AutomationId ?? baseDescriptor.AutomationId,
        AutomationIdMobile = AutomationIdMobile ?? baseDescriptor.AutomationIdMobile,
        Visible = Visible ?? baseDescriptor.Visible,
        VisibilityFlag = VisibilityFlag ?? baseDescriptor.VisibilityFlag,
        IsLtr = IsLtr ?? baseDescriptor.IsLtr,
        IsPassword = IsPassword ?? baseDescriptor.IsPassword,
        CommandName = CommandName ?? baseDescriptor.CommandName,
        Section = Section ?? baseDescriptor.Section,
        Order = Order ?? baseDescriptor.Order,
    };
}

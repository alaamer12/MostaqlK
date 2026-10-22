namespace MostaqlK.Sources.Nafezly;

/// <summary>
/// Nafezly's project-state vocabulary as module data. The scraped state TEXT is stored raw on
/// <see cref="MostaqlK.Models.ProjectDetails.ProjectStatus"/> (the UI shows it verbatim — a state
/// never gets retinted by brand, per the README invariant); this table only records the four
/// canonical strings so shared code has no reason to hardcode an Arabic word.
/// Three are directly observed in the A2 fixtures (capture-manifest §1.4/§5); <c>مغلق</c> (closed)
/// was NEVER seen anywhere and is mapped defensively per the A3 brief, not from observation.
/// </summary>
public static class NafezlyStates
{
    public const string Open = "مفتوح";
    public const string InProgress = "تحت التنفيذ";
    public const string Completed = "مكتمل";

    /// <summary>Unobserved in the anonymous fixtures; kept so a closed project maps instead of slipping through.</summary>
    public const string Closed = "مغلق";

    /// <summary>Every state string the module recognizes, in the observed + defensive order.</summary>
    public static IReadOnlyList<string> All { get; } = [Open, InProgress, Completed, Closed];
}

namespace MostaqlK.Services;

/// <summary>
/// Parsing helpers for the v3 visual-parity launch flags consumed by <c>App</c>,
/// <c>MainWindowPage</c>, and <c>tools/v3_parity.py</c>. Every member here is only ever
/// consulted when the corresponding flag is present on the command line: a launch without
/// parity flags behaves exactly as before (the harness, not the product, drives these states).
/// <list type="table">
/// <item><term>--skip-onboarding</term><description>latches the onboarding completion preference before the first window is created.</description></item>
/// <item><term>--parity-state=default|panel-collapsed|filters-open</term><description>deterministically forces the projects-page interact-to-change states the parity matrix captures.</description></item>
/// </list>
/// </summary>
public static class ParityLaunchFlags
{
    public const string SkipOnboardingArgument = "--skip-onboarding";
    public const string ParityStateArgument = "--parity-state=";
    public const string ParityShapeArgument = "--parity-shape=";

    public const string StateDefault = "default";
    public const string StatePanelCollapsed = "panel-collapsed";
    public const string StateFiltersOpen = "filters-open";

    /// <summary>The <c>--parity-shape=&lt;value&gt;</c> argument (settings-gallery only), or null when absent.</summary>
    public static string? ParityShape(string[] args) => ValueAfter(args, ParityShapeArgument);

    /// <summary>
    /// <see cref="ParityState"/> normalized for the gallery: <c>default</c> means "no filter", so a
    /// full-gallery capture shows every variant cell (the mockup's <c>?state=</c> script treats it the
    /// same way). Every other value isolates one cell.
    /// </summary>
    public static string? ParityStateFilter(string[] args) =>
        ParityState(args) is { Length: > 0 } state && !state.Equals(StateDefault, StringComparison.OrdinalIgnoreCase)
            ? state
            : null;

    /// <summary>
    /// Preference the shipped <c>PipelineDashboardPanel</c> reads at construction for its
    /// collapsed/expanded state. The flag writes it before the panel exists (the same way a
    /// user's last collapse persists), so no rail/panel code needs a parity branch. The literal
    /// mirrors the panel's private <c>KeyIsExpanded</c> constant by design — the panel unit is
    /// owned by the shell wave and must not gain harness knowledge.
    /// </summary>
    public const string PipelinePanelExpandedPreferenceKey = "pipeline_panel_is_expanded";

    public static bool HasSkipOnboarding(string[] args) =>
        args.Any(arg => arg.Equals(SkipOnboardingArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>The <c>--parity-state=&lt;value&gt;</c> argument, or null when absent.</summary>
    public static string? ParityState(string[] args) => ValueAfter(args, ParityStateArgument);

    private static string? ValueAfter(string[] args, string prefix)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..].ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// The ONE launch state that is allowed to force the filters modal open: an explicit
    /// <c>--parity-state=filters-open</c>. Every other value — including <c>default</c>,
    /// <c>panel-collapsed</c>, and the harness's always-present single-instance argument — returns
    /// <c>false</c>. This is the explicit allowlist that closes the state-leak: the modal-open path
    /// must never key off "any non-null parity state" (a truthy check), only this exact value, so a
    /// default launch can never present the dialog.
    /// </summary>
    public static bool ShouldOpenFiltersModal(string[] args) =>
        string.Equals(ParityState(args), StateFiltersOpen, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Forces the projects-page ItC state at startup: <c>panel-collapsed</c> pre-writes the
    /// pipeline panel's persisted expanded preference (read when the panel is constructed);
    /// <c>default</c>/<c>filters-open</c> pre-write it as expanded so a previous parity launch's
    /// collapse can never leak into a capture (the filters modal itself is opened by
    /// <c>MainWindowPage</c>, which reads <see cref="ParityState"/> directly).
    /// Absent flag ⇒ the preference is left untouched (normal user persistence).
    /// </summary>
    public static void ApplyPipelinePanelState(string[] args)
    {
        var state = ParityState(args);
        if (state is null)
        {
            return;
        }

        Microsoft.Maui.Storage.Preferences.Set(
            PipelinePanelExpandedPreferenceKey,
            !state.Equals(StatePanelCollapsed, StringComparison.OrdinalIgnoreCase));
    }
}

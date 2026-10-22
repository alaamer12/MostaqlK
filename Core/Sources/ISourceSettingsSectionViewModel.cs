namespace MostaqlK.Core.Sources;

/// <summary>
/// The bindable shape a source's contributed settings section presents to the shared Settings host.
/// A module's section view-model implements this (see <see cref="ISourceSettingsSectionContribution.CreateViewModel"/>);
/// the host binds a single generic template against it and never names a source (backend-ui-contract §4/§10).
/// <para>
/// Every optional row is gated by an explicit <c>Has*</c> flag rather than by an empty value: an absent
/// row is <em>never rendered</em> (not rendered empty), the same presence-by-data rule the details page
/// uses for schema fields. A contribution that only has a toggle sets <see cref="HasSessionRow"/>=false,
/// and vice-versa.
/// </para>
/// </summary>
public interface ISourceSettingsSectionViewModel
{
    /// <summary>Section header in Arabic (mirrors <see cref="ISourceSettingsSectionContribution.HeaderAr"/>).</summary>
    string HeaderAr { get; }

    /// <summary>Whether this section carries the enrichment-retry toggle row.</summary>
    bool HasRetryToggle { get; }

    /// <summary>Label beside the retry toggle when present.</summary>
    string RetryToggleLabelAr { get; }

    /// <summary>Retry toggle state; the setter persists it (per-source, through <c>ISettingsRepository</c>).</summary>
    bool EnrichmentRetryEnabled { get; set; }

    /// <summary>Whether this section carries the session-state read-out row.</summary>
    bool HasSessionRow { get; }

    /// <summary>Label of the session-state row when present.</summary>
    string SessionRowLabelAr { get; }

    /// <summary>Descriptor-derived session-state sentence shown on the session row.</summary>
    string SessionStateText { get; }
}

using MostaqlK.Core.Sources;

namespace MostaqlK.Models;

/// <summary>
/// Fully enriched project, fetched from the project's own detail page after discovery.
/// Backs the project details page described in <c>project-details.html</c>.
/// </summary>
public sealed class ProjectDetails
{
    /// <summary>Which monitored site this project came from.</summary>
    public SourceId Source { get; set; } = SourceId.Default;

    /// <summary>The site-local numeric id, unique only within <see cref="Source"/>.</summary>
    public long ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? Budget { get; set; }

    public int? DeliveryDays { get; set; }

    public string? ProjectStatus { get; set; }

    /// <summary>
    /// The freelancer a project was awarded to (منفذ المشروع), scraped from the detail page.
    /// Populated only by sources whose pages expose it anonymously
    /// (<see cref="SourceCapabilities.HasExecutorInfo"/> — nafezly on awarded projects); null
    /// everywhere else (mostaql never carries it, nafezly open projects have none yet). The UI
    /// renders the executor block iff the capability holds AND a value is present.
    /// </summary>
    public string? ExecutorName { get; set; }

    public int PublishTimeNumber { get; set; }

    public string PublishTimeText { get; set; } = string.Empty;

    public int ProposalCount { get; set; }

    public string ProposalCountText { get; set; } = string.Empty;

    public List<ProjectSkill> Skills { get; set; } = [];

    public Owner Owner { get; set; } = new();

    public List<Asset> Attachments { get; set; } = [];

    public EnrichmentStatus EnrichmentStatus { get; set; } = EnrichmentStatus.Pending;

    public DateTimeOffset DiscoveredAt { get; set; }

    public DateTimeOffset? EnrichedAt { get; set; }

    /// <summary>
    /// Resolved value/source/confidence for every meta-row field considered by
    /// <c>Infrastructure.Http.Parsers.DetailParser</c>'s structural/inference combinator
    /// (mirrors pipeline.py's <c>parse_project()</c> "fields" dict), keyed by field name
    /// (e.g. "project_status", "published_date", "hire_rate"). Populated for drift-detection
    /// and debugging purposes even for fields with no dedicated property above.
    /// </summary>
    public Dictionary<string, FieldResolution> FieldProvenance { get; set; } = new();

    /// <summary>
    /// Fields where the structural and inference-engine values disagreed (mirrors
    /// pipeline.py's <c>mismatches</c> list), kept for drift detection.
    /// </summary>
    public List<FieldMismatch> Mismatches { get; set; } = [];
}

/// <summary>Resolved value plus provenance for a single meta-row field. See <see cref="ProjectDetails.FieldProvenance"/>.</summary>
public sealed record FieldResolution(string? Value, string Source, double Confidence);

/// <summary>A structural/inference disagreement recorded for a single field. See <see cref="ProjectDetails.Mismatches"/>.</summary>
public sealed record FieldMismatch(string Field, string? StructuralValue, string? InferenceValue);

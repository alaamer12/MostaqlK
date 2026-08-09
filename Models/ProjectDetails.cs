namespace MostaqlK.Models;

/// <summary>
/// Fully enriched project, fetched from the project's own detail page after discovery.
/// Backs the project details page described in <c>project-details.html</c>.
/// </summary>
public sealed class ProjectDetails
{
    public long ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? Budget { get; set; }

    public int? DeliveryDays { get; set; }

    public List<ProjectSkill> Skills { get; set; } = [];

    public Owner Owner { get; set; } = new();

    public List<Asset> Attachments { get; set; } = [];

    public EnrichmentStatus EnrichmentStatus { get; set; } = EnrichmentStatus.Pending;

    public DateTimeOffset? EnrichedAt { get; set; }
}

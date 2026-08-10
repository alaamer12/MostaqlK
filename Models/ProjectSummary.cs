namespace MostaqlK.Models;

/// <summary>
/// Lightweight representation of a project as it appears in the listing feed, before
/// enrichment. Backs the project card in <c>Features/Projects/Views/ProjectCard.xaml</c>.
/// </summary>
public sealed class ProjectSummary
{
    public long ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public string PostedRelative { get; set; } = string.Empty;

    public int ProposalCount { get; set; }

    public string? Budget { get; set; }

    public int? DeliveryDays { get; set; }

    public string SkillsText { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsUnread { get; set; } = true;

    public EnrichmentStatus EnrichmentStatus { get; set; } = EnrichmentStatus.Pending;

    public DateTimeOffset DiscoveredAt { get; set; }
}

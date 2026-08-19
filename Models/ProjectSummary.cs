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

    public int PublishTimeNumber { get; set; }

    public string PublishTimeText { get; set; } = string.Empty;

    public int ProposalCount { get; set; }

    public string ProposalCountText { get; set; } = string.Empty;

    public string? Budget { get; set; }

    public int? DeliveryDays { get; set; }

    public string SkillsText { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? ProjectStatus { get; set; }

    public bool IsUnread { get; set; } = true;

    public EnrichmentStatus EnrichmentStatus { get; set; } = EnrichmentStatus.Pending;

    public DateTimeOffset DiscoveredAt { get; set; }

    /// <summary>
    /// Timestamp at which this project's enrichment fully completed (set by
    /// <see cref="MostaqlK.Infrastructure.Http.Parsers.DetailParser"/> the moment detail parsing
    /// succeeds). Null while the project is still pending/being enriched. This — not
    /// <see cref="DiscoveredAt"/> — is what the feed sorts by, so a project only jumps to the
    /// top of the list once its enrichment has genuinely finished.
    /// </summary>
    public DateTimeOffset? EnrichedAt { get; set; }
}

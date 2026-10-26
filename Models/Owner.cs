using MostaqlK.Core.Sources;

namespace MostaqlK.Models;

/// <summary>
/// The client/employer who posted a project, on one of the monitored sites.
/// </summary>
public sealed class Owner
{
    /// <summary>Which monitored site this owner profile belongs to.</summary>
    public SourceId Source { get; set; } = SourceId.Default;

    /// <summary>The site-local owner id (parsed or hashed per source), unique only within <see cref="Source"/>.</summary>
    public long OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ProfileUrl { get; set; }

    public string? AvatarUrl { get; set; }

    public double? Rating { get; set; }

    public int? CompletedProjectsCount { get; set; }
    public double? HiringRatePercent { get; set; }

    public string? RegisteredAt { get; set; }
    public int? OpenProjectsCount { get; set; }
    public int? InProgressProjectsCount { get; set; }
    public int? OngoingCommunicationsCount { get; set; }
}

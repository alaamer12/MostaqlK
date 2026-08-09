namespace MostaqlK.Models;

/// <summary>
/// The Mostaql client/employer who posted a project.
/// </summary>
public sealed class Owner
{
    public long OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? ProfileUrl { get; set; }

    public string? AvatarUrl { get; set; }

    public double? Rating { get; set; }

    public int? CompletedProjectsCount { get; set; }

    public int? HiringRatePercent { get; set; }
}

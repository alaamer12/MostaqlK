using MostaqlK.Core.Sources;

namespace MostaqlK.Models;

/// <summary>
/// A competitor/rival freelancer's file on one monitored site (v4 R3, the <c>performers</c> table).
/// Distinct from <see cref="Owner"/> (the client) — this is who bids AGAINST the user.
/// <para>
/// Identity is <see cref="PerformerKey"/>: the numeric avatar-prefix id on Nafezly (R3.2/D26) and the
/// username on anonymous Mostaql (D37, because Mostaql's numeric id is session-only). It is unique
/// only within <see cref="Source"/>; the composite <c>(Source, PerformerKey)</c> is the storage key,
/// so a Mostaql and a Nafezly performer never collide (R3, §1 isolation I2).
/// </para>
/// </summary>
public sealed class Performer
{
    public required SourceId Source { get; init; }

    /// <summary>Site-local stable key (numeric id for Nafezly, username for Mostaql). Never a global id.</summary>
    public required string PerformerKey { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? ProfileUrl { get; init; }
    public string? AvatarUrl { get; init; }

    public double? Rating { get; init; }
    public int? CompletedProjectsCount { get; init; }

    /// <summary>Completion rate (Mostaql catalog exposes this; a distinct signal from hire rate).</summary>
    public double? CompletionRate { get; init; }

    /// <summary>Live presence text as the site prints it (Mostaql «متصل الآن»); null = absent/offline.</summary>
    public string? LivePresence { get; init; }

    public DateTimeOffset LastSeenAt { get; init; } = DateTimeOffset.UtcNow;
}

namespace MostaqlK.Core.Sources;

/// <summary>
/// The universal project key: a website's own numeric project id is only unique
/// <em>within that site</em>, so every cross-cutting address (queue, tracker, diff,
/// backlog, notifications, routes, repositories) carries its <see cref="SourceId"/>
/// alongside it.
/// </summary>
public readonly record struct SourceProjectId(SourceId Source, long ProjectId)
{
    /// <summary>Convenience for single-source call sites that already know the source.</summary>
    public static SourceProjectId Of(SourceId source, long projectId) => new(source, projectId);

    public override string ToString() => $"{Source.Value}:{ProjectId}";
}

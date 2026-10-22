using MostaqlK.Core.Sources;

namespace MostaqlK.Services.Pipeline.DiffEngine;

/// <summary>
/// Outcome of diffing a freshly polled listing page against known committed/in-flight state.
/// </summary>
public sealed class DiffResult
{
    public List<SourceProjectId> NewProjectIds { get; } = [];

    public List<SourceProjectId> AlreadyKnownProjectIds { get; } = [];
}

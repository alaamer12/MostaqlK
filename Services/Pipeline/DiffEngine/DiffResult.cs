namespace MostaqlK.Services.Pipeline.DiffEngine;

/// <summary>
/// Outcome of diffing a freshly polled listing page against known committed/in-flight state.
/// </summary>
public sealed class DiffResult
{
    public List<long> NewProjectIds { get; } = [];

    public List<long> AlreadyKnownProjectIds { get; } = [];
}

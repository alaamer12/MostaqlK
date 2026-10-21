namespace MostaqlK.Core.Sources;

/// <summary>
/// What a source's pages can yield. UI and persistence consult these flags instead of
/// branching on a source name — a missing capability means the section/field is simply
/// never rendered, never rendered empty.
/// </summary>
public sealed record SourceCapabilities(
    bool HasExecutorInfo,
    bool HasSupervisedFlag,
    bool NeedsSessionForFullEnrichment);

namespace MostaqlK.Core.Sources;

/// <summary>
/// The set of sources this build watches. Rail tiles, pipeline instances, settings
/// sections, and status aggregation all enumerate this — which is why a disabled or
/// removed source can never leave a ghost tile or a dead feed behind.
/// </summary>
public sealed class SourceRegistry
{
    private readonly Dictionary<SourceId, ISourceDescriptor> _byId;

    public SourceRegistry(IEnumerable<ISourceDescriptor> descriptors)
    {
        _byId = new Dictionary<SourceId, ISourceDescriptor>();
        foreach (var descriptor in descriptors)
        {
            _byId[descriptor.Id] = descriptor;
        }

        All = new List<ISourceDescriptor>(_byId.Values).AsReadOnly();
    }

    /// <summary>Every registered source, in registration order.</summary>
    public IReadOnlyList<ISourceDescriptor> All { get; }

    public ISourceDescriptor Get(SourceId id) => _byId.TryGetValue(id, out var descriptor)
        ? descriptor
        : throw new KeyNotFoundException($"Source '{id}' is not registered.");

    public bool TryGet(SourceId id, out ISourceDescriptor descriptor)
    {
        var found = _byId.TryGetValue(id, out var value);
        descriptor = value!;
        return found;
    }

    public bool Contains(SourceId id) => _byId.ContainsKey(id);
}

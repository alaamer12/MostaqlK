namespace MostaqlK.Core.Layers;

/// <summary>Base for all layer-resolution failures. Resolution never falls back silently.</summary>
public abstract class LayerException(string message) : Exception(message);

/// <summary>
/// Two matching layers claim the same seam with equal specificity and no tie-breaker layer names
/// both. The build/startup must fail loudly until someone registers the resolution as a decision.
/// </summary>
public sealed class AmbiguousLayerException(string seamId, IReadOnlyList<LayerKey> matchingKeys)
    : LayerException($"Layer collision on seam '{seamId}': {string.Join(" vs ", matchingKeys)} match with equal specificity and no tie-breaker layer exists.");

/// <summary>Two registrations occupy the same cell (same key) for the same seam.</summary>
public sealed class DuplicateLayerException(string seamId, LayerKey key)
    : LayerException($"Duplicate layer registration on seam '{seamId}' in cell {key}.");

/// <summary>A seam has no base layer, so a build with no matching override would resolve to nothing.</summary>
public sealed class MissingBaseException(string seamId)
    : LayerException($"Seam '{seamId}' has no base layer (empty key).");

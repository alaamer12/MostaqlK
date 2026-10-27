namespace MostaqlK.Core.Layers;

/// <summary>
/// Chooses which registered layer fulfils a seam for the current context. Pure and framework-free
/// so the offline test harness can prove the resolution rules without a running app.
/// </summary>
public static class LayerResolver
{
    /// <summary>
    /// Single-winner resolution for implementation seams: among matching layers the one with the
    /// most coordinates wins; equal-specificity collisions throw until a tie-breaker layer is
    /// registered; an absent base throws.
    /// </summary>
    public static T Resolve<T>(string seamId, IReadOnlyList<LayerRegistration<T>> layers, IReadOnlyDictionary<LayerAxis, string> context)
    {
        EnsureNoDuplicateCells(seamId, layers);

        var hits = layers.Where(l => l.Key.Matches(context)).ToList();
        if (hits.Count == 0)
        {
            throw new MissingBaseException(seamId);
        }

        var best = hits.Max(l => l.Key.Specificity);
        var top = hits.Where(l => l.Key.Specificity == best).ToList();
        if (top.Count > 1)
        {
            throw new AmbiguousLayerException(seamId, top.Select(l => l.Key).ToList());
        }

        return top[0].Value;
    }

    /// <summary>
    /// Chain resolution for merge-style seams (e.g. settings descriptors): returns every matching
    /// layer ordered by ascending specificity (base first) so the caller can fold them — base
    /// implementation, then each layer's patch on top. Duplicate cells still fail; equal-
    /// specificity cross-axis collisions are detected by the caller's merge when two layers at the
    /// same level try to change the same field.
    /// </summary>
    public static IReadOnlyList<LayerRegistration<T>> ResolveChain<T>(string seamId, IReadOnlyList<LayerRegistration<T>> layers, IReadOnlyDictionary<LayerAxis, string> context, bool requireBase = true)
    {
        EnsureNoDuplicateCells(seamId, layers);

        var hits = layers.Where(l => l.Key.Matches(context)).ToList();
        if (hits.Count == 0)
        {
            // An implementation seam with nothing to fall back to is a configuration error. A
            // COLLECTION seam (settings rows, nav items) is additive: a layer registered for
            // coordinates the current context does not satisfy simply means "this entry is not
            // present for you", which the caller resolves by skipping it.
            if (requireBase)
            {
                throw new MissingBaseException(seamId);
            }

            return [];
        }

        return hits
            .OrderBy(l => l.Key.Specificity)
            .ThenBy(l => l.Key, KeyOrderComparer.Instance)
            .ToList();
    }

    private static void EnsureNoDuplicateCells<T>(string seamId, IReadOnlyList<LayerRegistration<T>> layers)
    {
        var duplicate = layers.GroupBy(l => l.Key).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new DuplicateLayerException(seamId, duplicate.Key);
        }
    }

    /// <summary>Deterministic ordering of same-specificity keys so a chain never depends on registration order.</summary>
    private sealed class KeyOrderComparer : IComparer<LayerKey>
    {
        public static readonly KeyOrderComparer Instance = new();

        public int Compare(LayerKey? x, LayerKey? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var left = x.Coordinates;
            var right = y.Coordinates;
            for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
            {
                var byAxis = left[i].Axis.CompareTo(right[i].Axis);
                if (byAxis != 0) return byAxis;

                var byValue = string.CompareOrdinal(left[i].Value, right[i].Value);
                if (byValue != 0) return byValue;
            }

            return left.Count.CompareTo(right.Count);
        }
    }
}

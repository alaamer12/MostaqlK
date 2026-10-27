using System.Collections.Immutable;

namespace MostaqlK.Core.Layers;

/// <summary>One (axis, value) coordinate of a <see cref="LayerKey"/>.</summary>
public sealed record LayerCoordinate(LayerAxis Axis, string Value);

/// <summary>
/// The explicit coordinates a layer applies to. An empty key is the base layer; a one-pair key is
/// a single-axis layer; a multi-pair key is a tie-breaker that exists only where single-axis layers
/// collide. A layer matches a context when every coordinate equals the context's current value.
/// </summary>
public sealed class LayerKey : IEquatable<LayerKey>
{
    /// <summary>The base layer: applies to every context.</summary>
    public static readonly LayerKey Base = new();

    private readonly ImmutableArray<LayerCoordinate> _coordinates;

    public LayerKey(params LayerCoordinate[] coordinates)
    {
        _coordinates = coordinates
            .OrderBy(c => c.Axis)
            .ToImmutableArray();

        var duplicateAxis = _coordinates.GroupBy(c => c.Axis).FirstOrDefault(g => g.Count() > 1);
        if (duplicateAxis is not null)
        {
            throw new ArgumentException($"Layer key binds axis '{duplicateAxis.Key}' twice.");
        }
    }

    /// <summary>Number of coordinates — how specific this layer is. Base is 0.</summary>
    public int Specificity => _coordinates.Length;

    public IReadOnlyList<LayerCoordinate> Coordinates => _coordinates;

    /// <summary>True when every coordinate equals the context's value for that axis.</summary>
    public bool Matches(IReadOnlyDictionary<LayerAxis, string> context)
    {
        foreach (var coordinate in _coordinates)
        {
            if (!context.TryGetValue(coordinate.Axis, out var current)
                || !string.Equals(current, coordinate.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Convenience: <c>LayerKey.For(LayerAxis.Source, "nafezly")</c>.</summary>
    public static LayerKey For(LayerAxis axis, string value) => new(new LayerCoordinate(axis, value));

    public bool Equals(LayerKey? other) =>
        other is not null && _coordinates.SequenceEqual(other._coordinates);

    public override bool Equals(object? obj) => Equals(obj as LayerKey);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var coordinate in _coordinates)
        {
            hash.Add(coordinate);
        }

        return hash.ToHashCode();
    }

    public override string ToString() =>
        _coordinates.Length == 0 ? "{}" : "{" + string.Join(", ", _coordinates.Select(c => $"{c.Axis}={c.Value}")) + "}";
}

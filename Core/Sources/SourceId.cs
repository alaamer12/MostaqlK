namespace MostaqlK.Core.Sources;

/// <summary>
/// Identifies one monitored website (mostaql, nafezly, …). A string value so it maps
/// directly onto the DB <c>source_id</c> column, the Preferences container name, the
/// secrets key, and the <see cref="SourceRegistry"/> key without enum↔string tables.
/// </summary>
public readonly record struct SourceId
{
    /// <summary>The raw identifier ("mostaql", "nafezly", …). Never null.</summary>
    public string Value { get; }

    private SourceId(string value) => Value = value;

    public static SourceId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Source id must be non-empty.", nameof(value));
        }

        return new SourceId(value.Trim().ToLowerInvariant());
    }

    public static bool TryFrom(string? value, out SourceId id)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            id = default;
            return false;
        }

        id = new SourceId(value.Trim().ToLowerInvariant());
        return true;
    }

    /// <summary>The only source registered until the nafezly module lands.</summary>
    public static SourceId Mostaql { get; } = new("mostaql");

    /// <summary>
    /// The source assumed by deep links and rows that predate multi-source storage,
    /// so v1 URLs keep working unchanged.
    /// </summary>
    public static SourceId Default { get; } = Mostaql;

    public override string ToString() => Value ?? string.Empty;
}

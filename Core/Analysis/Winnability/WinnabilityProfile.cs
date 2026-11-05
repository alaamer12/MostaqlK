namespace MostaqlK.Core.Analysis;

/// <summary>
/// Per-source winnability weighting for ONE factor key — the data half of what
/// <see cref="MostaqlK.Core.Parsing.FieldProfileData"/> is to the inference engine. Pure data,
/// no behavior: the shared <see cref="WinnabilityEngine"/> consumes it unchanged for any source,
/// and each source supplies its own table so Mostaql and Nafezly can never share a weight set
/// (v4 requirement R4.3 / decision D17).
///
/// Which factors a source can even attempt is a function of its <see cref="RequiredFields"/>:
/// a factor whose required field is absent from the scraped profile renormalises away
/// (R4.4 / D18) and lowers the coverage figure, rather than being scored as neutral.
/// </summary>
public sealed record WinFactorData
{
    /// <summary>Field keys this factor needs. If any is unavailable the factor contributes nothing.</summary>
    public required IReadOnlyList<string> RequiredFields { get; init; }

    /// <summary>Relative contribution 0..1. Hand-authored, never fitted (D17); values deferred by D44.</summary>
    public required double Weight { get; init; }

    /// <summary>True when a HIGHER raw value is WORSE (bidder count, black-hole ratio) — the engine inverts the sub-score.</summary>
    public bool Invert { get; init; }

    /// <summary>True when this factor's input ages. Only these are multiplied by the staleness decay f(t) (R4.11).</summary>
    public bool TimeSensitive { get; init; }
}

/// <summary>One (factor key, factor data) pair. List order is the engine's iteration order.</summary>
public sealed record WinFactorEntry(string Key, WinFactorData Factor);

/// <summary>
/// A source's complete winnability table, consumed by <see cref="WinnabilityEngine.Score"/>.
/// An ordered list rather than a dictionary, mirroring <see cref="MostaqlK.Core.Parsing.FieldProfileTable"/>:
/// enumeration order is contractual so two sources with the same factors in different orders
/// produce a documented, reproducible sequence.
/// </summary>
public sealed record WinnabilityProfileTable(IReadOnlyList<WinFactorEntry> Entries);

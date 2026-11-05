namespace MostaqlK.Core.Analysis;

/// <summary>
/// The source-blind projection of everything a score is computed from: the numeric factor inputs,
/// keyed by the exact field names a <see cref="WinFactorData.RequiredFields"/> list references,
/// plus the moment the underlying data was captured. Which source produced these numbers is not
/// represented here — that is carried by the table handed to <see cref="WinnabilityEngine"/>.
///
/// R4.12: <see cref="CrawledAt"/> is what the decay is derived from at read time; the score itself
/// is never stored, so aging changes it on the next open without any data being rewritten.
/// </summary>
public sealed record WinnableProject
{
    /// <summary>Factor inputs keyed by field name. A key's presence means the value was actually
    /// measured; absence means it renormalises away (the engine does not invent a neutral value).</summary>
    public required IReadOnlyDictionary<string, double> Measurements { get; init; }

    /// <summary>When the data behind this projection was captured, for the staleness decay.</summary>
    public required DateTimeOffset CrawledAt { get; init; }

    /// <summary>True when a field was measured and is usable as a factor input.</summary>
    public bool Has(string field) => Measurements.ContainsKey(field);
}

/// <summary>One factor's contribution, so the details page can show the breakdown (R5.1) and the
/// feed can show coverage. A factor that renormalised away is reported, not hidden.</summary>
public sealed record WinFactorOutcome(
    string Key,
    bool Contributed,
    double NormalizedWeight,
    double? SubScore,
    bool DecayApplied,
    double DecayMultiplier);

/// <summary>
/// The engine's output. <see cref="Score"/> is a 0–100 RELATIVE INDEX, never a probability (R4.1 /
/// D15). It is null when no factor contributed — the surface must then say "not enough data", never
/// render a fabricated zero. <see cref="Coverage"/> is mandatory (F1 / D18): a 3-factor Mostaql
/// number has to sit beside its coverage, not masquerade as a 9-factor Nafezly one.
/// </summary>
public sealed record WinnabilityResult(
    int? Score,
    int ContributedCount,
    int TotalCount,
    double Coverage,
    IReadOnlyList<WinFactorOutcome> Factors,
    IReadOnlyList<string> Caveats);

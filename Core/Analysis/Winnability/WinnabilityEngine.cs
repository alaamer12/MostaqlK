namespace MostaqlK.Core.Analysis;

/// <summary>
/// The shared, source-blind winnability engine — the analogue of
/// <see cref="MostaqlK.Core.Parsing.FieldInferenceEngine"/> for scoring. It names no platform and
/// reads only the table it is handed (R4.3 / D17 keep the weights per source; the algorithm is
/// single-sourced here). Nothing else in the app may reach in and special-case a site.
///
/// The two pieces that ARE fully settled and therefore implemented now: the renormalise-over-
/// available-factors rule (R4.4 / D18) and the staleness decay on time-sensitive factors
/// (R4.11 / D42). The per-factor sub-score shapes (A1..E3) land in the engine-completion phase;
/// until then every available factor evaluates to zero, so the index is honestly 0 rather than a
/// provisional guess dressed up as a number.
/// </summary>
public static class WinnabilityEngine
{
    /// <summary>Decay horizon in days — the "one week old" reference point. Tunable, not derived.</summary>
    public const double DecayHorizonDays = 7.0;

    /// <summary>Decay steepness. Tunable, not derived. Named so a successor sees R4.11 chose it.</summary>
    public const double DecayLambda = 0.9;

    /// <summary>
    /// f(t) = 1 / (1 + λ·ln(1 + t/H)) — strictly decreasing forever (no flat region), bounded away
    /// from zero (no clamp or branch), half the weight gone at two weeks, a sixth at a year
    /// (R4.11). Applied only to <see cref="WinFactorData.TimeSensitive"/> factors.
    /// </summary>
    public static double DecayFactor(DateTimeOffset crawledAt, DateTimeOffset now)
    {
        double days = (now - crawledAt).TotalDays;
        if (days <= 0)
            return 1.0;
        return 1.0 / (1.0 + DecayLambda * Math.Log(1.0 + days / DecayHorizonDays));
    }

    /// <summary>
    /// Compute the relative winnability index. <paramref name="now"/> is injected so the decay is
    /// testable and derived at read time; callers pass the current clock, never a stored value.
    /// </summary>
    public static WinnabilityResult Score(
        WinnableProject project,
        WinnabilityProfileTable table,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(table);

        // Pass 1: which factors can contribute, and over what total weight they renormalise.
        double totalUsableWeight = 0.0;
        foreach (var entry in table.Entries)
        {
            if (AllPresent(project, entry.Factor.RequiredFields))
                totalUsableWeight += entry.Factor.Weight;
        }

        var outcomes = new List<WinFactorOutcome>(table.Entries.Count);
        double decay = DecayFactor(project.CrawledAt, now);
        double weighted = 0.0;
        int contributed = 0;

        // Pass 2: renormalise each usable factor over the usable total, then decay the time-sensitive ones.
        foreach (var entry in table.Entries)
        {
            bool usable = totalUsableWeight > 0 && AllPresent(project, entry.Factor.RequiredFields);
            if (!usable)
            {
                outcomes.Add(new WinFactorOutcome(entry.Key, false, 0.0, null, false, 1.0));
                continue;
            }

            double normalizedWeight = entry.Factor.Weight / totalUsableWeight;
            double multiplier = entry.Factor.TimeSensitive ? decay : 1.0;
            double sub = Evaluate(entry.Key, project, entry.Factor); // 0..1, filled by the engine-completion phase
            weighted += normalizedWeight * multiplier * sub;
            contributed++;
            outcomes.Add(new WinFactorOutcome(entry.Key, true, normalizedWeight, sub, entry.Factor.TimeSensitive, multiplier));
        }

        int? score = contributed == 0 ? null : (int)Math.Clamp(Math.Round(weighted * 100.0), 0, 100);
        double coverage = table.Entries.Count == 0 ? 0.0 : (double)contributed / table.Entries.Count;

        return new WinnabilityResult(
            score,
            contributed,
            table.Entries.Count,
            coverage,
            outcomes,
            Caveats: SelfSelectionCaveat());
    }

    private static bool AllPresent(WinnableProject project, IReadOnlyList<string> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (!project.Has(fields[i]))
                return false;
        }
        return fields.Count > 0;
    }

    /// <summary>
    /// A single factor's 0..1 sub-score. Returns 0 for now: the factor shapes (A1..E3) and their
    /// hand-authored weight values are the engine-completion phase's job, and D44 defers the values
    /// to an experiment. A zero here is deliberate honesty, not an unfinished branch.
    /// </summary>
    private static double Evaluate(string key, WinnableProject project, WinFactorData factor) => 0.0;

    // F3: a professional model states its own bias. No weighting fixes it, so the breakdown carries it.
    private static IReadOnlyList<string> SelfSelectionCaveat() => new[]
    {
        "Win rate is drawn only from projects this user chose to bid on; it is biased upward against "
        + "projects they would have skipped. Stated, not corrected (F3)."
    };
}

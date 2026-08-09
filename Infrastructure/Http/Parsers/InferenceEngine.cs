using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace MostaqlK.Infrastructure.Http.Parsers;

/// <summary>
/// Resolution produced by <see cref="InferenceEngine.InferFields"/> for a single field key.
/// Mirrors the per-field dict returned by inference.py's <c>resolve_fields()</c> (a subset:
/// value/confidence/strategy - runner-ups are not carried over since nothing downstream
/// currently consumes them).
/// </summary>
public sealed record FieldInferenceResult(string? Value, double Confidence, string Strategy);

/// <summary>
/// Ports the context-aware, structure-independent scoring engine from the Python prototype
/// <c>inference.py</c> to C#/HtmlAgilityPack. See
/// <c>.repertoire/progress/python/parser/scratch/inference.py</c> for the original - every
/// method below is commented with the section/function it mirrors so fidelity can be
/// spot-checked against it.
/// </summary>
public static class InferenceEngine
{
    // -----------------------------------------------------------------
    // Tunable weights (hand-set, not learned) - mirrors inference.py's WEIGHTS dict exactly.
    // -----------------------------------------------------------------
    private const double StemWeight = 3.0;
    private const double UnitWeight = 2.0;
    private const double TypeWeight = 1.0;
    private const double PositionWeight = 0.5;
    private const double MissingUnitPenalty = -1.5;
    private const int BoilerplateDampingThreshold = 6;
    private const double BoilerplateDampingFactor = 0.35;

    private const int LocalWindowTokens = 12;
    private const double LocalConfidenceMargin = 0.20;

    // -----------------------------------------------------------------
    // Arabic digit normalization + crude stemming - mirrors inference.py lines 50-90.
    // -----------------------------------------------------------------
    private const string ArabicDigits = "٠١٢٣٤٥٦٧٨٩";

    private static readonly string[] Prefixes = ["ال", "و", "ف", "ب", "ل", "لل"];
    private static readonly string[] Suffixes = ["ها", "هم", "ه", "ة", "ات", "ين", "ون", "ي", "ا"];
    private static readonly string[] PrefixesByLenDesc = Prefixes.OrderByDescending(p => p.Length).ToArray();
    private static readonly string[] SuffixesByLenDesc = Suffixes.OrderByDescending(s => s.Length).ToArray();

    /// <summary>Mirrors _strip_affixes: crude Arabic prefix/suffix stripping, not a synonym table.</summary>
    private static string StripAffixes(string word)
    {
        var w = word;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var p in PrefixesByLenDesc)
            {
                if (w.StartsWith(p, StringComparison.Ordinal) && w.Length - p.Length >= 2)
                {
                    w = w[p.Length..];
                    changed = true;
                    break;
                }
            }
        }
        foreach (var s in SuffixesByLenDesc)
        {
            if (w.EndsWith(s, StringComparison.Ordinal) && w.Length - s.Length >= 2)
            {
                w = w[..^s.Length];
                break;
            }
        }
        return w;
    }

    /// <summary>Mirrors normalize_ws.</summary>
    private static string NormalizeWs(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : Regex.Replace(s, @"\s+", " ").Trim();

    private static readonly Regex DiacriticsRegex = new(@"[\u064B-\u065F\u0670\u0640]", RegexOptions.Compiled);
    private static readonly char[] StemTrimChars = ['،', '.', ',', ':', '؛', ';', '(', ')', '[', ']', '{', '}', '»', '«', '"', '\''];

    /// <summary>Mirrors stem(): strip diacritics/tatweel, punctuation, then affixes.</summary>
    private static string Stem(string word)
    {
        var w = NormalizeWs(word);
        w = DiacriticsRegex.Replace(w, string.Empty);
        w = w.Trim(StemTrimChars);
        return w.Length == 0 ? string.Empty : StripAffixes(w);
    }

    // -----------------------------------------------------------------
    // Value type patterns - mirrors inference.py lines 93-132.
    // -----------------------------------------------------------------
    private static readonly Regex PercentRe = new(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled);
    private static readonly Regex RangeRe = new(@"\$?\s*([\d.]+)\s*-\s*\$?\s*([\d.]+)", RegexOptions.Compiled);
    private static readonly Regex FloatRe = new(@"\b\d+\.\d+\b", RegexOptions.Compiled);
    private static readonly Regex IntRe = new(@"(?<!\.)\b\d+\b(?!\.\d)", RegexOptions.Compiled);
    private static readonly string[] CurrencySyms = ["$", "usd", "دولار", "ريال", "sar", "egp", "جنيه"];
    private static readonly string[] NotCalculatedMarkers = ["لم يحسب بعد", "غير محدد", "n/a", "لا يوجد"];

    private static readonly string[] DurationUnits =
        ["يوم", "يوما", "أيام", "ايام", "ساعة", "ساعات", "أسبوع", "اسبوع", "أسابيع", "شهر", "أشهر"];
    private static readonly string[] RelativeDateWords = ["منذ", "قبل", "خلال"];
    private static readonly Regex AbsoluteDateRe =
        new(@"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}\b|\b\d{1,2}[-/]\d{1,2}[-/]\d{4}\b", RegexOptions.Compiled);
    private static readonly string[] MonthNames =
        ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"];

    /// <summary>Translates Arabic-Indic digits to ASCII digits (mirrors str.translate(ARABIC_TO_ASCII)).</summary>
    private static string ToAsciiDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            var idx = ArabicDigits.IndexOf(c);
            sb.Append(idx >= 0 ? (char)('0' + idx) : c);
        }
        return sb.ToString();
    }

    /// <summary>Mirrors classify_value_types.</summary>
    private static HashSet<string> ClassifyValueTypes(string tokenText)
    {
        var t = tokenText.Trim();
        var types = new HashSet<string>();
        if (t.Length == 0)
        {
            return types;
        }
        var asciiT = ToAsciiDigits(t);
        var tLower = t.ToLowerInvariant();
        if (NotCalculatedMarkers.Any(m => tLower.Contains(m)))
        {
            types.Add("PLACEHOLDER");
            return types;
        }
        if (PercentRe.IsMatch(asciiT))
        {
            types.Add("PERCENT");
        }
        if (RangeRe.IsMatch(asciiT) && asciiT.Contains('-'))
        {
            types.Add("RANGE");
        }
        if (AbsoluteDateRe.IsMatch(asciiT))
        {
            types.Add("DATE");
        }
        if (FloatRe.IsMatch(asciiT))
        {
            types.Add("FLOAT");
            types.Add("NUMBER");
        }
        else if (IntRe.IsMatch(asciiT))
        {
            types.Add("NUMBER");
        }
        return types;
    }

    // -----------------------------------------------------------------
    // Field profiles - mirrors inference.py's FIELD_PROFILES exactly (same field keys,
    // same core_stems/expected_types/unit_hints/requires_unit per field).
    // -----------------------------------------------------------------
    private sealed class FieldProfile
    {
        public string[] CoreStems { get; init; } = [];
        public HashSet<string> ExpectedTypes { get; init; } = [];
        public HashSet<string> ExpectedTypesWeak { get; init; } = [];
        public string[] UnitHints { get; init; } = [];
        public bool RequiresUnit { get; init; }
    }

    private static readonly string[] StartedSinceUnitHints = RelativeDateWords.Concat(DurationUnits).ToArray();

    private static readonly Dictionary<string, FieldProfile> FieldProfiles = new()
    {
        ["project_status"] = new FieldProfile
        {
            CoreStems = [Stem("حالة"), Stem("المشروع")],
            ExpectedTypes = ["TEXT"],
            UnitHints = [],
            RequiresUnit = false,
        },
        ["published_date"] = new FieldProfile
        {
            CoreStems = [Stem("نشر"), Stem("تاريخ")],
            ExpectedTypes = ["DATE"],
            ExpectedTypesWeak = ["NUMBER"],
            UnitHints = MonthNames,
            RequiresUnit = false,
        },
        ["budget"] = new FieldProfile
        {
            CoreStems = [Stem("ميزانية"), Stem("تكلفة"), Stem("سعر")],
            ExpectedTypes = ["NUMBER", "FLOAT", "RANGE", "PLACEHOLDER"],
            UnitHints = CurrencySyms,
            RequiresUnit = true,
        },
        ["duration"] = new FieldProfile
        {
            CoreStems = [Stem("مدة"), Stem("تنفيذ"), Stem("وقت"), Stem("لازم")],
            ExpectedTypes = ["NUMBER", "FLOAT"],
            UnitHints = DurationUnits,
            RequiresUnit = true,
        },
        ["registration_date"] = new FieldProfile
        {
            CoreStems = [Stem("تسجيل"), Stem("تاريخ")],
            ExpectedTypes = ["DATE"],
            ExpectedTypesWeak = ["NUMBER"],
            UnitHints = MonthNames,
            RequiresUnit = false,
        },
        ["hire_rate"] = new FieldProfile
        {
            CoreStems = [Stem("معدل"), Stem("توظيف")],
            ExpectedTypes = ["PERCENT", "NUMBER"],
            UnitHints = ["%"],
            RequiresUnit = true,
        },
        ["open_projects_count"] = new FieldProfile
        {
            CoreStems = [Stem("مشاريع"), Stem("مفتوحة")],
            ExpectedTypes = ["NUMBER"],
            UnitHints = [],
            RequiresUnit = false,
        },
        ["in_progress_count"] = new FieldProfile
        {
            CoreStems = [Stem("مشاريع"), Stem("تنفيذ")],
            ExpectedTypes = ["NUMBER"],
            UnitHints = [],
            RequiresUnit = false,
        },
        ["ongoing_conversations"] = new FieldProfile
        {
            CoreStems = [Stem("تواصلات"), Stem("جارية")],
            ExpectedTypes = ["NUMBER"],
            UnitHints = [],
            RequiresUnit = false,
        },
        ["started_since"] = new FieldProfile
        {
            CoreStems = [Stem("بدأ"), Stem("تنفيذه"), Stem("منذ")],
            ExpectedTypes = ["DATE", "NUMBER"],
            UnitHints = StartedSinceUnitHints,
            RequiresUnit = false,
        },
        ["deal_date"] = new FieldProfile
        {
            CoreStems = [Stem("تاريخ"), Stem("الصفقة")],
            ExpectedTypes = ["DATE"],
            ExpectedTypesWeak = ["NUMBER"],
            UnitHints = MonthNames,
            RequiresUnit = false,
        },
        ["delivery_date"] = new FieldProfile
        {
            CoreStems = [Stem("موعد"), Stem("التسليم")],
            ExpectedTypes = ["DATE"],
            ExpectedTypesWeak = ["NUMBER"],
            UnitHints = MonthNames,
            RequiresUnit = false,
        },
    };

    // -----------------------------------------------------------------
    // Step 1: flatten the DOM into an ordered text-token stream - mirrors inference.py
    // lines 223-284 (Token, _dom_path, flatten, dom_distance).
    // -----------------------------------------------------------------
    private sealed class Token
    {
        public required string Text { get; init; }
        public required int Index { get; init; }
        public required HtmlNode Element { get; init; }
        public required List<HtmlNode> DomPath { get; init; }
    }

    /// <summary>Mirrors _dom_path: ancestor chain (self included), used only for cheap distance calc.</summary>
    private static List<HtmlNode> BuildDomPath(HtmlNode el)
    {
        var path = new List<HtmlNode>();
        var cur = el;
        while (cur is not null)
        {
            path.Add(cur);
            cur = cur.ParentNode;
        }
        return path;
    }

    /// <summary>
    /// Mirrors flatten(): walk the DOM in document order, split each element's OWN leaf text
    /// (not nested children's text) into whitespace tokens. This word-level flattening is what
    /// defeats "every word in its own span" / "split words across nested elements" tricks.
    /// </summary>
    private static List<Token> Flatten(HtmlNode root)
    {
        var tokens = new List<Token>();
        var idx = 0;
        var elements = root.SelectNodes(".//*") ?? Enumerable.Empty<HtmlNode>();
        foreach (var el in elements)
        {
            var sb = new StringBuilder();
            foreach (var child in el.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Text)
                {
                    sb.Append(((HtmlTextNode)child).Text);
                }
            }
            var own = NormalizeWs(HtmlEntity.DeEntitize(sb.ToString()));
            if (own.Length == 0)
            {
                continue;
            }
            foreach (var word in own.Split(' '))
            {
                if (word.Length == 0)
                {
                    continue;
                }
                tokens.Add(new Token { Text = word, Index = idx, Element = el, DomPath = BuildDomPath(el) });
                idx++;
            }
        }
        return tokens;
    }

    /// <summary>Mirrors dom_distance: cheap hop-count to the shared ancestor, summed across both sides.</summary>
    private static int DomDistance(List<HtmlNode> pathA, List<HtmlNode> pathB)
    {
        var setB = new HashSet<HtmlNode>(pathB);
        var hopsA = 0;
        foreach (var node in pathA)
        {
            if (setB.Contains(node))
            {
                var hopsB = pathB.IndexOf(node);
                return hopsA + hopsB;
            }
            hopsA++;
        }
        return hopsA + pathB.Count;
    }

    // -----------------------------------------------------------------
    // Step 2: candidate extraction from the flattened stream - mirrors inference.py lines
    // 287-386 (Candidate, extract_candidates, _find_adjacent_unit).
    // -----------------------------------------------------------------
    private sealed class Candidate
    {
        public required string RawText { get; init; }
        public required HashSet<string> Types { get; init; }
        public required int TokenIndex { get; init; }
        public required HtmlNode Element { get; init; }
        public required List<HtmlNode> DomPath { get; init; }
        public string? UnitNearby { get; init; }
        public Dictionary<string, double> Scores { get; } = new();
        public Dictionary<string, double> Probabilities { get; } = new();
    }

    private static readonly Regex MergeConnectorRe = new(@"\A(?:[-.]|\$)\Z", RegexOptions.Compiled);
    private static readonly Regex MergeDigitRe = new(@"\A\d+%?\Z", RegexOptions.Compiled);
    private static readonly Regex ValueSeedRe = new(@"\d", RegexOptions.Compiled);
    private static readonly char[] UnitTrimChars = ['،', '.', ',', ':', '؛', ';', '(', ')', '[', ']', '{', '}', '»', '«', '"', '\'', '%', '$'];

    /// <summary>
    /// Mirrors extract_candidates: merges forward only from a digit-bearing seed token (never
    /// starts a merge from an arbitrary word), evaluates both the bare token and the merged
    /// window, de-duplicates, then keeps only the longest candidate per seed token.
    /// </summary>
    private static List<Candidate> ExtractCandidates(List<Token> tokens)
    {
        var candidates = new List<Candidate>();
        var n = tokens.Count;
        var i = 0;
        while (i < n)
        {
            var tok = tokens[i];
            if (!ValueSeedRe.IsMatch(tok.Text))
            {
                i++;
                continue;
            }

            var j = i;
            var windowTexts = new List<string> { tok.Text };
            while (j + 1 < n && (j - i) < 4)
            {
                var nxt = tokens[j + 1];
                if (MergeConnectorRe.IsMatch(nxt.Text) || MergeDigitRe.IsMatch(nxt.Text))
                {
                    windowTexts.Add(nxt.Text);
                    j++;
                }
                else
                {
                    break;
                }
            }
            var merged = windowTexts.Count > 1 ? string.Concat(windowTexts) : tok.Text;

            foreach (var (candidateText, endIdx) in new[] { (tok.Text, i), (merged, j) })
            {
                var types = ClassifyValueTypes(candidateText);
                if (types.Count == 0)
                {
                    continue;
                }
                var unitNearby = FindAdjacentUnit(tokens, endIdx);
                candidates.Add(new Candidate
                {
                    RawText = candidateText,
                    Types = types,
                    TokenIndex = tok.Index,
                    Element = tok.Element,
                    DomPath = tok.DomPath,
                    UnitNearby = unitNearby,
                });
            }

            i = merged != tok.Text ? j + 1 : i + 1;
        }

        // de-duplicate identical (text, token_index) pairs from the merge/bare overlap
        var seen = new HashSet<(string RawText, int TokenIndex)>();
        var deduped = new List<Candidate>();
        foreach (var c in candidates)
        {
            if (seen.Add((c.RawText, c.TokenIndex)))
            {
                deduped.Add(c);
            }
        }

        // Suppress a bare sub-candidate when a longer merged candidate starting at the SAME
        // token already subsumes it.
        var bySeed = new Dictionary<int, List<Candidate>>();
        foreach (var c in deduped)
        {
            if (!bySeed.TryGetValue(c.TokenIndex, out var list))
            {
                list = [];
                bySeed[c.TokenIndex] = list;
            }
            list.Add(c);
        }

        var final = new List<Candidate>();
        foreach (var group in bySeed.Values)
        {
            if (group.Count == 1)
            {
                final.AddRange(group);
                continue;
            }
            final.Add(group.OrderByDescending(c => c.RawText.Length).First());
        }
        return final;
    }

    /// <summary>Mirrors _find_adjacent_unit.</summary>
    private static string? FindAdjacentUnit(List<Token> tokens, int idx, int window = 3)
    {
        var lo = Math.Max(0, idx - window);
        var hi = Math.Min(tokens.Count, idx + window + 1);
        for (var k = lo; k < hi; k++)
        {
            if (k == idx)
            {
                continue;
            }
            var t = tokens[k].Text.Trim(UnitTrimChars);
            var tLower = t.ToLowerInvariant();
            if (DurationUnits.Any(u => string.Equals(u, tLower, StringComparison.OrdinalIgnoreCase))
                || t == "%"
                || CurrencySyms.Any(cs => tokens[k].Text.ToLowerInvariant().Contains(cs)))
            {
                return tokens[k].Text;
            }
        }
        return null;
    }

    // -----------------------------------------------------------------
    // Step 3+4: scoring - mirrors inference.py lines 389-508.
    // -----------------------------------------------------------------
    private static Dictionary<string, int> PageWideStemCounts(List<Token> tokens)
    {
        var counts = new Dictionary<string, int>();
        foreach (var tok in tokens)
        {
            var s = Stem(tok.Text);
            if (s.Length > 0)
            {
                counts[s] = counts.GetValueOrDefault(s) + 1;
            }
        }
        return counts;
    }

    private static List<Token> LocalWindow(List<Token> tokens, int centerIdx, int window = LocalWindowTokens)
    {
        var lo = Math.Max(0, centerIdx - window);
        var hi = Math.Min(tokens.Count, centerIdx + window + 1);
        return tokens.GetRange(lo, hi - lo);
    }

    /// <summary>Mirrors score_candidate: additive score for `candidate` against every field profile.</summary>
    private static void ScoreCandidate(Candidate candidate, List<Token> tokens, Dictionary<string, int> stemCounts)
    {
        var nearby = LocalWindow(tokens, candidate.TokenIndex);
        var nearbyTextJoin = string.Join(" ", nearby.Select(t => t.Text)).ToLowerInvariant();

        foreach (var (field, profile) in FieldProfiles)
        {
            var score = 0.0;

            // --- stem signal, with token-distance decay and DOM-distance decay ---
            double? bestStemHit = null;
            foreach (var t in nearby)
            {
                var s = Stem(t.Text);
                if (s.Length == 0 || !profile.CoreStems.Contains(s))
                {
                    continue;
                }
                var tokenDist = Math.Abs(t.Index - candidate.TokenIndex);
                var domDist = DomDistance(t.DomPath, candidate.DomPath);
                var dist = Math.Min(tokenDist, domDist);
                var decayed = 1.0 / (1.0 + dist);
                var weight = StemWeight;
                if (stemCounts.GetValueOrDefault(s) >= BoilerplateDampingThreshold)
                {
                    weight *= BoilerplateDampingFactor;
                }
                if (t.Index > candidate.TokenIndex)
                {
                    weight *= 0.5;
                }
                var contribution = weight * decayed;
                if (bestStemHit is null || contribution > bestStemHit)
                {
                    bestStemHit = contribution;
                }
            }
            if (bestStemHit.HasValue)
            {
                score += bestStemHit.Value;
            }

            // --- unit signal ---
            var unitHit = false;
            if (candidate.UnitNearby is not null)
            {
                var unitLower = candidate.UnitNearby.ToLowerInvariant();
                if (profile.UnitHints.Any(u => unitLower.Contains(u.ToLowerInvariant()) || u.ToLowerInvariant().Contains(unitLower)))
                {
                    unitHit = true;
                }
            }
            else if (profile.UnitHints.Any(u => nearbyTextJoin.Contains(u.ToLowerInvariant())))
            {
                unitHit = true;
            }
            if (unitHit)
            {
                score += UnitWeight;
            }
            else if (profile.RequiresUnit)
            {
                score += MissingUnitPenalty;
            }

            // --- type compatibility signal ---
            if (candidate.Types.Overlaps(profile.ExpectedTypes))
            {
                score += TypeWeight;
            }
            else if (candidate.Types.Overlaps(profile.ExpectedTypesWeak))
            {
                score += TypeWeight * 0.25;
            }

            candidate.Scores[field] = score;
        }
    }

    /// <summary>Mirrors apply_position_prior: boosts candidates in a dense "metadata cluster".</summary>
    private static void ApplyPositionPrior(List<Candidate> candidates)
    {
        foreach (var c in candidates)
        {
            var clusterSize = candidates.Count(other => !ReferenceEquals(other, c) && DomDistance(other.DomPath, c.DomPath) <= 3);
            if (clusterSize < 2)
            {
                continue;
            }
            foreach (var field in c.Scores.Keys.ToList())
            {
                c.Scores[field] += PositionWeight;
            }
        }
    }

    /// <summary>Mirrors softmax().</summary>
    private static Dictionary<string, double> Softmax(Dictionary<string, double> scoreMap)
    {
        if (scoreMap.Count == 0)
        {
            return new Dictionary<string, double>();
        }
        var m = scoreMap.Values.Max();
        var exps = scoreMap.ToDictionary(kv => kv.Key, kv => Math.Exp(kv.Value - m));
        var total = exps.Values.Sum();
        if (total == 0)
        {
            total = 1.0;
        }
        return exps.ToDictionary(kv => kv.Key, kv => kv.Value / total);
    }

    private static List<Candidate> ScoreAll(List<Candidate> candidates, List<Token> tokens, Dictionary<string, int> stemCounts)
    {
        foreach (var c in candidates)
        {
            ScoreCandidate(c, tokens, stemCounts);
        }
        ApplyPositionPrior(candidates);
        foreach (var c in candidates)
        {
            foreach (var kv in Softmax(c.Scores))
            {
                c.Probabilities[kv.Key] = kv.Value;
            }
        }
        return candidates;
    }

    // -----------------------------------------------------------------
    // Step 5: resolve one winner per field - mirrors resolve_fields().
    // -----------------------------------------------------------------
    private static Dictionary<string, FieldInferenceResult> ResolveFields(List<Candidate> candidates)
    {
        var perField = FieldProfiles.Keys.ToDictionary(f => f, _ => new List<(double Prob, Candidate Cand)>());
        foreach (var c in candidates)
        {
            foreach (var (field, prob) in c.Probabilities)
            {
                perField[field].Add((prob, c));
            }
        }

        var results = new Dictionary<string, FieldInferenceResult>();
        foreach (var (field, scored) in perField)
        {
            if (scored.Count == 0)
            {
                results[field] = new FieldInferenceResult(null, 0.0, "no_candidates_found");
                continue;
            }
            scored.Sort((a, b) => b.Prob.CompareTo(a.Prob));
            var (topProb, topCand) = scored[0];
            var runnerUps = scored.Skip(1).Take(3).ToList();
            var margin = topProb - (runnerUps.Count > 0 ? runnerUps[0].Prob : 0.0);
            var strategy = margin >= LocalConfidenceMargin ? "local_inference" : "global_inference_ambiguous";
            var value = topCand.RawText + (topCand.UnitNearby is not null && !topCand.RawText.Contains(topCand.UnitNearby)
                ? $" {topCand.UnitNearby}"
                : string.Empty);
            results[field] = new FieldInferenceResult(value, Math.Round(topProb, 3), strategy);
        }
        return results;
    }

    // -----------------------------------------------------------------
    // Public entry point - mirrors infer_fields().
    // -----------------------------------------------------------------
    public static Dictionary<string, FieldInferenceResult> InferFields(HtmlNode root)
    {
        var tokens = Flatten(root);
        var stemCounts = PageWideStemCounts(tokens);
        var candidates = ExtractCandidates(tokens);
        candidates = ScoreAll(candidates, tokens, stemCounts);
        return ResolveFields(candidates);
    }
}

# parsers-inference-worker — Wave B6 Report

**Date:** 2026-08-25 · **Scope:** §3.D InferenceEngine paragraph, §4.1 traps 6/19/21, §8 signature

## Goal

Port `Infrastructure/Http/Parsers/InferenceEngine.cs` fully to
`.repertoire/python/src/mostaql/scraping/parsers/inference.py` as a structure-independent
scoring engine, with a self-contained test suite, passing all quality gates.

## What was done

### Files created/modified (boundaries respected)

| File | Action |
|---|---|
| `.repertoire/python/src/mostaql/scraping/parsers/inference.py` | Replaced Wave-7 stub with full port (~700 lines) |
| `.repertoire/python/tests/test_inference_engine.py` | New — 39 tests, self-contained synthetic HTML |
| `.repertoire/agents/parsers-inference-worker.md` | This report |

No other parser/barrel/config file touched. No scratch files created (none needed).

### Port checklist verification (all against the C# source, line-cited in code)

1. **Flatten** — `.//*` document-order iteration; skips script/style/noscript/template/
   textarea/option/select incl. nested descendants (case-insensitive tag match); own-text =
   `el.text` + direct children's `tail`s (reproduces HAP direct-text-node semantics exactly;
   trap §4.1-9); `\s+`→" "; tokens carry sequential index + source element + self-inclusive
   ancestor path (`_flatten`, `_own_text`, `_build_dom_path`, `_dom_distance`).
2. **Stem()** — normalize-ws → strip `[\u064B-\u065F\u0670\u0640]` → trim
   `،.,:؛;()[]{}»«"'` → `_strip_affixes`: prefixes stripped REPEATEDLY (longest-first each
   iteration via stable descending-length order, remainder ≥2), suffixes EXACTLY ONCE
   (remainder ≥2). Locked: `والكتب`→`كتب`, `كتابها`→`كتاب`, `يوم`→`يوم`, `بلا`→`لا`,
   `الا`→`ال`.
3. **Boilerplate damping** — page-wide stem counts; ×0.35 when count ≥ 6
   (`_page_wide_stem_counts`; relative-comparison test proves damped < unique).
4. **Candidates** — digit-seeded greedy merge up to 4 following tokens matching connector
   `\A(?:[-.]|\$)\Z` or `\A\d+%?\Z` (C# bodies preserved); bare AND merged evaluated;
   adjacent-unit lookup ±3 (trim set incl. `%$`; duration exact-equality on trimmed-lower;
   `%` exact on trimmed; currency substring on UNTRIMMED text); dedupe `(raw_text,
   token_index)` keeping first; longest-per-seed suppression; index advances past merged
   window (`_extract_candidates`, `_merge_window_end`, `_find_adjacent_unit`).
5. **Type classifier** — private `_to_ascii_arabic_digits` maps ONLY U+0660–0669 (trap
   §4.1-6), deliberately distinct from `mostaql.text.to_ascii_digits` (documented at both
   sites); placeholder markers short-circuit to `{PLACEHOLDER}`; Percent/Range/AbsoluteDate/
   Float/Int patterns preserved.
6. **Scoring ×14 profiles** — table copied verbatim (core stems precomputed via the same
   `_stem`, mirroring C# static initializers); stem = 3.0 × damping × (×0.5 if after) ×
   1/(1+min(tokenDist, domDist)) within ±12-token window; unit +2.0 / −1.5 requires-unit
   miss (adjacent-unit branch does NOT fall through to window scan — mirrored);
   type +1.0/+0.25 weak with FLOAT-guard blocking both tiers.
7. **Position prior** — +0.5 on every field when ≥2 other candidates within dom-distance ≤3.
8. **Softmax** — max-subtracted, zero-denominator → 1.0.
9. **resolve_fields** — margin top−runnerUp ≥ 0.20 → `local_inference` else
   `global_inference_ambiguous`; value = RawText + unit if not already contained;
   confidence = round(topProb, 3) (banker's rounding matches Math.Round ToEven); zero
   candidates → (None, 0.0, "no_candidates_found") for all 14 fields.

Constants exported as named module constants mirroring C# names/values
(STEM_WEIGHT … LOCAL_CONFIDENCE_MARGIN).

### Sibling contract honored exactly

`InferenceEngine.infer_fields(root) -> InferFieldsResult`,
dataclasses `InferredField(value: str | None, confidence: float, strategy: str)` /
`InferFieldsResult(fields: dict[str, InferredField])`. Sibling `detail.py` is already
consuming it (observed mid-flight integration).

## One documented deviation (behavior-preserving, required)

**`\d` → `[0-9]` inside the seven regex literals** (Percent/Range/Float/Int/AbsoluteDate/
MergeDigit/ValueSeed). .NET `\d` matches ASCII 0-9 only; Python `\d` matches any Unicode Nd
digit (including Persian U+06F0–F9 and Arabic U+0660–69). Copying `\d` verbatim would have
made Persian digits seed candidates and classify as NUMBER — breaking trap §4.1-6 end-to-end
and contradicting the C# engine's actual behavior. Pattern *bodies* are otherwise verbatim;
each site carries a citation comment explaining the ASCII-only requirement. Consequence
(verified faithful to C#): pure Arabic-Indic values like `٤٢` never seed a candidate either —
seeding/classification keys off ASCII digits exactly as .NET did.

## Test corrections made during verification (locked against real C# behavior, not assumptions)

- `stem("الا") == "ال"` — prefix floor blocks `ال`, then single suffix strip removes `ا`.
- `"10 - 20 30"` merges greedily to `10-2030` (consecutive digit tokens absorbed) — advance-
  past-window proven with an interleaved non-digit word instead.
- Bare `%` token trims to empty under UnitTrimChars → NOT detected as unit (C# quirk kept);
  currency detection uses untrimmed text so `ريال` IS detected.
- `مدة التنفيذ` legitimately ties between duration and started_since (shared stem `تنفيذ`);
  test scenario switched to `المدة` for a clean win assertion.
- Engine-level: BOTH `٤٢` and `۴۲` yield no_candidates (ASCII-only seeding per C#);
  conversion behavior locked at classifier level.

## Verification (from `.repertoire/python`)

| Gate | Result |
|---|---|
| `uv sync` | OK |
| `uv run ruff format .` / `ruff check .` | My two files CLEAN |
| `uv run mypy src` | inference.py CLEAN (strict) |
| `uv run xenon src -b B` | inference.py PASS (all blocks ≤B) |
| `uv run lint-imports` | Both contracts touching scraping KEPT |
| `uv run pytest -q` | **295 passed**, coverage 75.27% (>60 gate); my 39 tests pass |

## Foreign failures (reported, not fixed — sibling-owned, rerun once after 60s per protocol)

1. `mypy src`: 3 errors in `src/mostaql/scraping/parsers/detail.py` (~L150/L185 — tuple vs
   dataclass attribute access). Sibling was actively rewriting detail.py during my run.
2. `xenon src -b B`: C-rank blocks in `detail.py::_resolve_fields`, `detail.py::
   _extract_skills`, `structural.py::{_walk_to_value, find_owner_card, _read_gap_fillers}`.
3. `lint-imports`: contract `httpx-only-in-http-layer` BROKEN via
   `mostaql.scraping.scraper → mostaql.http → httpx`. Note this looks architectural, not
   transient: plan §7 places PageFetcher behind the scraper, but the pyproject contract
   forbids scraping importing `mostaql.http`. Flagging to master for arbitration.

## Concerns

- **Tie-breaking determinism:** C# `List.Sort` (prob desc) is unstable; Python sort is
  stable (insertion order wins ties). Same-input-same-output holds in Python; byte-parity
  with .NET tie ORDER is not guaranteed (confidence/value identical only if tied candidates
  share raw text). Accepted as deterministic-improvement; noted for the parity ledger.
- The lint-imports scraper/http conflict above needs a master decision (contract text vs
  scraper design); outside my boundary either way.

## BOUNCE-FIX 1

**Date:** 2026-08-25 · **Trigger:** Master review rejected the Wave B6 `\d → [0-9]` deviation
as based on a false premise. Verdict accepted: in .NET regular expressions WITHOUT
`RegexOptions.ECMAScript` (the case for every InferenceEngine regex — all are plain
`RegexOptions.Compiled`), `\d` equals `\p{Nd}` and matches Unicode Nd digits including
Arabic-Indic U+0660–69 AND Persian U+06F0–F9. Python `\d` on str patterns is likewise
Unicode Nd. The original verbatim `\d` was therefore already correct parity, and plan
§4.1 trap 6 concerns ONLY the private digit CONVERTER (U+0660–69 but not U+06F0–F9) — it
never authorized touching regex character classes.

### What changed

| File | Change |
|---|---|
| `.repertoire/python/src/mostaql/scraping/parsers/inference.py` | Reverted ALL SEVEN regex literals to verbatim C# bodies: `_PERCENT_RE`, `_RANGE_RE`, `_FLOAT_RE`, `_INT_RE`, `_ABSOLUTE_DATE_RE`, `_MERGE_DIGIT_RE`, `_VALUE_SEED_RE` (each verified against InferenceEngine.cs L96-L99/L106-107/L393-L394). Rewrote the false-premise NOTE block above the value patterns + added a corrected "regexes verbatim / both `\d`s are Unicode Nd" bullet to the module docstring; made the module docstring raw (`r"""`) because the new text contains `` \d `` (xenon surfaced a SyntaxWarning on first run). Removed the two stale `# C# \d = ASCII` comments. Converter `_to_ascii_arabic_digits` and everything else UNCHANGED. |
| `.repertoire/python/tests/test_inference_engine.py` | Flipped three false-premise tests: classify-level `۴۲` now asserts `{"NUMBER"}` while asserting the converter leaves it untouched; engine-level `۴۲`/`٤٢` budget pages now assert resolution (`value == "۴۲ دولار"` / `"٤٢ دولار"`, `local_inference`). Added regression tests: `test_extract_candidates_persian_raw_text_numeric` (candidate from `<p>۴۲</p>` has RawText retaining Persian chars, types `{NUMBER}`) and `test_trap6_semantics_are_converter_scoped_only` (converter maps ٤٢→42, leaves ۴۲; both classify NUMBER). Imported `_to_ascii_arabic_digits`. |

### Verification (from `.repertoire/python`)

```
uv run ruff check .    -> All checks passed!
uv run mypy src        -> Success: no issues found in 43 source files
uv run xenon src -b B  -> clean (no output; SyntaxWarning fixed via r-docstring)
uv run pytest -q       -> 379 passed in 10.09s; coverage 93.42% (>60 gate)
```

### Trap §4.1-6 semantics — confirmation

Trap 6 is now converter-scoped ONLY: the divergence between
`_to_ascii_arabic_digits` (maps U+0660–0669 exclusively) and
`mostaql.text.normalization.to_ascii_digits` (also U+06F0–F9) is preserved,
while seeding (`ValueSeedRe`) and classification (`IntRe` etc.) behave
identically for Arabic-Indic AND Persian digits in both implementations via the
shared Unicode-Nd `\d` — exactly as .NET did. Boundaries respected: only the
two allowed files plus this report were touched.

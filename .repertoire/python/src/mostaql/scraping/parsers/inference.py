r"""Structure-independent inference engine (C# ``Infrastructure/Http/Parsers/InferenceEngine.cs``).

Flattens the DOM into an ordered word-token stream, extracts digit-seeded
value candidates, and scores every candidate against 14 field profiles with
stem/unit/type signals, boilerplate damping, a dense-cluster position prior,
and per-candidate softmax. Every function below cites the C# member it mirrors
so fidelity can be spot-checked (plan §3.D, traps §4.1-6/19/21, §8 contract).

Parity notes:
- Trap §4.1-6: the private ``_to_ascii_arabic_digits`` converter maps ONLY
  U+0660-U+0669. It is deliberately distinct from
  :func:`mostaql.text.normalization.to_ascii_digits`, which additionally maps
  Persian U+06F0-U+06F9. Do not unify them.
- Regex literals are copied VERBATIM from C# because verbatim ``\d`` IS the
  correct parity choice: .NET ``\d`` (without RegexOptions.ECMAScript) equals
  ``\p{Nd}`` and matches Unicode Nd digits exactly like Python ``\d`` on str
  patterns. Arabic-Indic and Persian digits therefore seed candidates and
  classify identically in both implementations; the U+0660-vs-U+06F0
  divergence lives EXCLUSIVELY in the private converter above.
- Trap §4.1-9: lxml.html already decodes entities at parse time, matching the
  net effect of HAP text nodes + ``HtmlEntity.DeEntitize``; no extra unescape.
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass, field
from typing import Final, cast

from lxml.html import HtmlElement  # type: ignore[import-untyped]

from mostaql.text.normalization import normalize

__all__ = [
    "InferFieldsResult",
    "InferenceEngine",
    "InferredField",
]

# ---------------------------------------------------------------------------
# Tunable weights (hand-set, not learned) - C# InferenceEngine.cs L27-L36.
# ---------------------------------------------------------------------------
STEM_WEIGHT: Final[float] = 3.0
UNIT_WEIGHT: Final[float] = 2.0
TYPE_WEIGHT: Final[float] = 1.0
POSITION_WEIGHT: Final[float] = 0.5
MISSING_UNIT_PENALTY: Final[float] = -1.5
BOILERPLATE_DAMPING_THRESHOLD: Final[int] = 6
BOILERPLATE_DAMPING_FACTOR: Final[float] = 0.35

LOCAL_WINDOW_TOKENS: Final[int] = 12
LOCAL_CONFIDENCE_MARGIN: Final[float] = 0.20

STRATEGY_LOCAL: Final[str] = "local_inference"
STRATEGY_AMBIGUOUS: Final[str] = "global_inference_ambiguous"
STRATEGY_NO_CANDIDATES: Final[str] = "no_candidates_found"

# ---------------------------------------------------------------------------
# Arabic digit normalization + crude stemming - C# L41-L91.
# ---------------------------------------------------------------------------
_ARABIC_DIGITS: Final[str] = "٠١٢٣٤٥٦٧٨٩"
#: Trap §4.1-6: U+0660-U+0669 ONLY - Persian digits (U+06F0-U+06F9) stay untouched.
_ARABIC_TO_ASCII = str.maketrans(_ARABIC_DIGITS, "0123456789")

_PREFIXES: Final[tuple[str, ...]] = ("ال", "و", "ف", "ب", "ل", "لل")
_SUFFIXES: Final[tuple[str, ...]] = (
    "ها",  # noqa: RUF001
    "هم",
    "ه",  # noqa: RUF001
    "ة",
    "ات",
    "ين",
    "ون",
    "ي",
    "ا",  # noqa: RUF001
)
#: Stable descending-length orders (C# L45-L46 OrderByDescending is stable).
_PREFIXES_BY_LEN_DESC: Final[tuple[str, ...]] = tuple(sorted(_PREFIXES, key=len, reverse=True))
_SUFFIXES_BY_LEN_DESC: Final[tuple[str, ...]] = tuple(sorted(_SUFFIXES, key=len, reverse=True))

_DIACRITICS_RE: Final[re.Pattern[str]] = re.compile(r"[\u064B-\u065F\u0670\u0640]")
_STEM_TRIM_CHARS: Final[str] = "،.,:؛;()[]{}»«\"'"


def _to_ascii_arabic_digits(s: str) -> str:
    """Translate Arabic-Indic digits U+0660-U+0669 to ASCII; all else unchanged."""
    return s.translate(_ARABIC_TO_ASCII)


def _strip_affixes(word: str) -> str:
    """Crude Arabic prefix/suffix stripping (C# StripAffixes L49-L75).

    Prefixes strip REPEATEDLY (longest-first each iteration, remainder >= 2);
    suffixes strip EXACTLY ONCE (longest-first, remainder >= 2). Trap §4.1-18.
    """
    w = word
    changed = True
    while changed:
        changed = False
        for p in _PREFIXES_BY_LEN_DESC:
            if w.startswith(p) and len(w) - len(p) >= 2:
                w = w[len(p) :]
                changed = True
                break
    for sfx in _SUFFIXES_BY_LEN_DESC:
        if w.endswith(sfx) and len(w) - len(sfx) >= 2:
            w = w[: -len(sfx)]
            break
    return w


def _stem(word: str) -> str:
    """Strip diacritics/tatweel, punctuation, then affixes (C# Stem L85-L91)."""
    w = normalize(word)
    w = _DIACRITICS_RE.sub("", w)
    w = w.strip(_STEM_TRIM_CHARS)
    return _strip_affixes(w) if w else ""


# ---------------------------------------------------------------------------
# Value type patterns - C# L96-L109, copied VERBATIM.
# Verbatim ``\d`` is the CORRECT parity choice: .NET ``\d`` (without
# RegexOptions.ECMAScript) equals ``\p{Nd}`` and matches Unicode Nd digits,
# exactly like Python's ``\d`` on str patterns. The U+0660-vs-U+06F0
# divergence lives exclusively in ``_to_ascii_arabic_digits`` (trap §4.1-6),
# NOT in these patterns.
# ---------------------------------------------------------------------------
_PERCENT_RE: Final[re.Pattern[str]] = re.compile(r"(\d+(?:\.\d+)?)\s*%")
_RANGE_RE: Final[re.Pattern[str]] = re.compile(r"\$?\s*([\d.]+)\s*-\s*\$?\s*([\d.]+)")
_FLOAT_RE: Final[re.Pattern[str]] = re.compile(r"\b\d+\.\d+\b")
_INT_RE: Final[re.Pattern[str]] = re.compile(r"(?<!\.)\b\d+\b(?!\.\d)")
_ABSOLUTE_DATE_RE: Final[re.Pattern[str]] = re.compile(
    r"\b\d{4}[-/]\d{1,2}[-/]\d{1,2}\b|\b\d{1,2}[-/]\d{1,2}[-/]\d{4}\b"
)

_CURRENCY_SYMS: Final[tuple[str, ...]] = (
    "$",
    "usd",
    "دولار",
    "ريال",
    "sar",
    "egp",
    "جنيه",
)
_NOT_CALCULATED_MARKERS: Final[tuple[str, ...]] = (
    "لم يحسب بعد",
    "غير محدد",
    "n/a",
    "لا يوجد",
)

_DURATION_UNITS: Final[tuple[str, ...]] = (
    "يوم",
    "يوما",
    "أيام",
    "ايام",
    "ساعة",
    "ساعات",
    "أسبوع",
    "اسبوع",
    "أسابيع",
    "شهر",
    "أشهر",
)
_RELATIVE_DATE_WORDS: Final[tuple[str, ...]] = ("منذ", "قبل", "خلال")
_MONTH_NAMES: Final[tuple[str, ...]] = (
    "يناير",
    "فبراير",
    "مارس",
    "أبريل",
    "مايو",
    "يونيو",
    "يوليو",
    "أغسطس",
    "سبتمبر",
    "أكتوبر",
    "نوفمبر",
    "ديسمبر",
)


def _classify_value_types(token_text: str) -> set[str]:
    """Classify a raw token string into value types (C# ClassifyValueTypes L124-L161).

    Placeholder markers short-circuit to a PLACEHOLDER-only set before any
    numeric classification runs.
    """
    t = token_text.strip()
    types: set[str] = set()
    if not t:
        return types
    ascii_t = _to_ascii_arabic_digits(t)
    t_lower = t.lower()
    if any(marker in t_lower for marker in _NOT_CALCULATED_MARKERS):
        types.add("PLACEHOLDER")
        return types
    if _PERCENT_RE.search(ascii_t):
        types.add("PERCENT")
    if _RANGE_RE.search(ascii_t) and "-" in ascii_t:
        types.add("RANGE")
    if _ABSOLUTE_DATE_RE.search(ascii_t):
        types.add("DATE")
    if _FLOAT_RE.search(ascii_t):
        types.add("FLOAT")
        types.add("NUMBER")
    elif _INT_RE.search(ascii_t):
        types.add("NUMBER")
    return types


# ---------------------------------------------------------------------------
# Field profiles - C# FieldProfile/FieldProfiles L167-L282 (table verbatim;
# core stems precomputed with the same _stem used at scoring time).
# ---------------------------------------------------------------------------
@dataclass(frozen=True, slots=True)
class _FieldProfile:
    core_stems: tuple[str, ...]
    expected_types: frozenset[str]
    expected_types_weak: frozenset[str]
    unit_hints: tuple[str, ...]
    requires_unit: bool


_STARTED_SINCE_UNIT_HINTS: Final[tuple[str, ...]] = (
    *_RELATIVE_DATE_WORDS,
    *_DURATION_UNITS,
)


def _profile(
    *stems: str,
    expected: tuple[str, ...],
    weak: tuple[str, ...] = (),
    hints: tuple[str, ...] = (),
    requires_unit: bool = False,
) -> _FieldProfile:
    return _FieldProfile(
        core_stems=tuple(_stem(s) for s in stems),
        expected_types=frozenset(expected),
        expected_types_weak=frozenset(weak),
        unit_hints=hints,
        requires_unit=requires_unit,
    )


_FIELD_PROFILES: Final[dict[str, _FieldProfile]] = {
    "project_status": _profile(
        "حالة",
        "المشروع",
        expected=("TEXT",),
    ),
    "published_date": _profile(
        "نشر",
        "تاريخ",
        expected=("DATE",),
        weak=("NUMBER",),
        hints=_MONTH_NAMES,
    ),
    "budget": _profile(
        "ميزانية",
        "تكلفة",
        "سعر",
        expected=("NUMBER", "FLOAT", "RANGE", "PLACEHOLDER"),
        hints=_CURRENCY_SYMS,
        requires_unit=True,
    ),
    "duration": _profile(
        "مدة",
        "تنفيذ",
        "وقت",
        "لازم",
        expected=("NUMBER", "FLOAT"),
        hints=_DURATION_UNITS,
        requires_unit=True,
    ),
    "registration_date": _profile(
        "تسجيل",
        "تاريخ",
        expected=("DATE",),
        weak=("NUMBER",),
        hints=_MONTH_NAMES,
    ),
    "hire_rate": _profile(
        "معدل",
        "توظيف",
        expected=("PERCENT", "NUMBER"),
        hints=("%",),
        requires_unit=True,
    ),
    "open_projects_count": _profile(
        "مشاريع",
        "مفتوحة",
        expected=("NUMBER",),
    ),
    "in_progress_count": _profile(
        "مشاريع",
        "تنفيذ",
        expected=("NUMBER",),
    ),
    "completed_projects_count": _profile(
        "مشاريع",
        "منجزة",
        expected=("NUMBER",),
    ),
    "ongoing_conversations": _profile(
        "تواصلات",
        "جارية",
        expected=("NUMBER",),
    ),
    "started_since": _profile(
        "بدأ",
        "تنفيذه",
        "منذ",
        expected=("DATE", "NUMBER"),
        hints=_STARTED_SINCE_UNIT_HINTS,
    ),
    "deal_date": _profile(
        "تاريخ",
        "الصفقة",
        expected=("DATE",),
        weak=("NUMBER",),
        hints=_MONTH_NAMES,
    ),
    "delivery_date": _profile(
        "موعد",
        "التسليم",
        expected=("DATE",),
        weak=("NUMBER",),
        hints=_MONTH_NAMES,
    ),
    "proposal_count": _profile(
        "عدد",
        "المقترحات",
        "عروض",
        "مقترحات",
        expected=("NUMBER",),
        hints=("عرض", "عروض", "عرضان", "عرضين"),
    ),
}

# ---------------------------------------------------------------------------
# Step 1: flatten the DOM into an ordered text-token stream - C# L288-L374.
# ---------------------------------------------------------------------------
_NON_CONTENT_ELEMENTS: Final[frozenset[str]] = frozenset(
    {"script", "style", "noscript", "template", "textarea", "option", "select"}
)


@dataclass(slots=True)
class _Token:
    text: str
    index: int
    element: HtmlElement
    dom_path: list[HtmlElement]


def _tag_name(el: HtmlElement) -> str:
    tag = el.tag
    if isinstance(tag, str):
        return tag.rpartition("}")[2].lower()
    return ""


def _build_dom_path(el: HtmlElement) -> list[HtmlElement]:
    """Ancestor chain, self included (C# BuildDomPath L297-L307)."""
    path: list[HtmlElement] = []
    cur: HtmlElement | None = el
    while cur is not None:
        path.append(cur)
        cur = cur.getparent()
    return path


def _own_text(el: HtmlElement) -> str:
    """Concatenate the element's OWN leaf text nodes only (C# Flatten L333-L341).

    lxml stores parent-owned inter-node text as each direct child's tail, so
    ``el.text`` plus every direct child's ``tail`` reproduces HAP's direct
    text-node children exactly (trap §4.1-9: word-level own-text flattening).
    """
    parts: list[str] = [el.text or ""]
    parts.extend(child.tail or "" for child in el)
    return normalize("".join(parts))


def _flatten(root: HtmlElement) -> list[_Token]:
    """Walk the DOM in document order splitting each element's own text (C# L321-L357)."""
    tokens: list[_Token] = []
    idx = 0
    elements = cast("list[HtmlElement]", root.xpath(".//*"))
    for el in elements:
        if _tag_name(el) in _NON_CONTENT_ELEMENTS or any(
            _tag_name(a) in _NON_CONTENT_ELEMENTS for a in el.iterancestors()
        ):
            continue
        own = _own_text(el)
        if not own:
            continue
        for word in own.split(" "):
            if not word:
                continue
            tokens.append(_Token(text=word, index=idx, element=el, dom_path=_build_dom_path(el)))
            idx += 1
    return tokens


def _dom_distance(path_a: list[HtmlElement], path_b: list[HtmlElement]) -> int:
    """Cheap hop-count to shared ancestor, summed across both sides (C# L360-L374)."""
    set_b = set(path_b)
    hops_a = 0
    for node in path_a:
        if node in set_b:
            return hops_a + path_b.index(node)
        hops_a += 1
    return hops_a + len(path_b)


# ---------------------------------------------------------------------------
# Step 2: candidate extraction - C# L380-L513.
# ---------------------------------------------------------------------------
_MERGE_CONNECTOR_RE: Final[re.Pattern[str]] = re.compile(r"\A(?:[-.]|\$)\Z")
_MERGE_DIGIT_RE: Final[re.Pattern[str]] = re.compile(r"\A\d+%?\Z")
_VALUE_SEED_RE: Final[re.Pattern[str]] = re.compile(r"\d")
_UNIT_TRIM_CHARS: Final[str] = "،.,:؛;()[]{}»«\"'%$"


@dataclass(slots=True)
class _Candidate:
    raw_text: str
    types: set[str]
    token_index: int
    element: HtmlElement
    dom_path: list[HtmlElement]
    unit_nearby: str | None
    scores: dict[str, float] = field(default_factory=dict)
    probabilities: dict[str, float] = field(default_factory=dict)


def _find_adjacent_unit(tokens: list[_Token], idx: int, window: int = 3) -> str | None:
    """Scan +/-window tokens for a duration/%/currency unit (C# FindAdjacentUnit L493-L513)."""
    lo = max(0, idx - window)
    hi = min(len(tokens), idx + window + 1)
    for k in range(lo, hi):
        if k == idx:
            continue
        tok = tokens[k]
        t = tok.text.strip(_UNIT_TRIM_CHARS)
        t_lower = t.lower()
        if (
            any(u == t_lower for u in _DURATION_UNITS)
            or t == "%"
            or any(cs in tok.text.lower() for cs in _CURRENCY_SYMS)
        ):
            return tok.text
    return None


def _merge_window_end(tokens: list[_Token], i: int) -> int:
    """Greedy forward merge of up to 4 connector/digit-% tokens (C# L416-L430)."""
    j = i
    n = len(tokens)
    while j + 1 < n and (j - i) < 4:
        nxt = tokens[j + 1].text
        if _MERGE_CONNECTOR_RE.match(nxt) or _MERGE_DIGIT_RE.match(nxt):
            j += 1
        else:
            break
    return j


def _keep_longest_per_seed(group: list[_Candidate]) -> list[_Candidate]:
    """Suppress bare sub-candidates subsumed by a longer same-seed merge (C# L479-L489)."""
    if len(group) == 1:
        return group
    return [max(group, key=lambda c: len(c.raw_text))]


def _extract_candidates(tokens: list[_Token]) -> list[_Candidate]:
    """Digit-seeded extraction evaluating bare AND merged windows (C# L402-L490)."""
    raw_candidates: list[_Candidate] = []
    seen: set[tuple[str, int]] = set()
    n = len(tokens)
    i = 0
    while i < n:
        tok = tokens[i]
        if not _VALUE_SEED_RE.search(tok.text):
            i += 1
            continue
        j = _merge_window_end(tokens, i)
        merged = "".join(t.text for t in tokens[i : j + 1])
        for candidate_text, end_idx in ((tok.text, i), (merged, j)):
            types = _classify_value_types(candidate_text)
            if not types:
                continue
            key = (candidate_text, tok.index)
            if key in seen:
                continue
            seen.add(key)
            raw_candidates.append(
                _Candidate(
                    raw_text=candidate_text,
                    types=types,
                    token_index=tok.index,
                    element=tok.element,
                    dom_path=tok.dom_path,
                    unit_nearby=_find_adjacent_unit(tokens, end_idx),
                )
            )
        i = j + 1 if merged != tok.text else i + 1

    by_seed: dict[int, list[_Candidate]] = {}
    for cand in raw_candidates:
        by_seed.setdefault(cand.token_index, []).append(cand)
    final: list[_Candidate] = []
    for group in by_seed.values():
        final.extend(_keep_longest_per_seed(group))
    return final


# ---------------------------------------------------------------------------
# Steps 3+4: scoring - C# L518-L673.
# ---------------------------------------------------------------------------
def _page_wide_stem_counts(tokens: list[_Token]) -> dict[str, int]:
    counts: dict[str, int] = {}
    for tok in tokens:
        s = _stem(tok.text)
        if s:
            counts[s] = counts.get(s, 0) + 1
    return counts


def _local_window(
    tokens: list[_Token], center_idx: int, window: int = LOCAL_WINDOW_TOKENS
) -> list[_Token]:
    lo = max(0, center_idx - window)
    hi = min(len(tokens), center_idx + window + 1)
    return tokens[lo:hi]


def _stem_contribution(
    nearby: list[_Token],
    candidate: _Candidate,
    profile: _FieldProfile,
    stem_counts: dict[str, int],
) -> float | None:
    """Best single stem hit with damping/recency/distance decay (C# L549-L580)."""
    best: float | None = None
    for tok in nearby:
        s = _stem(tok.text)
        if not s or s not in profile.core_stems:
            continue
        token_dist = abs(tok.index - candidate.token_index)
        dom_dist = _dom_distance(tok.dom_path, candidate.dom_path)
        decayed = 1.0 / (1.0 + min(token_dist, dom_dist))
        weight = STEM_WEIGHT
        if stem_counts.get(s, 0) >= BOILERPLATE_DAMPING_THRESHOLD:
            weight *= BOILERPLATE_DAMPING_FACTOR
        if tok.index > candidate.token_index:
            weight *= 0.5
        contribution = weight * decayed
        if best is None or contribution > best:
            best = contribution
    return best


def _unit_hit(candidate: _Candidate, profile: _FieldProfile, nearby_text: str) -> bool:
    """Bidirectional-hint match on the adjacent unit, else joined window (C# L582-L595)."""
    if candidate.unit_nearby is not None:
        unit_lower = candidate.unit_nearby.lower()
        return any(
            unit_lower in hint.lower() or hint.lower() in unit_lower for hint in profile.unit_hints
        )
    return any(hint.lower() in nearby_text for hint in profile.unit_hints)


def _type_bonus(candidate: _Candidate, profile: _FieldProfile) -> float:
    """Expected/weak type overlap guarded against FLOAT mismatches (C# L605-L618).

    A FLOAT candidate can NEVER satisfy a profile lacking FLOAT: unrelated
    decimals (e.g. a "4.9" client rating) must not earn whole-number type credit.
    """
    float_mismatch = "FLOAT" in candidate.types and "FLOAT" not in profile.expected_types
    if float_mismatch:
        return 0.0
    if candidate.types & profile.expected_types:
        return TYPE_WEIGHT
    if candidate.types & profile.expected_types_weak:
        return TYPE_WEIGHT * 0.25
    return 0.0


def _score_candidate(
    candidate: _Candidate,
    tokens: list[_Token],
    stem_counts: dict[str, int],
) -> None:
    """Additive per-field score for one candidate (C# ScoreCandidate L540-L622)."""
    nearby = _local_window(tokens, candidate.token_index)
    nearby_text = " ".join(t.text for t in nearby).lower()
    for fname, prof in _FIELD_PROFILES.items():
        score = 0.0
        stem_best = _stem_contribution(nearby, candidate, prof, stem_counts)
        if stem_best is not None:
            score += stem_best
        if _unit_hit(candidate, prof, nearby_text):
            score += UNIT_WEIGHT
        elif prof.requires_unit:
            score += MISSING_UNIT_PENALTY
        score += _type_bonus(candidate, prof)
        candidate.scores[fname] = score


def _apply_position_prior(candidates: list[_Candidate]) -> None:
    """Boost candidates in a dense metadata cluster (C# L625-L639)."""
    for cand in candidates:
        cluster_size = sum(
            other is not cand and _dom_distance(other.dom_path, cand.dom_path) <= 3
            for other in candidates
        )
        if cluster_size < 2:
            continue
        for fname in list(cand.scores):
            cand.scores[fname] += POSITION_WEIGHT


def _softmax(score_map: dict[str, float]) -> dict[str, float]:
    """Max-subtracted softmax with zero-denominator guard (C# L642-L656)."""
    if not score_map:
        return {}
    m = max(score_map.values())
    exps = {key: math.exp(val - m) for key, val in score_map.items()}
    total = sum(exps.values())
    if total == 0:
        total = 1.0
    return {key: val / total for key, val in exps.items()}


def _score_all(
    candidates: list[_Candidate],
    tokens: list[_Token],
    stem_counts: dict[str, int],
) -> list[_Candidate]:
    for cand in candidates:
        _score_candidate(cand, tokens, stem_counts)
    _apply_position_prior(candidates)
    for cand in candidates:
        cand.probabilities.update(_softmax(cand.scores))
    return candidates


# ---------------------------------------------------------------------------
# Step 5: resolve one winner per field - C# ResolveFields L678-L708.
# ---------------------------------------------------------------------------
def _resolve_fields(candidates: list[_Candidate]) -> InferFieldsResult:
    per_field: dict[str, list[tuple[float, _Candidate]]] = {fname: [] for fname in _FIELD_PROFILES}
    for cand in candidates:
        for fname, prob in cand.probabilities.items():
            per_field[fname].append((prob, cand))

    fields: dict[str, InferredField] = {}
    for fname, scored in per_field.items():
        if not scored:
            fields[fname] = InferredField(None, 0.0, STRATEGY_NO_CANDIDATES)
            continue
        ranked = sorted(scored, key=lambda pc: pc[0], reverse=True)
        top_prob, top_cand = ranked[0]
        runner_prob = ranked[1][0] if len(ranked) > 1 else 0.0
        margin = top_prob - runner_prob
        strategy = STRATEGY_LOCAL if margin >= LOCAL_CONFIDENCE_MARGIN else STRATEGY_AMBIGUOUS
        unit = top_cand.unit_nearby
        value = top_cand.raw_text + (
            f" {unit}" if unit is not None and unit not in top_cand.raw_text else ""
        )
        fields[fname] = InferredField(value, round(top_prob, 3), strategy)
    return InferFieldsResult(fields=fields)


# ---------------------------------------------------------------------------
# Public entry point - C# InferFields L713-L720.
# ---------------------------------------------------------------------------
@dataclass(frozen=True, slots=True)
class InferredField:
    """Per-field inference resolution (C# FieldInferenceResult record L13)."""

    value: str | None
    confidence: float
    strategy: str


@dataclass(frozen=True, slots=True)
class InferFieldsResult:
    """All field resolutions keyed by field name (§8 frozen contract)."""

    fields: dict[str, InferredField]


class InferenceEngine:
    """Context-aware, structure-independent scoring engine facade."""

    @staticmethod
    def infer_fields(root: HtmlElement) -> InferFieldsResult:
        tokens = _flatten(root)
        stem_counts = _page_wide_stem_counts(tokens)
        candidates = _extract_candidates(tokens)
        candidates = _score_all(candidates, tokens, stem_counts)
        return _resolve_fields(candidates)

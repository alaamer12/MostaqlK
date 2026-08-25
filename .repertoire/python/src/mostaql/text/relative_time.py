"""Arabic relative-time number parser (C# ArabicRelativeTime.ParseRelativeNumber).

Parses the integer from strings like "منذ 3 دقائق", "منذ ساعتين", "منذ يوم",
"منذ لحظات". Matching pipeline mirrors C# exactly:
CleanHtml → ToAsciiDigits (explicit digit-run wins) → NormalizeLabel → word heuristics.
"""

import re
from collections.abc import Iterable

from mostaql.text.normalization import clean_html, normalize_label, to_ascii_digits

__all__ = ["parse_relative_number"]

_DIGIT_RE = re.compile(r"\d+")

#: C# int.TryParse bound: digit runs beyond Int32 fail parsing and fall through
#: to the Arabic word heuristics, exactly as in the .NET implementation.
_INT32_MAX = 2**31 - 1

_MOMENTS = "لحظات"
_DUALS = (
    "دقيقتان",
    "دقيقتين",
    "ساعتان",
    "ساعتين",
    "يومان",
    "يومين",
    "شهران",
    "شهرين",
    "سنتان",
    "سنتين",
    "اسبوعان",
    "اسبوعين",
)
_SINGULARS = (
    "دقيقه",
    "دقيقة",
    "ساعه",
    "ساعة",
    "يوم",
    "شهر",
    "سنه",
    "سنة",
    "عام",
    "اسبوع",
    "أسبوع",
)
_PLURALS = (
    "دقائق",
    "ساعات",
    "ايام",
    "أيام",
    "اشهر",
    "أشهر",
    "شهور",
    "سنوات",
    "اعوام",
    "أعوام",
    "اسابيع",
    "أسابيع",
)


def _contains_any(haystack: str, needles: Iterable[str]) -> bool:
    """True when any needle occurs as a substring of haystack."""
    return any(needle in haystack for needle in needles)


def _first_digit_run(s: str) -> int | None:
    """First ``\\d+`` run as an int, or None when absent/beyond Int32 (C# TryParse)."""
    match = _DIGIT_RE.search(s)
    if not match:
        return None
    parsed = int(match.group())
    return parsed if parsed <= _INT32_MAX else None


def parse_relative_number(text: str | None) -> int:
    """Parse an Arabic relative-time string into its integer count (default 0).

    Order of precedence: explicit digit run → "لحظات" → dual forms (2) →
    singular forms (1, unless a plural marker occurs) → 0.
    """
    if not text or not text.strip():
        return 0

    cleaned = clean_html(text)

    number = _first_digit_run(to_ascii_digits(cleaned))
    if number is not None:
        return number

    norm = normalize_label(cleaned)

    if _MOMENTS in norm:
        return 0

    if _contains_any(norm, _DUALS):
        return 2

    if _contains_any(norm, _SINGULARS) and not _contains_any(norm, _PLURALS):
        return 1

    return 0

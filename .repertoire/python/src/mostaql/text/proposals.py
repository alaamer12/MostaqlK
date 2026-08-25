"""Arabic proposal-count parser (C# ArabicProposalParser.Parse).

Handles singular/dual/plural wording ("عرض واحد", "عرضان", "3-10 عروض",
"11+ عرضاً") and the add-proposal call-to-action ("أضف أول عرض").
"""

import re

from mostaql.text.normalization import clean_html, normalize_label, to_ascii_digits

__all__ = ["parse_proposals"]

_DIGIT_RE = re.compile(r"\d+")

#: C# int.TryParse bound: digit runs beyond Int32 fall through, as in .NET.
_INT32_MAX = 2**31 - 1


def _word_count(normalized_text: str) -> int | None:
    """Non-numeric markers, mirroring C# check order exactly."""
    if "اضف اول عرض" in normalized_text:
        return 0
    if "عرض واحد" in normalized_text or normalized_text == "عرض":
        return 1
    if "عرضان" in normalized_text or "عرضين" in normalized_text:
        return 2
    return None


def _first_digit_run(s: str) -> int | None:
    """First ``\\d+`` run as an int, or None when absent/beyond Int32 (C# TryParse)."""
    match = _DIGIT_RE.search(s)
    if not match:
        return None
    parsed = int(match.group())
    return parsed if parsed <= _INT32_MAX else None


def parse_proposals(text: str | None) -> tuple[int, str]:
    """Parse a proposal-count string into ``(number, cleaned_text)``.

    The returned text is the HTML-cleaned input; ``""``/null input yields
    ``(0, "")``. Never returns negative numbers. Contains-"عرض"-without-digits
    conservatively yields 0, exactly like C#.
    """
    if not text or not text.strip():
        return (0, "")

    cleaned = clean_html(text)
    normalized_text = normalize_label(cleaned)

    word_count = _word_count(normalized_text)
    if word_count is not None:
        return (word_count, cleaned)

    number = _first_digit_run(to_ascii_digits(normalized_text))
    if number is not None:
        return (number, cleaned)

    # Conservative fallback: no markers, no digits — 0 whether or not "عرض"
    # occurs (C#'s final two branches both return 0).
    return (0, cleaned)

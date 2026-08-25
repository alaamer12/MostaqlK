"""Arabic-aware text ground utilities shared by parsers (plan §8 contracts).

Pure stdlib leaf layer; imports nothing internal except sibling ``mostaql.text``
modules (import-linter ``pure-leaves`` contract).
"""

from mostaql.text.normalization import (
    LABEL_TRIM_CHARS,
    clean_html,
    normalize,
    normalize_label,
    strip_diacritics,
    to_ascii_digits,
)
from mostaql.text.proposals import parse_proposals
from mostaql.text.relative_time import parse_relative_number

__all__ = [
    "LABEL_TRIM_CHARS",
    "clean_html",
    "normalize",
    "normalize_label",
    "parse_proposals",
    "parse_relative_number",
    "strip_diacritics",
    "to_ascii_digits",
]

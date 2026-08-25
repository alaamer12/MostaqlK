"""Whitespace, diacritic, digit, and HTML-cleaning normalization (C# StringNormalization).

Authoritative single ground for string normalization shared by parsers and Arabic
format helpers. Pure stdlib; no I/O.

Parity notes (plan §4.1):
- ``clean_html`` decodes entities BEFORE stripping tags, and the tag regex has no
  DOTALL flag — a tag whose ``>`` sits after a newline survives cleaning.
- Zero-width/bidi characters (U+200B/E/F ...) are never stripped anywhere.
"""

import html
import re

__all__ = [
    "LABEL_TRIM_CHARS",
    "clean_html",
    "normalize",
    "normalize_label",
    "strip_diacritics",
    "to_ascii_digits",
]

_ARABIC_DIACRITICS_RE = re.compile(r"[\u064B-\u065F\u0670\u0640]")
_WHITESPACE_RE = re.compile(r"\s+")
_HTML_TAGS_RE = re.compile(r"<.*?>")

#: Characters trimmed from both ends by :func:`normalize_label` (C# LabelTrimChars).
LABEL_TRIM_CHARS: tuple[str, ...] = (
    ":",
    "\uff1a",
    "\u061b",
    ";",
    ".",
    "\u060c",
    ",",
    "-",
    "\u2013",
    "\u2014",
    " ",
)

_DIGIT_TRANSLATION = str.maketrans(
    "".join(chr(cp) for cp in range(0x0660, 0x066A))
    + "".join(chr(cp) for cp in range(0x06F0, 0x06FA)),
    "0123456789" * 2,
)

_LABEL_FOLD_TRANSLATION = str.maketrans(
    {
        "أ": "ا",  # noqa: RUF001
        "إ": "ا",  # noqa: RUF001
        "آ": "ا",  # noqa: RUF001
        "ٱ": "ا",  # noqa: RUF001
        "ى": "ي",
        "ة": "ه",  # noqa: RUF001
        "ؤ": "و",
        "ئ": "ي",
    }
)


def normalize(s: str | None) -> str:
    """Collapse whitespace runs to a single space and trim; null-safe to ``""``."""
    if not s:
        return ""
    return _WHITESPACE_RE.sub(" ", s).strip()


def to_ascii_digits(s: str | None) -> str:
    """Convert Arabic-Indic (U+0660-U+0669) AND extended/Persian (U+06F0-U+06F9) digits
    to ASCII; every other character unchanged; null-safe to ``""``."""
    if not s:
        return ""
    return s.translate(_DIGIT_TRANSLATION)


def strip_diacritics(s: str | None) -> str:
    """Remove Arabic tashkeel (U+064B-U+065F), dagger alif (U+0670), and tatweel (U+0640)."""
    if not s:
        return ""
    return _ARABIC_DIACRITICS_RE.sub("", s)


def normalize_label(s: str | None) -> str:
    """Canonical form for comparing Arabic labels regardless of orthographic variation.

    normalize → strip diacritics → fold alef/ya/ta-marbuta/waw variants → trim
    label punctuation from both ends.
    """
    text = normalize(s)
    if not text:
        return ""
    text = strip_diacritics(text).translate(_LABEL_FOLD_TRANSLATION)
    return text.strip("".join(LABEL_TRIM_CHARS))


def clean_html(s: str | None) -> str:
    """De-entity, remove markup tags, then trim quotes/whitespace.

    Entity decoding happens first (so ``&lt;b&gt;`` becomes a real tag that is then
    removed); tags spanning newlines are NOT matched (no DOTALL) and survive.
    """
    if not s or not s.strip():
        return ""
    text = html.unescape(s).strip()
    text = _HTML_TAGS_RE.sub("", text)
    return text.strip("\"' \t\r\n")

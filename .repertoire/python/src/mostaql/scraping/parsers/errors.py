"""Raise-helpers over the shared parse-error factories in :mod:`mostaql.errors`.

The C# parsers throw ``ParseException`` exclusively through the internal
``ParseErrors`` factory; the Python port keeps that shape: factories live in
``mostaql.errors`` (stable codes PARSE-001/002/003) and this module provides
the single sanctioned *raising* entry points so no other file constructs a
``ParseException`` directly.
"""

from mostaql.errors import (
    ParseException,
    empty_html,
    missing_title,
    no_project_rows,
)

__all__ = [
    "raise_empty_html",
    "raise_missing_title",
    "raise_no_project_rows",
]


def raise_empty_html(parser_name: str) -> None:
    """PARSE-001 — abort parsing: empty or whitespace-only HTML."""
    raise ParseException(empty_html(parser_name))


def raise_missing_title(project_id: int) -> None:
    """PARSE-002 — abort detail parsing: title chain exhausted."""
    raise ParseException(missing_title(project_id))


def raise_no_project_rows() -> None:
    """PARSE-003 — abort listing parsing: zero project rows across all tiers."""
    raise ParseException(no_project_rows())

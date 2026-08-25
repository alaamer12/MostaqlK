"""``.NET "O"``-compatible timestamp helpers (plan §3.E storage trap 1).

C# persists ``DateTimeOffset.ToString("O")`` — seven fractional digits plus an
explicit ``+HH:MM`` offset (UTC emits ``+00:00``, never ``Z``). Python must emit
and re-read the exact same byte shape so TEXT ordering and SQLite ``date()``
normalization behave identically (storage traps 2 and 3).
"""

import re
from datetime import UTC, datetime, timedelta

__all__ = ["current_utc", "dotnet_o_format", "parse_dotnet_o"]

_FRACTION_RE = re.compile(r"\.(\d+)")


def dotnet_o_format(dt: datetime) -> str:
    """Format a tz-aware datetime as the .NET "O" round-trip shape.

    Produces ``yyyy-MM-ddTHH:mm:ss.fffffff+HH:MM`` — exactly seven fractional
    digits (``microsecond * 10``, zero-padded to 7) and a signed colon offset;
    UTC renders as ``+00:00`` mirroring ``DateTimeOffset.UtcNow.ToString("O")``.
    Raises :class:`ValueError` for naive input.
    """
    offset = dt.utcoffset()
    if dt.tzinfo is None or offset is None:
        raise ValueError(f"dotnet_o_format requires a tz-aware datetime, got naive: {dt!r}")
    fraction = f"{dt.microsecond * 10:07d}"
    return f"{dt.strftime('%Y-%m-%dT%H:%M:%S')}.{fraction}{_format_offset(offset)}"


def parse_dotnet_o(value: str) -> datetime:
    """Parse a .NET "O" timestamp into a tz-aware :class:`datetime`.

    Accepts six- or seven-digit fractions, trailing ``Z``, and ``+HH:MM`` /
    ``-HH:MM`` offsets; the parsed offset is preserved (never folded to UTC),
    matching C# ``DateTimeOffset.Parse`` semantics.
    """
    text = value.strip()
    if text.endswith(("Z", "z")):
        text = text[:-1] + "+00:00"
    match = _FRACTION_RE.search(text)
    if match is not None:
        digits = (match.group(1) + "000000")[:6]
        start, end = match.span(1)
        text = text[:start] + digits + text[end:]
    parsed = datetime.fromisoformat(text)
    return parsed


def current_utc() -> datetime:
    """Tz-aware UTC now (the store's only clock source)."""
    return datetime.now(UTC)


def _format_offset(offset: timedelta) -> str:
    total_minutes = offset.days * 24 * 60 + offset.seconds // 60
    sign = "-" if total_minutes < 0 else "+"
    total_minutes = abs(total_minutes)
    hours, minutes = divmod(total_minutes, 60)
    return f"{sign}{hours:02d}:{minutes:02d}"

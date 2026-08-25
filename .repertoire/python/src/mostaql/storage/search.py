"""FTS5 query building for projects_fts (C# FtsQueryService prefix logic).

Pure module: no I/O, no sqlite3 import — execution lives on the store. Each
space-separated term is quote-escaped (embedded ``"`` doubled), wrapped as
``"term"*`` (quoted prefix), and joined with spaces for an implicit AND;
whitespace-only input yields the empty string.
"""

__all__ = ["build_fts_query"]


def build_fts_query(query: str) -> str:
    """Transform ``'تصميم موقع'`` into ``'"تصميم"* "موقع"*'`` (C# verbatim chain)."""
    terms: list[str] = []
    for token in query.split(" "):
        trimmed = token.strip()
        if not trimmed:
            continue
        escaped = trimmed.replace('"', '""')
        terms.append(f'"{escaped}"*')
    return " ".join(terms)

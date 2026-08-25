"""Enrichment lifecycle state (C# Models/EnrichmentStatus).

String values match the SQLite ``enrichment_status`` column values written by
the C# app ("Pending"/"Enriched"; "Failed" is never persisted today).
"""

from enum import StrEnum

__all__ = ["EnrichmentStatus"]


class EnrichmentStatus(StrEnum):
    """Lifecycle state of a discovered project's enrichment."""

    PENDING = "Pending"
    ENRICHED = "Enriched"
    FAILED = "Failed"

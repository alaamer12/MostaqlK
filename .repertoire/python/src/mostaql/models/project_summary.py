"""Listing-feed project model (C# Models/ProjectSummary).

Field defaults are load-bearing (plan §4.1 trap 22): ``is_unread=True``,
``enrichment_status=PENDING``, empty-string-not-null for text fields.
"""

from dataclasses import dataclass, field
from datetime import datetime

from mostaql.models.enrichment_status import EnrichmentStatus

__all__ = ["ProjectSummary"]


@dataclass(frozen=True, slots=True)
class ProjectSummary:
    """Lightweight representation of a listing-feed project before enrichment.

    ``discovered_at`` is required and must be tz-aware UTC (listing parser stamps
    UtcNow per card); ``enriched_at`` stays null until enrichment completes.
    """

    project_id: int
    title: str
    url: str = ""
    client_name: str = ""
    publish_time_number: int = 0
    publish_time_text: str = ""
    proposal_count: int = 0
    proposal_count_text: str = ""
    description: str = ""
    budget: str | None = None
    delivery_days: int | None = None
    skills_text: str = ""
    project_status: str | None = None
    is_unread: bool = True
    enrichment_status: EnrichmentStatus = EnrichmentStatus.PENDING
    discovered_at: datetime = field(kw_only=True)
    enriched_at: datetime | None = field(default=None, kw_only=True)

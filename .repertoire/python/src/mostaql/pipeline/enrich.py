"""Enrichment: one rate-limiter token, then one detail fetch (plan §8 contract).

Port of C# ``Services/Pipeline/EnrichmentService.cs`` minus the dropped asset-download
path (plan §12 ledger 6): a single attempt per call -- ``token -> fetch_project_details``.
"""

from typing import Protocol

from mostaql.models import ProjectDetails
from mostaql.pipeline.ratelimit import TokenBucketRateLimiter

__all__ = ["DetailFetcher", "EnrichmentService"]


class DetailFetcher(Protocol):
    """Structural scraper seam (C# ``IProjectScraper.FetchProjectDetailsAsync``).

    Defined locally so the pipeline never hard-couples to the HTTP layer;
    :class:`mostaql.scraping.scraper.MostaqlScraper` satisfies it structurally.
    """

    async def fetch_project_details(self, project_id: int) -> ProjectDetails:
        """Fetch and parse one project's detail page."""
        ...


class EnrichmentService:
    """Fetches full project details under the shared outbound request budget.

    C# origin: ``Services/Pipeline/EnrichmentService.cs``. One attempt per call; retrying
    is the worker's job (retry ladder in ``worker.py``).
    """

    def __init__(self, limiter: TokenBucketRateLimiter, scraper: DetailFetcher) -> None:
        self._limiter = limiter
        self._scraper = scraper

    async def enrich(self, project_id: int) -> ProjectDetails:
        """Consume one limiter token, then fetch the project's details.

        Typed network/parse errors from the scraper propagate unchanged to the caller.
        """
        await self._limiter.wait_for_token()
        return await self._scraper.fetch_project_details(project_id)

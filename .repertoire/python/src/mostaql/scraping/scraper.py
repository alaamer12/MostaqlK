"""MostaqlScraper equivalent: URL building, fetch orchestration, post-parse URL fill.

Parity notes (C# ``MostaqlScraper.FetchListingAsync`` / ``FetchProjectDetailsAsync``):

- Query params normalize exactly like C#: trimmed; leading ``?`` added when
  missing; ``None``/whitespace-only leave the bare listing URL.
- ``ParseException`` propagates AS-IS: with typed exceptions the parse failure is
  its own carrier, where C# wrapped it into an ``HttpErrors.ParseFailed`` Result
  (plan §12.1 ledger); the PARSE-* code stays observable on ``exc.error.code``.
- ``details.url`` is filled AFTER parsing via :func:`dataclasses.replace` --
  C# assigns ``details.Url`` post-parse (plan §4.1 trap 2); the Python models are
  frozen dataclasses, so replacement is the sanctioned equivalent of that mutation.
"""

from dataclasses import replace

from mostaql.http import PageFetcher
from mostaql.models import ProjectDetails, ProjectSummary
from mostaql.scraping.parsers.detail import DetailParser
from mostaql.scraping.parsers.listing import ListingParser

__all__ = ["MostaqlScraper"]


class MostaqlScraper:
    """Raw access to the Mostaql website: listing feed and project detail pages."""

    LISTING_URL = "https://mostaql.com/projects"
    DETAIL_URL_FORMAT = "https://mostaql.com/project/{0}"

    def __init__(self, fetcher: PageFetcher) -> None:
        self._fetcher = fetcher

    async def fetch_listing(self, query_params: str | None = None) -> list[ProjectSummary]:
        """Fetch the listing feed and parse it into summaries.

        Raises typed network errors from the fetcher; ``ParseException`` from the
        parser propagates unchanged.
        """
        url = self._listing_url(query_params)
        html = await self._fetcher.get_html(url)
        return ListingParser.parse(html)

    async def fetch_project_details(self, project_id: int) -> ProjectDetails:
        """Fetch one project's detail page, parse it, then stamp the canonical URL.

        Raises typed network errors from the fetcher; ``ParseException`` from the
        parser propagates unchanged.
        """
        url = self.DETAIL_URL_FORMAT.format(project_id)
        html = await self._fetcher.get_html(url)
        details = DetailParser.parse(project_id, html)
        return replace(details, url=url)

    def _listing_url(self, query_params: str | None) -> str:
        if query_params is None or not query_params.strip():
            return self.LISTING_URL
        normalized = query_params.strip()
        if not normalized.startswith("?"):
            normalized = f"?{normalized}"
        return f"{self.LISTING_URL}{normalized}"

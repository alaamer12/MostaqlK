"""EnrichmentService tests: token-before-fetch ordering and passthrough (C# spec)."""

from datetime import UTC, datetime

from mostaql.models import Owner, ProjectDetails
from mostaql.pipeline.enrich import EnrichmentService
from mostaql.pipeline.ratelimit import TokenBucketRateLimiter


class SpyLimiter:
    def __init__(self) -> None:
        self.calls = 0

    async def wait_for_token(self) -> None:
        self.calls += 1


class SpyScraper:
    def __init__(self, details: ProjectDetails) -> None:
        self.details = details
        self.calls: list[int] = []

    async def fetch_project_details(self, project_id: int) -> ProjectDetails:
        self.calls.append(project_id)
        return self.details


def details(pid: int) -> ProjectDetails:
    return ProjectDetails(
        project_id=pid,
        title=f"project {pid}",
        owner=Owner(name="owner"),
        discovered_at=datetime(2026, 1, 1, tzinfo=UTC),
    )


async def test_token_consumed_before_detail_fetch() -> None:
    events: list[str] = []

    class OrderLimiter:
        async def wait_for_token(self) -> None:
            events.append("token")

    class OrderScraper:
        async def fetch_project_details(self, project_id: int) -> ProjectDetails:
            events.append("fetch")
            return details(project_id)

    service = EnrichmentService(OrderLimiter(), OrderScraper())  # type: ignore[arg-type]
    await service.enrich(7)

    assert events == ["token", "fetch"]


async def test_scraper_result_passed_through_unchanged() -> None:
    limiter = SpyLimiter()
    scraper = SpyScraper(details(9))
    service = EnrichmentService(limiter, scraper)  # type: ignore[arg-type]

    result = await service.enrich(9)

    assert result is scraper.details
    assert limiter.calls == 1
    assert scraper.calls == [9]


async def test_every_enrich_call_consumes_exactly_one_token() -> None:
    limiter = SpyLimiter()
    scraper = SpyScraper(details(1))
    service = EnrichmentService(limiter, scraper)  # type: ignore[arg-type]

    for pid in (1, 2, 3):
        await service.enrich(pid)

    assert limiter.calls == 3
    assert len(scraper.calls) == 3


async def test_real_limiter_satisfies_service_seam() -> None:
    """Structural check: the concrete TokenBucketRateLimiter plugs in unchanged."""
    limiter = TokenBucketRateLimiter(requests_per_minute=600, safe_requests=False)
    scraper = SpyScraper(details(5))
    service = EnrichmentService(limiter, scraper)  # type: ignore[arg-type]

    assert (await service.enrich(5)).title == "project 5"

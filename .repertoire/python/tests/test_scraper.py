"""MostaqlScraper orchestration tests: URL building, post-parse URL fill, error passthrough.

Dual-mode by design: while the Wave 7 parser stubs lack ``ListingParser`` /
``DetailParser``, documented-API doubles are injected so these tests exercise the
orchestration immediately; once the real parsers land the injection becomes a
no-op and the SAME fixtures flow through the REAL parsers unchanged.
"""

import importlib
import sys
import types
from collections.abc import Callable
from datetime import UTC, datetime

import httpx
import pytest

from mostaql.errors import ParseException, missing_title, no_project_rows
from mostaql.models import ProjectDetails, ProjectSummary

_DISCOVERED = datetime(2026, 8, 25, 12, 0, 0, tzinfo=UTC)

LISTING_HTML = """
<html>
  <body>
    <table>
      <tr class="project-row">
        <td>
          <h2><a href="/project/123">بناء تطبيق جوال</a></h2>
          <p class="project__brief"><a href="/project/123">مطلوب مطور لتطبيق جوال</a></p>
          <ul class="project__meta">
            <li><i class="hsoub-file-signature-icon"></i> 3 عروض</li>
            <li>منذ ساعتين</li>
            <li>شركة نمو</li>
          </ul>
        </td>
      </tr>
      <tr class="project-row">
        <td>
          <h2><a href="/project/456">تصميم موقع تعريفي</a></h2>
          <ul class="project__meta">
            <li>منذ يوم واحد</li>
          </ul>
        </td>
      </tr>
    </table>
  </body>
</html>
"""

DETAIL_HTML = """
<html>
  <head><title>مشروع تجريبي - مستقل</title></head>
  <body>
    <h1>مشروع تجريبي</h1>
    <div id="projectDetailsTab">
      <div class="text-wrapper-div">
        <p>وصف المشروع التجريبي.</p>
      </div>
    </div>
  </body>
</html>
"""


def _summary(project_id: int, url: str, title: str) -> ProjectSummary:
    return ProjectSummary(project_id=project_id, title=title, url=url, discovered_at=_DISCOVERED)


def _details(project_id: int) -> ProjectDetails:
    return ProjectDetails(
        project_id=project_id,
        title="مشروع تجريبي",
        url="",
        description="وصف المشروع التجريبي.",
        discovered_at=_DISCOVERED,
        enriched_at=_DISCOVERED,
    )


def _module_has(module_name: str, attr: str) -> bool:
    try:
        module = importlib.import_module(module_name)
    except ImportError:
        return False
    return hasattr(module, attr)


def _install_parser_shims() -> None:
    """Self-disabling stand-ins matching the frozen §8 parser signatures."""
    if not _module_has("mostaql.scraping.parsers.listing", "ListingParser"):
        module = types.ModuleType("mostaql.scraping.parsers.listing")

        class _ListingParserShim:
            @staticmethod
            def parse(html: str) -> list[ProjectSummary]:
                return [
                    _summary(123, "/project/123", "بناء تطبيق جوال"),
                    _summary(456, "/project/456", "تصميم موقع تعريفي"),
                ]

        module.ListingParser = _ListingParserShim  # type: ignore[attr-defined]
        sys.modules["mostaql.scraping.parsers.listing"] = module

    if not _module_has("mostaql.scraping.parsers.detail", "DetailParser"):
        module = types.ModuleType("mostaql.scraping.parsers.detail")

        class _DetailParserShim:
            @staticmethod
            def parse(project_id: int, html: str) -> ProjectDetails:
                return _details(project_id)

        module.DetailParser = _DetailParserShim  # type: ignore[attr-defined]
        sys.modules["mostaql.scraping.parsers.detail"] = module


_install_parser_shims()


def _scraper_module():
    return importlib.import_module("mostaql.scraping.scraper")


@pytest.fixture
async def make_scraper() -> Callable[[dict[str, str], list[str]], object]:
    clients: list[httpx.AsyncClient] = []

    def _make(pages: dict[str, str], seen: list[str]):
        def handler(request: httpx.Request) -> httpx.Response:
            seen.append(str(request.url))
            body = pages.get(str(request.url))
            if body is None:
                return httpx.Response(404, text="not found")
            return httpx.Response(200, text=body)

        from mostaql.http import PageFetcher

        client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
        clients.append(client)
        scraper_cls = _scraper_module().MostaqlScraper
        return scraper_cls(PageFetcher(client))

    yield _make
    for client in clients:
        await client.aclose()


def test_urls_match_csharp_constants():
    scraper_cls = _scraper_module().MostaqlScraper
    assert scraper_cls.LISTING_URL == "https://mostaql.com/projects"
    assert scraper_cls.DETAIL_URL_FORMAT == "https://mostaql.com/project/{0}"


async def test_fetch_listing_returns_parsed_summaries(make_scraper):
    seen: list[str] = []
    scraper = make_scraper({"https://mostaql.com/projects": LISTING_HTML}, seen)

    summaries = await scraper.fetch_listing()

    assert seen == ["https://mostaql.com/projects"]
    assert [s.project_id for s in summaries] == [123, 456]
    assert [s.title for s in summaries] == ["بناء تطبيق جوال", "تصميم موقع تعريفي"]
    assert [s.url for s in summaries] == ["/project/123", "/project/456"]


@pytest.mark.parametrize(
    ("query_params", "expected_url"),
    [
        ("category=dev", "https://mostaql.com/projects?category=dev"),
        ("?category=dev", "https://mostaql.com/projects?category=dev"),
        ("  ?x=1  ", "https://mostaql.com/projects?x=1"),
        ("", "https://mostaql.com/projects"),
        ("   ", "https://mostaql.com/projects"),
        (None, "https://mostaql.com/projects"),
    ],
)
async def test_fetch_listing_query_param_normalization(query_params, expected_url, make_scraper):
    seen: list[str] = []
    scraper = make_scraper({expected_url: LISTING_HTML}, seen)

    summaries = await scraper.fetch_listing(query_params)

    assert seen == [expected_url]
    assert len(summaries) == 2


async def test_fetch_project_details_fills_canonical_url_after_parse(make_scraper):
    seen: list[str] = []
    scraper = make_scraper({"https://mostaql.com/project/123": DETAIL_HTML}, seen)

    details = await scraper.fetch_project_details(123)

    assert seen == ["https://mostaql.com/project/123"]
    assert details.project_id == 123
    assert details.url == "https://mostaql.com/project/123"
    assert details.title == "مشروع تجريبي"
    assert details.description == "وصف المشروع التجريبي."


async def test_fetch_listing_lets_parse_exception_propagate(monkeypatch, make_scraper):
    received: list[str] = []

    class _RaisingListingParser:
        @staticmethod
        def parse(html: str) -> list[ProjectSummary]:
            received.append(html)
            raise ParseException(no_project_rows())

    monkeypatch.setattr(_scraper_module(), "ListingParser", _RaisingListingParser)
    seen: list[str] = []
    scraper = make_scraper({"https://mostaql.com/projects": LISTING_HTML}, seen)

    with pytest.raises(ParseException) as excinfo:
        await scraper.fetch_listing(None)

    assert excinfo.value.error.code == "PARSE-003"
    assert received == [LISTING_HTML]


async def test_fetch_project_details_lets_parse_exception_propagate(monkeypatch, make_scraper):
    received_ids: list[int] = []

    class _RaisingDetailParser:
        @staticmethod
        def parse(project_id: int, html: str) -> ProjectDetails:
            received_ids.append(project_id)
            raise ParseException(missing_title(project_id))

    monkeypatch.setattr(_scraper_module(), "DetailParser", _RaisingDetailParser)
    seen: list[str] = []
    scraper = make_scraper({"https://mostaql.com/project/77": DETAIL_HTML}, seen)

    with pytest.raises(ParseException) as excinfo:
        await scraper.fetch_project_details(77)

    assert excinfo.value.error.code == "PARSE-002"
    assert received_ids == [77]
    assert seen == ["https://mostaql.com/project/77"]

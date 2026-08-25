"""ListingParser: three-tier card discovery into ProjectSummary models.

Port of ``Infrastructure/Http/Parsers/ListingParser.cs`` (behavioral parity).

ENTITY RULE (parity decision, plan §4.1 trap 9): HtmlAgilityPack keeps HTML
entities encoded in node text, so C# wraps every text read in
``Normalize(HtmlEntity.DeEntitize(node.InnerText))``. lxml.html already decodes
entities at parse time, and its ``text_content()`` concatenates adjacent text
nodes without separators exactly like HAP's InnerText fusing. Every C# text
read therefore maps to plain ``normalize(node.text_content())`` here — NO extra
``html.unescape`` (double-decoding would corrupt payloads containing literal
``&amp;amp;``-style text).

Class-matching styles are deliberately mixed, as in C# (trap 10): the tier
selectors and the meta list use the exact-token ``concat`` trick, while the
brief paragraph uses a raw whole-attribute substring match.
"""

import re
from dataclasses import dataclass
from datetime import UTC, datetime

from lxml import etree  # type: ignore[import-untyped]
from lxml.html import HtmlElement, document_fromstring  # type: ignore[import-untyped]

from mostaql.models.project_summary import ProjectSummary
from mostaql.scraping.parsers.errors import raise_empty_html, raise_no_project_rows
from mostaql.text.normalization import normalize
from mostaql.text.proposals import parse_proposals
from mostaql.text.relative_time import parse_relative_number

__all__ = ["ListingParser"]

# ListingParser.cs:25 - exact-token class match on tr.project-row.
_TIER1_ROWS_XPATH = "//tr[contains(concat(' ', normalize-space(@class), ' '), ' project-row ')]"
# ListingParser.cs:43 - exact-token class match on div.project-item.
_TIER2_ITEMS_XPATH = "//div[contains(concat(' ', normalize-space(@class), ' '), ' project-item ')]"
# ListingParser.cs:64 - raw href substring sweep.
_TIER3_LINKS_XPATH = "//a[contains(@href, '/project/')]"
# ListingParser.cs:95 - h2 anchor first, any anchor fallback.
_TITLE_LINK_XPATHS = (".//h2/a", ".//a")
# ListingParser.cs:109 - exact-token class match on ul.project__meta.
_META_LIST_XPATH = ".//ul[contains(concat(' ', normalize-space(@class), ' '), ' project__meta ')]"
# ListingParser.cs:118-119 - SUBSTRING contains (NOT the token trick), inner a else the p.
_BRIEF_LINK_XPATH = ".//p[contains(@class, 'project__brief')]/a"
_BRIEF_FALLBACK_XPATH = ".//p[contains(@class, 'project__brief')]"
# ListingParser.cs:142-143 - icon classes via attribute substring.
_PROPOSAL_ICON_XPATHS = (
    ".//span[contains(@class, 'hsoub-file-signature-icon')]",
    ".//i[contains(@class, 'fa-users')]",
)
# ListingParser.cs:180 / cs:151 - content keywords, checked in this priority order.
_PROPOSAL_WORDS = ("عرض", "عروض")
_TIME_MARKERS = ("منذ", "ساعة", "يوم", "لحظات")

# ListingParser.cs:182-183 - FIRST numeric run after "/project/", never the last digit run.
_PROJECT_ID_RE = re.compile(r"/project/(\d+)")


def _first(nodes: list[HtmlElement]) -> HtmlElement | None:
    """SelectSingleNode equivalent: first node or None."""
    return nodes[0] if nodes else None


def _extract_project_id_from_url(url: str) -> int:
    """C# ExtractProjectIdFromUrl (cs:192-208).

    The id is the FIRST numeric segment after ``/project/`` - not the last
    numeric run in the URL, so a slug like ``-canva-2024`` cannot hijack it.
    Fallback for URLs without the prefix: trim trailing slashes, split on ``/``
    AND ``-``, take the first entirely-numeric segment, else 0.
    """
    if not url:
        return 0

    match = _PROJECT_ID_RE.search(url)
    if match:
        return int(match.group(1))

    trimmed = url.rstrip("/")
    for segment in trimmed.split("/"):
        for part in segment.split("-"):
            if part.isdigit():
                return int(part)
    return 0


@dataclass(slots=True)
class _RowFields:
    """Mutable accumulator for the meta-list classification (cs:111-115)."""

    client_name: str = ""
    proposal_count: int = 0
    proposal_count_text: str = ""
    publish_time_number: int = 0
    publish_time_text: str = ""

    def absorb(self, li: HtmlElement) -> None:
        """Classify one direct-child <li> content-based, priority order (cs:133-160)."""
        text = normalize(li.text_content())
        if not text:
            # cs:136-139: empty-text skip runs BEFORE the icon check - an icon-only
            # <li> with no text is silently discarded.
            return

        has_proposal_icon = any(li.xpath(xpath) for xpath in _PROPOSAL_ICON_XPATHS)
        if has_proposal_icon or any(word in text for word in _PROPOSAL_WORDS):
            number, original_text = parse_proposals(text)  # cs:145-150
            self.proposal_count = number
            self.proposal_count_text = original_text
        elif any(marker in text for marker in _TIME_MARKERS):
            # EXPECTED QUIRK (preserved from C#): a client name that merely contains
            # a time word ("يوم" inside "عميل اليوم") lands in the time bucket.
            self.publish_time_number = parse_relative_number(text)  # cs:151-155
            self.publish_time_text = text
        elif self.client_name == "":
            self.client_name = text  # cs:156-159: later candidates discarded.


def _parse_row(row: HtmlElement) -> ProjectSummary | None:
    """C# ParseRow (cs:92-177): one card -> summary, or None to SKIP silently."""
    title_link: HtmlElement | None = None
    for xpath in _TITLE_LINK_XPATHS:
        title_link = _first(row.xpath(xpath))  # cs:95
        if title_link is not None:
            break
    if title_link is None:
        return None

    url: str = title_link.get("href") or ""  # raw href, no absolutization (cs:102)

    fields = _RowFields()
    meta = _first(row.xpath(_META_LIST_XPATH))
    if meta is not None:
        for li in meta.xpath("./li"):  # DIRECT children only (cs:127)
            fields.absorb(li)

    brief = _first(row.xpath(_BRIEF_LINK_XPATH))
    if brief is None:
        brief = _first(row.xpath(_BRIEF_FALLBACK_XPATH))  # cs:118-119
    description = normalize(brief.text_content()) if brief is not None else ""

    return ProjectSummary(
        project_id=_extract_project_id_from_url(url),
        title=normalize(title_link.text_content()),  # cs:101
        url=url,
        client_name=fields.client_name,
        publish_time_number=fields.publish_time_number,
        publish_time_text=fields.publish_time_text,
        proposal_count=fields.proposal_count,
        proposal_count_text=fields.proposal_count_text,
        description=description,
        discovered_at=datetime.now(UTC),  # fresh per card (cs:175)
    )


def _parse_card_tiers(root: HtmlElement) -> list[ProjectSummary]:
    """C# tiers 1-2 (cs:25-56): exact-token tr.project-row, else div.project-item."""
    cards = root.xpath(_TIER1_ROWS_XPATH)
    if not cards:
        cards = root.xpath(_TIER2_ITEMS_XPATH)
    return [summary for card in cards if (summary := _parse_row(card)) is not None]


def _sweep_anchor_links(root: HtmlElement) -> list[ProjectSummary]:
    """C# tier 3 (cs:58-81): anchor sweep deduped by id KEEPING FIRST (trap 14)."""
    summaries: list[ProjectSummary] = []
    seen: dict[int, None] = {}  # ordered set preserving first-occurrence order
    for link in root.xpath(_TIER3_LINKS_XPATH):
        url: str = link.get("href") or ""
        project_id = _extract_project_id_from_url(url)
        title = normalize(link.text_content())
        if project_id <= 0 or not title or project_id in seen:  # cs:68-71
            continue
        seen[project_id] = None
        summaries.append(
            ProjectSummary(
                project_id=project_id,
                title=title,
                url=url,
                discovered_at=datetime.now(UTC),  # fresh per anchor (cs:78)
            )
        )
    return summaries


class ListingParser:
    """Parses the projects listing page HTML into ProjectSummary records."""

    @staticmethod
    def parse(html: str) -> list[ProjectSummary]:
        """Three-tier cascade: tr.project-row -> div.project-item -> anchor sweep."""
        if not html or not html.strip():  # cs:16-19
            raise_empty_html("ListingParser")

        try:
            root: HtmlElement | None = document_fromstring(html)
        except etree.ParserError:
            # HAP's LoadHtml never throws on junk input; it just yields a node-less
            # DOM that falls through every tier. Mirror that outcome.
            root = None

        summaries = _parse_card_tiers(root) if root is not None else []
        if not summaries and root is not None:  # gate is zero SUMMARIES (cs:58)
            summaries = _sweep_anchor_links(root)

        if not summaries:  # cs:83-87
            raise_no_project_rows()

        return summaries

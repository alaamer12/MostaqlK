"""ListingParser unit tests (Wave B4).

Fixtures live under tests/regression/fixtures/listing/. Assertions pin the C#
behaviors called out in refactor-python-plan.md §3.C.1 and §4.1 traps 1/5/9/
10/14/22 - including deliberate quirks marked EXPECTED QUIRK below.
"""

import time
from itertools import pairwise
from pathlib import Path

import pytest

from mostaql.errors import ParseException
from mostaql.models.enrichment_status import EnrichmentStatus
from mostaql.scraping.parsers.listing import ListingParser

FIXTURES_DIR = Path(__file__).parent / "regression" / "fixtures" / "listing"


def load_fixture(name: str) -> str:
    return (FIXTURES_DIR / name).read_text(encoding="utf-8")


def row_card_html(project_id: int) -> str:
    return (
        "<html><body><table>"
        "<tr class='project-row'><td><h2>"
        f"<a href='/project/{project_id}-t'>عنوان {project_id}</a></h2></td></tr>"
        "</table></body></html>"
    )


def div_card_html(project_id: int) -> str:
    return (
        "<html><body>"
        f"<div class='project-item'><h2><a href='/project/{project_id}-d'>عنوان {project_id}</a>"
        "</h2></div></body></html>"
    )


def anchor_html(project_id: int) -> str:
    return f"<p><a href='/project/{project_id}-a'>عنوان {project_id}</a></p>"


class TestTableRows:
    def test_full_field_mapping_per_card(self) -> None:
        summaries = ListingParser.parse(load_fixture("table_rows.html"))

        assert len(summaries) == 3

        a = summaries[0]
        assert a.project_id == 1001
        assert a.title == "مشروع تصميم شعار"
        assert a.url == "/project/1001-logo-design"
        assert a.client_name == "مؤسسة النور"
        assert a.publish_time_number == 3
        assert a.publish_time_text == "منذ 3 ساعات"
        assert a.proposal_count == 7
        assert a.proposal_count_text == "7 عروض"
        assert a.description == "نبحث عن مصمم شعار محترف"

        # Card B: meta order [proposals(text), posted(dual), client]; brief without link.
        b = summaries[1]
        assert b.project_id == 2002
        assert b.client_name == "شركة الأفق"
        assert b.proposal_count == 1
        assert b.proposal_count_text == "عرض واحد"
        assert b.publish_time_number == 2
        assert b.publish_time_text == "منذ يومين"
        assert b.description == "وصف مباشر بدون رابط"

        # Card C: span icon signal, entity+newline brief normalization, trailing slash URL.
        c = summaries[2]
        assert c.project_id == 3003
        assert c.url == "/project/3003-website/"
        assert c.client_name == ""
        assert c.proposal_count == 12
        assert c.proposal_count_text == "12 عرض"
        assert c.publish_time_number == 0
        assert c.publish_time_text == "منذ لحظات"
        assert c.description == "تصميم متجر & مدونة"

    def test_model_defaults_are_load_bearing(self) -> None:
        """Plan §4.1 trap 22: listing-time defaults must never drift."""
        summaries = ListingParser.parse(load_fixture("table_rows.html"))

        for summary in summaries:
            assert summary.budget is None
            assert summary.delivery_days is None
            assert summary.skills_text == ""
            assert summary.project_status is None
            assert summary.is_unread is True
            assert summary.enrichment_status is EnrichmentStatus.PENDING
            assert summary.enriched_at is None


class TestTierCascade:
    def test_tier_two_div_cards(self) -> None:
        summaries = ListingParser.parse(load_fixture("div_cards.html"))

        assert [s.project_id for s in summaries] == [4004, 5005]
        # The loose anchor after the divs must NOT leak in (tier 2 short-circuits tier 3).
        assert all(s.url != "/project/8888-loose" for s in summaries)

        first = summaries[0]
        assert first.client_name == "مؤسسة الرياض"
        assert first.proposal_count == 5
        assert first.proposal_count_text == "5 عروض"
        assert first.publish_time_number == 2
        assert first.publish_time_text == "منذ ساعتين"

        # Minimal div card: every meta field stays at its default.
        minimal = summaries[1]
        assert minimal.client_name == ""
        assert minimal.proposal_count == 0
        assert minimal.proposal_count_text == ""
        assert minimal.publish_time_number == 0
        assert minimal.publish_time_text == ""

    def test_link_sweep_dedupe_first_and_slug_id(self) -> None:
        summaries = ListingParser.parse(load_fixture("link_sweep.html"))

        pairs = [(s.project_id, s.title, s.url) for s in summaries]
        assert pairs == [
            (12345, "تصميم كانفا", "/project/12345-canva-2024"),
            (77777, "مشروع الفا", "/project/77777-alpha"),
        ]
        # Trap 14: dedupe keeps FIRST occurrence (77777-beta dropped).
        # Traps 1/5 flavor: slug year "2024" must not hijack the id.

    def test_table_beats_div_beats_anchors(self) -> None:
        html = (
            "<html><body>"
            + row_card_html(11)
            + div_card_html(22)
            + anchor_html(33)
            + "</body></html>"
        )
        summaries = ListingParser.parse(html)
        assert [s.project_id for s in summaries] == [11]

    def test_div_beats_anchors_when_no_table(self) -> None:
        html = "<html><body>" + div_card_html(44) + anchor_html(55) + "</body></html>"
        summaries = ListingParser.parse(html)
        assert [s.project_id for s in summaries] == [44]

    def test_zero_summary_tier_falls_through_to_anchor_sweep(self) -> None:
        """The tier-3 gate is ZERO SUMMARIES, not an empty node set (cs:58)."""
        html = (
            "<html><body><table>"
            "<tr class='project-row'><td>بدون رابط</td></tr>"
            "</table>" + anchor_html(66) + "</body></html>"
        )
        summaries = ListingParser.parse(html)
        assert [s.project_id for s in summaries] == [66]


class TestGarbageTolerance:
    def test_anchorless_row_skipped_and_footer_anchor_ignored(self) -> None:
        summaries = ListingParser.parse(load_fixture("mixed_and_garbage.html"))

        # The anchorless tr.project-row was skipped silently; because tier 1 still
        # produced one summary, the footer anchor must be ignored entirely.
        assert len(summaries) == 1
        good = summaries[0]
        assert good.project_id == 6006
        assert good.title == "مشروع متعدد الأسطر"
        assert good.client_name == "عميل جيد"
        assert good.publish_time_number == 5
        assert good.publish_time_text == "منذ 5 أيام"
        assert good.proposal_count == 61
        assert good.proposal_count_text == "٦١ عرض"
        assert good.description == "وصف قصير"


class TestClassificationQuirks:
    def test_client_named_with_time_word_misrouted(self) -> None:
        """EXPECTED QUIRK (cs:151): a client whose name merely CONTAINS a time word
        lands in the time bucket; the parser stays C#-faithful on purpose."""
        html = (
            "<html><body><table><tr class='project-row'><td>"
            "<h2><a href='/project/7007-q'>عنوان</a></h2>"
            "<ul class='project__meta'>"
            "<li>عميل اليوم</li>"
            "<li>فريق التطوير</li>"
            "</ul></td></tr></table></body></html>"
        )

        summary = ListingParser.parse(html)[0]

        assert summary.client_name == "فريق التطوير"
        assert summary.publish_time_text == "عميل اليوم"
        assert summary.publish_time_number == 1  # singular يوم -> 1
        assert summary.proposal_count == 0
        assert summary.proposal_count_text == ""

    def test_icon_only_li_without_text_is_skipped(self) -> None:
        """cs:136-139: the empty-text skip runs BEFORE the icon check."""
        html = (
            "<html><body><table><tr class='project-row'><td>"
            "<h2><a href='/project/8008-i'>عنوان</a></h2>"
            "<ul class='project__meta'>"
            "<li><i class='fa-users'></i></li>"
            "<li>منذ ساعة</li>"
            "</ul></td></tr></table></body></html>"
        )

        summary = ListingParser.parse(html)[0]

        assert summary.proposal_count == 0
        assert summary.proposal_count_text == ""
        assert summary.publish_time_text == "منذ ساعة"
        assert summary.publish_time_number == 1

    def test_brief_substring_match_vs_meta_token_match(self) -> None:
        """Trap 10: brief uses raw SUBSTRING contains; meta list uses the exact-token
        concat trick - lookalike classes diverge deliberately."""
        html = (
            "<html><body><table><tr class='project-row'><td>"
            "<h2><a href='/project/9009-b'>عنوان</a></h2>"
            "<ul class='xproject__metay'><li>زائر</li></ul>"
            "<p class='weird-project__brief-suffix'>نص مختصر</p>"
            "</td></tr></table></body></html>"
        )

        summary = ListingParser.parse(html)[0]

        assert summary.description == "نص مختصر"
        assert summary.client_name == ""


class TestErrorContracts:
    def test_blank_html_raises_parse_001(self) -> None:
        with pytest.raises(ParseException) as excinfo:
            ListingParser.parse(load_fixture("blank.html"))
        assert excinfo.value.error.code == "PARSE-001"

    def test_body_without_rows_raises_parse_003(self) -> None:
        with pytest.raises(ParseException) as excinfo:
            ListingParser.parse(load_fixture("empty_body.html"))
        assert excinfo.value.error.code == "PARSE-003"


class TestDiscoveredAtFreshness:
    def test_stamped_per_card_and_monotonic_across_calls(self) -> None:
        first_run = ListingParser.parse(load_fixture("table_rows.html"))
        stamps = [s.discovered_at for s in first_run]
        assert all(a <= b for a, b in pairwise(stamps))

        time.sleep(0.05)
        second_run = ListingParser.parse(load_fixture("table_rows.html"))
        assert second_run[0].discovered_at > stamps[-1]

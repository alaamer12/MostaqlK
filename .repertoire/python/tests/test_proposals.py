"""Tests for mostaql.text.proposals (C# ArabicProposalParser.Parse parity)."""

import pytest

from mostaql.text import parse_proposals


class TestEmptyInput:
    def test_none_yields_zero_empty(self):
        assert parse_proposals(None) == (0, "")

    def test_blank_yields_zero_empty(self):
        assert parse_proposals("   ") == (0, "")


class TestAddProposalMarker:
    def test_add_first_proposal_marker_is_zero(self):
        number, text = parse_proposals("أضف أول عرض")
        assert number == 0
        assert text == "أضف أول عرض"

    def test_marker_matched_after_folding(self):
        assert parse_proposals("<b>إضف أول عرض</b>")[0] == 0


class TestSingularAndDual:
    def test_bare_view_word_is_one(self):
        assert parse_proposals("عرض") == (1, "عرض")

    def test_view_wahid_bigram_is_one(self):
        assert parse_proposals("عرض واحد")[0] == 1

    @pytest.mark.parametrize("text", ["عرضان", "عرضين", "لديك عرضان جديدتان"])
    def test_dual_forms_are_two(self, text):
        assert parse_proposals(text)[0] == 2


class TestNumeric:
    @pytest.mark.parametrize(
        ("text", "expected"),
        [
            ("61 عرض", 61),
            ("3-10 عروض", 3),
            ("11+ عرضاً", 11),
            ("5 عروض", 5),
            ("١٢ عرض", 12),
            ("<i>7</i> عروض", 7),
        ],
    )
    def test_digit_run_parsed(self, text, expected):
        assert parse_proposals(text)[0] == expected

    def test_range_floors_to_first_number(self):
        assert parse_proposals("3-10 عروض") == (3, "3-10 عروض")

    def test_int32_overflow_treated_as_no_digits(self):
        # C# int.TryParse fails on overflow → falls through to conservative 0.
        assert parse_proposals("99999999999 عرض")[0] == 0


class TestConservativeFallback:
    @pytest.mark.parametrize("text", ["بعض العروض الأخرى", "قائمة العرض", "لا عروض"])
    def test_view_without_digits_or_markers_is_zero(self, text):
        number, cleaned = parse_proposals(text)
        assert number == 0
        assert cleaned == text


class TestReturnedTextIsCleaned:
    def test_tags_removed_from_returned_text(self):
        assert parse_proposals("<b>عرض</b>") == (1, "عرض")

    def test_entities_decoded_in_returned_text(self):
        number, text = parse_proposals("&quot;عرض&quot;")
        assert text == "عرض"
        assert number == 1

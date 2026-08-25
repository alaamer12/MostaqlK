"""Tests for mostaql.text.relative_time (C# ArabicRelativeTime.ParseRelativeNumber parity)."""

import pytest

from mostaql.text import parse_relative_number

DUALS = (
    "دقيقتان",
    "دقيقتين",
    "ساعتان",
    "ساعتين",
    "يومان",
    "يومين",
    "شهران",
    "شهرين",
    "سنتان",
    "سنتين",
    "اسبوعان",
    "اسبوعين",
)

SINGULARS = (
    "دقيقه",
    "دقيقة",
    "ساعه",
    "ساعة",
    "يوم",
    "شهر",
    "سنه",
    "سنة",
    "عام",
    "اسبوع",
    "أسبوع",
)

PLURAL_ONLY = (
    "منذ دقائق",
    "منذ ساعات",
    "منذ ايام",
    "منذ أيام",
    "منذ اشهر",
    "منذ أشهر",
    "منذ شهور",
    "منذ سنوات",
    "منذ اعوام",
    "منذ أعوام",
    "منذ اسابيع",
    "منذ أسابيع",
)


class TestDigitPrecedence:
    @pytest.mark.parametrize(
        ("text", "expected"),
        [
            ("منذ 7 دقائق", 7),
            ("منذ ١٥ يوما", 15),  # noqa: RUF001
            ("منذ ۳ ساعات", 3),
            ("2024/05/01", 2024),
            ("<b>12</b> دقيقة", 12),
            ("0 عرض", 0),
        ],
    )
    def test_explicit_digit_run_wins(self, text, expected):
        assert parse_relative_number(text) == expected

    def test_first_digit_run_taken(self):
        assert parse_relative_number("5 من 10 أيام") == 5

    def test_digit_run_beats_plural_markers(self):
        assert parse_relative_number("منذ 9 أيام") == 9

    def test_int32_overflow_falls_through_to_words(self):
        # Mirrors C# int.TryParse failure: huge digit run is ignored, words decide.
        assert parse_relative_number("99999999999 يوم") == 1
        assert parse_relative_number("99999999999 لحظات") == 0


class TestMoments:
    @pytest.mark.parametrize("text", ["منذ لحظات", "لحظات", "منذ لحظات معدودة"])
    def test_moments_zero(self, text):
        assert parse_relative_number(text) == 0


class TestDuals:
    @pytest.mark.parametrize("dual", DUALS)
    def test_dual_is_two(self, dual):
        assert parse_relative_number(f"منذ {dual}") == 2

    def test_dual_inside_sentence(self):
        assert parse_relative_number("تحديث قبل ساعتين تقريبا") == 2


class TestSingulars:
    @pytest.mark.parametrize("singular", SINGULARS)
    def test_singular_is_one(self, singular):
        assert parse_relative_number(f"منذ {singular}") == 1

    def test_diacritic_form_still_matches_after_label_folding(self):
        assert parse_relative_number("منذ دَقيقةٍ") == 1


class TestPluralOverride:
    @pytest.mark.parametrize("text", PLURAL_ONLY)
    def test_plural_marker_beats_singular_match(self, text):
        assert parse_relative_number(text) == 0

    def test_singular_word_with_plural_word_yields_zero(self):
        assert parse_relative_number("يوم من الأيام") == 0


class TestDefaults:
    @pytest.mark.parametrize("text", [None, "", "   ", "hello world", "قبل قليل", "xyz"])
    def test_unknown_is_zero(self, text):
        assert parse_relative_number(text) == 0

    def test_html_cleaned_before_matching(self):
        assert parse_relative_number("<span>منذ يوم</span>") == 1

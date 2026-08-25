"""Tests for mostaql.text.normalization (C# StringNormalization parity, plan §4.1 traps)."""

import pytest

from mostaql.text import (
    LABEL_TRIM_CHARS,
    clean_html,
    normalize,
    normalize_label,
    strip_diacritics,
    to_ascii_digits,
)


class TestNormalize:
    def test_none_returns_empty(self):
        assert normalize(None) == ""

    def test_empty_string_returns_empty(self):
        assert normalize("") == ""

    def test_collapses_whitespace_runs(self):
        assert normalize("  hello \t world\n\nfoo  ") == "hello world foo"

    def test_newlines_and_tabs_collapse_to_single_space(self):
        assert normalize("a\r\nb\tc") == "a b c"

    def test_already_clean_unchanged(self):
        assert normalize("clean text") == "clean text"

    def test_zero_width_chars_survive_normalize(self):
        result = normalize("\u200bA\u200eB\u200f")
        assert result == "\u200bA\u200eB\u200f"


class TestToAsciiDigits:
    def test_none_returns_empty(self):
        assert to_ascii_digits(None) == ""

    def test_arabic_indic_digits_converted(self):
        assert to_ascii_digits("٠١٢٣٤٥٦٧٨٩") == "0123456789"

    def test_persian_digits_converted(self):
        assert to_ascii_digits("۰۱۲۳۴۵۶۷۸۹") == "0123456789"

    def test_single_persian_digit(self):
        assert to_ascii_digits("۴") == "4"

    def test_mixed_text_preserved(self):
        assert to_ascii_digits("منذ ٧ أيام (2024)") == "منذ 7 أيام (2024)"  # noqa: RUF001

    def test_ascii_digits_unchanged(self):
        assert to_ascii_digits("abc 123") == "abc 123"


class TestStripDiacritics:
    def test_none_returns_empty(self):
        assert strip_diacritics(None) == ""

    def test_removes_tashkeel_range(self):
        assert strip_diacritics("عَرَضٌ") == "عرض"

    def test_removes_tatweel(self):
        assert strip_diacritics("مــد") == "مد"

    def test_removes_dagger_alif(self):
        assert strip_diacritics("هٰذا") == "هذا"

    def test_letters_outside_range_survive(self):
        assert strip_diacritics("عرض") == "عرض"


class TestNormalizeLabel:
    def test_none_returns_empty(self):
        assert normalize_label(None) == ""

    def test_whitespace_only_returns_empty(self):
        assert normalize_label("   ") == ""

    def test_folds_alef_variants(self):
        for variant in ("أ", "إ", "آ", "ٱ"):
            assert normalize_label(variant) == "ا"  # noqa: RUF001

    def test_folds_ya_variants(self):
        assert normalize_label("ى") == "ي"
        assert normalize_label("ئ") == "ي"

    def test_folds_ta_marbuta_to_ha(self):
        assert normalize_label("ة") == "ه"  # noqa: RUF001

    def test_folds_waw_hamza(self):
        assert normalize_label("ؤ") == "و"

    def test_combined_word_folding(self):
        assert normalize_label("أحمد إبراهيم") == "احمد ابراهيم"

    def test_strips_diacritics_before_folding(self):
        assert normalize_label("عَرضاً") == "عرضا"

    def test_trims_label_punctuation_both_ends(self):
        assert normalize_label(": الاسم؛ ") == "الاسم"

    def test_trims_fullwidth_colon_and_arabic_comma(self):
        assert normalize_label("\uff1aالاسم،") == "الاسم"  # noqa: RUF001

    def test_trims_all_trim_chars(self):
        for char in LABEL_TRIM_CHARS:
            assert normalize_label(f"{char}الاسم{char}") == "الاسم"

    def test_inner_punctuation_preserved(self):
        assert normalize_label("عدد: العروض") == "عدد: العروض"

    def test_zero_width_chars_survive(self):
        result = normalize_label("\u200bالاسم\u200e")  # noqa: RUF001
        assert "\u200b" in result
        assert "\u200e" in result


class TestCleanHtml:
    def test_none_returns_empty(self):
        assert clean_html(None) == ""

    def test_whitespace_only_returns_empty(self):
        assert clean_html("   \n\t ") == ""

    def test_decodes_entities_before_tag_strip(self):
        # "&lt;b&gt;" becomes a real tag after decoding, then is removed.
        assert clean_html("&lt;b&gt;hello&lt;/b&gt;") == "hello"

    def test_removes_simple_tags(self):
        assert clean_html("<p>text</p>") == "text"

    def test_newline_spanning_tag_survives(self):
        # Trap §4.1-7: regex has no DOTALL, so '>' after a newline is never matched.
        raw = "<div\nclass='x'>hi"
        assert clean_html(raw) == raw

    def test_tag_closed_on_next_line_removed_when_tag_itself_is_single_line(self):
        # Only tags whose OWN text spans a newline survive; "</span>" here is
        # entirely on line 2, so it still matches.
        assert clean_html("<span>x\n</span>y") == "x\ny"

    def test_same_line_tags_removed_even_with_later_newlines(self):
        raw = '<b>x</b>\n<div\nclass="y">'
        assert clean_html(raw) == 'x\n<div\nclass="y">'

    def test_trims_quotes_and_whitespace(self):
        assert clean_html("  \"  quoted  ' ") == "quoted"

    def test_trims_tab_cr_lf(self):
        assert clean_html("\t'x'\r\n") == "x"

    def test_plain_text_passthrough(self):
        assert clean_html("just text") == "just text"


@pytest.mark.parametrize(
    ("raw", "expected"),
    [
        ("&amp;", "&"),
        ("a &quot;b&quot; c", 'a "b" c'),
        ("<i>italic</i>", "italic"),
        ('"<b>bold</b>"', "bold"),
    ],
)
def test_clean_html_matrix(raw, expected):
    assert clean_html(raw) == expected

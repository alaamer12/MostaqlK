"""Unit tests for FTS5 query building (C# FtsQueryService prefix logic)."""

import pytest

from mostaql.storage.search import build_fts_query


def test_single_arabic_term_gets_quoted_prefix() -> None:
    assert build_fts_query("تصمي") == '"تصمي"*'


def test_multi_term_implicit_and() -> None:
    assert build_fts_query("تصميم موقع") == '"تصميم"* "موقع"*'


def test_embedded_quotes_are_doubled() -> None:
    assert build_fts_query('ab"c') == '"ab""c"*'


def test_empty_and_whitespace_yield_empty_string() -> None:
    assert build_fts_query("") == ""
    assert build_fts_query("   ") == ""


def test_extra_spaces_collapse_via_split_trim_filter() -> None:
    assert build_fts_query("a  b ") == '"a"* "b"*'


def test_mixed_script_terms() -> None:
    assert build_fts_query("php laravel") == '"php"* "laravel"*'


@pytest.mark.parametrize("query", ["", " ", "\t"])
def test_whitespace_only_variants(query: str) -> None:
    assert build_fts_query(query) == ""

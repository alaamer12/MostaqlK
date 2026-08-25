"""Tests for mostaql.scraping.parsers.inference (C# InferenceEngine parity).

Self-contained synthetic HTML only - no dependency on sibling parser modules.
Locks plan §3.D behavior and §4.1 traps 6 (digit-converter divergence), 18
(affix floors), 20 (softmax rounding + 0.20 margin flip).
"""

import math

from lxml.html import document_fromstring  # type: ignore[import-untyped]

from mostaql.scraping.parsers.inference import (
    InferenceEngine,
    _classify_value_types,
    _extract_candidates,
    _flatten,
    _page_wide_stem_counts,
    _score_all,
    _stem,
    _to_ascii_arabic_digits,
)

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _doc(html: str):
    return document_fromstring(html)


def _infer(html: str):
    return InferenceEngine.infer_fields(_doc(html))


# ---------------------------------------------------------------------------
# Flatten (checklist 1)
# ---------------------------------------------------------------------------


class TestFlatten:
    def test_skips_excluded_tags_including_nested(self):
        html = """
        <div>
          <script>var x = "ignore 123";</script>
          <style>.a { color: red }</style>
          <noscript>nada 99</noscript>
          <template><div><p>tpl 7</p></div></template>
          <textarea>ta 8</textarea>
          <select><option>opt 9</option></select>
          <p>visible 11</p>
          <div><section><script>deep 12</script><b>kept 13</b></section></div>
        </div>
        """
        tokens = _flatten(_doc(html))
        texts = [t.text for t in tokens]
        assert "visible" in texts and "11" in texts
        assert "kept" in texts and "13" in texts
        for banned in ("123", "nada", "99", "tpl", "ta", "opt", "deep"):
            assert not any(banned in t for t in texts)
        assert [t.index for t in tokens] == list(range(len(tokens)))

    def test_own_text_only_and_document_order(self):
        # HAP semantics: parent's own text nodes ("Hello ", "!") tokenize BEFORE
        # the nested <b>'s text, proving only direct leaf text is taken.
        tokens = _flatten(_doc("<div>Hello <b>World</b>!</div>"))
        assert [(t.text, t.element.tag) for t in tokens] == [
            ("Hello", "div"),
            ("!", "div"),
            ("World", "b"),
        ]
        assert [t.index for t in tokens] == [0, 1, 2]

    def test_whitespace_collapsed_within_element(self):
        tokens = _flatten(_doc("<p>a\n\t b   c</p>"))
        assert [t.text for t in tokens] == ["a", "b", "c"]

    def test_indices_sequential_across_elements(self):
        tokens = _flatten(_doc("<p>x 1</p><span>y 2</span>"))
        assert [t.index for t in tokens] == list(range(len(tokens)))


# ---------------------------------------------------------------------------
# Stemming (checklist 2, trap §4.1-18)
# ---------------------------------------------------------------------------


class TestStem:
    def test_prefix_loop_strips_repeatedly_longest_first(self):
        assert _stem("والكتب") == "كتب"

    def test_single_suffix_strip_only(self):
        # "ها" removed once; no further suffix pass on the result.  # noqa: RUF003
        assert _stem("كتابها") == "كتاب"

    def test_suffix_remainder_floor_two(self):
        # Stripping "ا" would leave 1 char -> not stripped (locked C# behavior).  # noqa: RUF003
        assert _stem("يوم") == "يوم"
        assert _stem("بلا") == "لا"

    def test_prefix_remainder_floor_two(self):
        # "ال" + single char is never stripped as a prefix; the trailing "ا"  # noqa: RUF003
        # suffix IS stripped once (remainder "ال" keeps 2 chars). C#-verified.
        assert _stem("الا") == "ال"

    def test_diacritics_tatweel_and_punctuation(self):
        assert _stem("مـــدَّة") == "مد"
        assert _stem("(الميزانية)،") == "ميزاني"

    def test_definite_article_equivalence_for_profiles(self):
        assert _stem("المشروع") == _stem("مشروع")


# ---------------------------------------------------------------------------
# Type classifier (checklist 5, trap §4.1-6)
# ---------------------------------------------------------------------------


class TestClassifyValueTypes:
    def test_percent(self):
        assert _classify_value_types("50%") == {"PERCENT", "NUMBER"}

    def test_range(self):
        assert _classify_value_types("100-200") == {"RANGE", "NUMBER"}

    def test_absolute_date_both_orders(self):
        assert "DATE" in _classify_value_types("2024/05/01")
        assert "DATE" in _classify_value_types("01-05-2024")

    def test_float_adds_number(self):
        assert _classify_value_types("4.9") == {"FLOAT", "NUMBER"}

    def test_int(self):
        assert _classify_value_types("42") == {"NUMBER"}

    def test_arabic_indic_digits_convert(self):
        assert _classify_value_types("٤٢") == {"NUMBER"}

    def test_persian_digits_classify_while_converter_untouched(self):
        # .NET \d (non-ECMAScript) and Python \d BOTH match Unicode Nd, so ۴۲
        # classifies as NUMBER even though the PRIVATE CONVERTER (trap
        # §4.1-6) leaves Persian U+06F0-F9 untranslated.
        assert _to_ascii_arabic_digits("۴۲") == "۴۲"
        assert _classify_value_types("۴۲") == {"NUMBER"}

    def test_placeholder_marker_short_circuits(self):
        assert _classify_value_types("لم يحسب بعد") == {"PLACEHOLDER"}
        assert _classify_value_types("غير محدد") == {"PLACEHOLDER"}
        assert _classify_value_types("N/A") == {"PLACEHOLDER"}

    def test_placeholder_wins_over_numbers(self):
        assert _classify_value_types("n/a 5") == {"PLACEHOLDER"}

    def test_empty_is_empty(self):
        assert _classify_value_types("   ") == set()


# ---------------------------------------------------------------------------
# Candidate extraction (checklist 4)
# ---------------------------------------------------------------------------


class TestExtractCandidates:
    def test_merge_then_advance_past_merged_window(self):
        # Greedy merge absorbs consecutive digit tokens ("10-2030" would happen
        # without the advance); a non-digit word stops the window so "30"
        # becomes its own seed, proving i jumped past the merged span.
        tokens = _flatten(_doc("<p>10 - 20</p><p>x 30</p>"))
        cands = _extract_candidates(tokens)
        assert [c.raw_text for c in cands] == ["10-20", "30"]
        assert all(c.unit_nearby is None for c in cands)

    def test_consecutive_digit_tokens_all_absorbed_into_merge(self):
        tokens = _flatten(_doc("<p>10 - 20 30</p>"))
        cands = _extract_candidates(tokens)
        assert [c.raw_text for c in cands] == ["10-2030"]

    def test_bare_subsumed_by_longest_merge_per_seed(self):
        tokens = _flatten(_doc("<p>250 - 500 دولار</p>"))
        cands = _extract_candidates(tokens)
        assert len(cands) == 1
        assert cands[0].raw_text == "250-500"
        assert cands[0].unit_nearby == "دولار"

    def test_currency_unit_detected_on_untrimmed_token(self):
        # C# checks currency hints against the UNTRIMMED token text; duration/%
        # equality uses the TRIMMED text.
        cands = _extract_candidates(_flatten(_doc("<p>ريال 100</p>")))
        assert [c.raw_text for c in cands] == ["100"]
        assert cands[0].unit_nearby == "ريال"

    def test_bare_percent_sign_not_a_unit_after_trim(self):
        # "%" trimmed by UnitTrimChars becomes empty -> not equal to "%":
        # faithful C# quirk; attached "50%" is classified instead.
        cands = _extract_candidates(_flatten(_doc("<p>50 %</p>")))
        assert [c.raw_text for c in cands] == ["50"]
        assert cands[0].unit_nearby is None


# ---------------------------------------------------------------------------
# Scoring sanity via public API (checklist 6/7/8/9)
# ---------------------------------------------------------------------------


class TestScoringSanity:
    def test_budget_range_with_currency_wins_budget(self):
        result = _infer(
            "<html><body><div>"
            "<span>الميزانية</span><span>250 - 500 دولار</span>"
            "</div></body></html>"
        )
        budget = result.fields["budget"]
        assert budget.value == "250-500 دولار"
        assert budget.strategy == "local_inference"
        best_field = max(result.fields.items(), key=lambda kv: kv[1].confidence)
        assert best_field[0] == "budget"

    def test_duration_label_beats_other_fields(self):
        # "المدة" stems to duration's core stem without overlapping
        # started_since's "تنفيذ" stem, so duration must clearly win.
        result = _infer(
            "<html><body><div><span>المدة</span><span>20 يوما</span></div></body></html>"
        )
        duration = result.fields["duration"]
        assert duration.value == "20 يوما"
        assert duration.strategy == "local_inference"
        best_field = max(result.fields.items(), key=lambda kv: kv[1].confidence)
        assert best_field[0] == "duration"

    def test_float_guard_keeps_rating_from_count_profile(self):
        # "4.9" is FLOAT; open_projects_count expects whole NUMBER only, so the
        # guard blocks type credit and the genuine integer wins the field.
        html = (
            "<html><body><div>"
            "<p>المشاريع المفتوحة</p><p>5</p>"
            "<p>التقييم العام</p><p>4.9</p>"
            "</div></body></html>"
        )
        assert _infer(html).fields["open_projects_count"].value == "5"
        root = _doc(html)
        tokens = _flatten(root)
        scored = {
            c.raw_text: c
            for c in _score_all(_extract_candidates(tokens), tokens, _page_wide_stem_counts(tokens))
        }
        guard = "open_projects_count"
        # FLOAT candidate earns no type credit on a count-only profile:
        assert scored["4.9"].scores[guard] < scored["5"].scores[guard]


# ---------------------------------------------------------------------------
# Boilerplate damping (checklist 3)
# ---------------------------------------------------------------------------


class TestDamping:
    def _budget_candidate_score(self, html: str) -> float:
        root = _doc(html)
        tokens = _flatten(root)
        counts = _page_wide_stem_counts(tokens)
        cands = _extract_candidates(tokens)
        scored = _score_all(cands, tokens, counts)
        target = next(c for c in scored if c.raw_text.startswith("50"))
        return target.scores["budget"]

    def test_repeated_stem_dampens_contribution(self):
        unique = self._budget_candidate_score(
            "<html><body><p>نص عابر تماما</p><p>الميزانية 50 دولار</p></body></html>"
        )
        damped = self._budget_candidate_score(
            "<html><body>"
            "<p>الميزانية</p><p>الميزانية</p><p>الميزانية</p>"
            "<p>الميزانية</p><p>الميزانية</p>"
            "<p>الميزانية 50 دولار</p>"
            "</body></html>"
        )
        assert damped < unique


# ---------------------------------------------------------------------------
# Margin strategy + confidence + empty pages (checklist 9)
# ---------------------------------------------------------------------------


class TestResolveFields:
    def test_margin_flip_to_ambiguous_on_near_tie(self):
        # Two symmetric budget blocks score identically -> margin 0 < 0.20.
        result = _infer(
            "<html><body>"
            "<div><span>الميزانية</span><span>100 دولار</span></div>"
            "<div><span>الميزانية</span><span>200 دولار</span></div>"
            "</body></html>"
        )
        budget = result.fields["budget"]
        assert budget.strategy == "global_inference_ambiguous"

    def test_confident_page_reports_local_inference(self):
        result = _infer("<p>الميزانية 250 - 500 دولار</p>")
        assert result.fields["budget"].strategy == "local_inference"

    def test_confidence_rounded_to_three_decimals(self):
        result = _infer("<p>الميزانية 250 - 500 دولار</p>")
        conf = result.fields["budget"].confidence
        assert conf == round(conf, 3)
        assert 0.0 <= conf <= 1.0

    def test_empty_body_no_candidates_found(self):
        result = _infer("<html><body><p>لا شيء هنا</p></body></html>")
        assert len(result.fields) == 14
        for f in result.fields.values():
            assert f.value is None
            assert f.confidence == 0.0
            assert f.strategy == "no_candidates_found"

    def test_all_fourteen_fields_present(self):
        result = _infer("<p>الميزانية 300 دولار</p>")
        assert set(result.fields) == {
            "project_status",
            "published_date",
            "budget",
            "duration",
            "registration_date",
            "hire_rate",
            "open_projects_count",
            "in_progress_count",
            "completed_projects_count",
            "ongoing_conversations",
            "started_since",
            "deal_date",
            "delivery_date",
            "proposal_count",
        }


class TestParityTraps:
    def test_persian_digits_seed_and_resolve_engine_level(self):
        # \d is Unicode Nd in both .NET (non-ECMAScript) and Python: ۴۲ seeds
        # a candidate, resolves budget, and the resolved value keeps the
        # Persian characters (mirrors C# RawText behavior).
        result = _infer("<p>الميزانية ۴۲ دولار</p>")
        budget = result.fields["budget"]
        assert budget.value == "۴۲ دولار"
        assert budget.strategy == "local_inference"

    def test_arabic_indic_digits_seed_and_resolve_engine_level(self):
        # ٤۲ seeds via Unicode-Nd \d; classification runs through the private
        # converter (٤٢ -> "42") but RawText retains the original characters.
        result = _infer("<p>الميزانية ٤٢ دولار</p>")
        budget = result.fields["budget"]
        assert budget.value == "٤٢ دولار"
        assert budget.strategy == "local_inference"

    def test_arabic_indic_classification_runs_through_converter(self):
        # Direct lock that classification sees ٤٢ through the private
        # converter (-> "42"), independent of the Unicode-Nd seeding path.
        assert _classify_value_types("٤٢") == {"NUMBER"}

    def test_extract_candidates_persian_raw_text_numeric(self):
        # Regression: candidate extraction on text containing "۴۲" produces a
        # numeric-typed candidate whose RawText retains the Persian
        # characters (mirroring .NET \p{Nd} seeding + verbatim RawText).
        cands = _extract_candidates(_flatten(_doc("<p>۴۲</p>")))
        assert [c.raw_text for c in cands] == ["۴۲"]
        assert all(c.types == {"NUMBER"} for c in cands)

    def test_trap6_semantics_are_converter_scoped_only(self):
        # Trap §4.1-6 lives ONLY in the converter: Arabic-Indic U+0660-69
        # maps to ASCII, Persian U+06F0-F9 stays untouched - while BOTH
        # classify identically as NUMBER via the shared Unicode-Nd \d.
        assert _to_ascii_arabic_digits("٤٢") == "42"
        assert _to_ascii_arabic_digits("۴۲") == "۴۲"
        assert _classify_value_types("٤٢") == _classify_value_types("۴۲") == {"NUMBER"}

    def test_softmax_probabilities_sum_to_one(self):
        root = _doc("<p>الميزانية 250 - 500 دولار</p>")
        tokens = _flatten(root)
        cands = _score_all(_extract_candidates(tokens), tokens, _page_wide_stem_counts(tokens))
        for cand in cands:
            total = sum(cand.probabilities.values())
            assert math.isclose(total, 1.0, rel_tol=1e-9)


class TestDeterminism:
    def test_same_input_identical_result_twice(self):
        html = (
            "<html><body><div>"
            "<span>الميزانية</span><span>250 - 500 دولار</span>"
            "<span>عدد العروض</span><span>12</span>"
            "</div></body></html>"
        )
        first = _infer(html)
        second = _infer(html)
        assert first.fields.keys() == second.fields.keys()
        for key in first.fields:
            assert first.fields[key] == second.fields[key]

"""StructuralExtractor unit tests (Wave B5).

Synthetic-DOM coverage of the markup-anchored and identifier-blind extraction
paths ported from StructuralExtractor.cs: class-matching styles (trap §4.1-10),
NormalizeMultiline block-boundary semantics (trap §4.1-9), the meta-panel /
owner-card cascades, the WalkToValue adjacency ladder, and the Path C gap
fillers including concatenated-label repair (trap §4.1-17).
"""

from lxml.html import HtmlElement, fromstring  # type: ignore[import-untyped]

from mostaql.scraping.parsers import structural as sx
from mostaql.text.normalization import normalize, normalize_label, to_ascii_digits


def dom(html: str) -> HtmlElement:
    return fromstring(f"<div>{html}</div>")


class TestReExportedAliases:
    def test_normalization_aliases_are_the_text_ground_implementations(self) -> None:
        # Thin aliases, never duplicated implementations.
        assert sx.normalize is normalize
        assert sx.to_ascii_digits is to_ascii_digits
        assert sx.normalize_label is normalize_label

    def test_known_labels_verbatim_count(self) -> None:
        # StructuralExtractor.cs:31-50 - exactly 17 labels.
        assert len(sx.KNOWN_LABELS) == 17
        assert "الميزانية" in sx.KNOWN_LABELS
        assert "بدأ تنفيذه منذ" in sx.KNOWN_LABELS
        assert "عدد المقترحات" in sx.KNOWN_LABELS
        # Deliberate asymmetry vs DetailParser's synonym table: the extractor's
        # known set has "مشاريع منجزة" but NOT its prefixed variant.
        assert "مشاريع منجزة" in sx.KNOWN_LABELS
        assert "المشاريع المنجزة" not in sx.KNOWN_LABELS


class TestNormalizeMultiline:
    def test_br_becomes_a_line_break(self) -> None:
        assert sx.normalize_multiline(dom("<span>سطر<br>ثان</span>")) == "سطر\nثان"  # noqa: RUF001

    def test_block_elements_append_newline_after_content(self) -> None:
        # C# appends the boundary newline AFTER recursing the block child,
        # separating it from FOLLOWING siblings (cs:209-216).
        assert sx.normalize_multiline(dom("<p>أ</p><p>ب</p>")) == "أ\nب"

    def test_horizontal_whitespace_collapses_within_lines(self) -> None:
        assert sx.normalize_multiline(dom("<span>أ  ب\t\tج<br>   د   </span>")) == "أ ب ج\nد"

    def test_three_plus_newlines_fold_to_paragraph_gap(self) -> None:
        assert sx.normalize_multiline(dom("a<br><br><br>b")) == "a\n\nb"

    def test_comments_contribute_nothing_but_tails_survive(self) -> None:
        assert sx.normalize_multiline(dom("a<!-- hidden -->b")) == "ab"


class TestClassMatchingStyles:
    def test_exact_token_match_rejects_partial_tokens(self) -> None:
        root = dom('<ul class="a skills b"></ul><ul class="skills-extra"></ul>')
        hits = sx._by_exact_token(root, "ul", "skills")
        assert len(hits) == 1
        assert hits[0].get("class") == "a skills b"

    def test_token_contains_matches_inside_single_tokens_only(self) -> None:
        root = dom('<div class="meta-row-x"></div><div class="pre meta-row"></div>')
        hits = sx._by_token_contains(root, "div", "meta-row")
        assert {hit.get("class") for hit in hits} == {"meta-row-x", "pre meta-row"}

    def test_raw_class_contains_spans_token_boundaries(self) -> None:
        root = dom('<div class="foo bar"></div>')
        element = root[0]
        assert sx._raw_class_contains(element, "oo ba")  # impossible token-wise
        assert sx._raw_class_contains(element, "bar")
        assert not sx._raw_class_contains(element, "baz")


class TestExtractMetaFields:
    def test_panel_rows_and_owner_stat_table_path_a(self) -> None:
        root = fromstring(
            """
            <div id="project-meta-panel">
              <div class="meta-row"><div class="meta-label">الميزانية:</div>
                <div class="meta-value">100 $</div></div>
              <div class="meta-row"><div class="meta-label">مدة التنفيذ</div>
                <div class="meta-value">5 يوم</div></div>
              <div class="meta-row"><div class="meta-label">بلا قيمة</div></div>
            </div>
            <div class="profile_card">
              <table class="table stats">
                <tr><td>مشاريع منجزة</td><td>27</td></tr>
                <tr><td>صف ثلاثي</td><td>x</td><td>y</td></tr>
              </table>
            </div>
            """
        )
        fields = sx.extract_meta_fields(root)
        # Keys are NormalizeLabel-canonicalized (colon variant folds in).
        assert fields[normalize_label("الميزانية")] == "100 $"
        assert fields[normalize_label("مدة التنفيذ")] == "5 يوم"
        assert fields[normalize_label("مشاريع منجزة")] == "27"
        assert normalize_label("صف ثلاثي") not in fields  # 3-td row skipped
        assert len([k for k in fields if "بلا" in k]) == 0

    def test_meta_container_class_fallback_without_panel_id(self) -> None:
        root = dom(
            '<div class="side meta-container"><div class="meta-row">'
            '<div class="meta-label">حالة المشروع</div>'
            '<div class="meta-value">مفتوح</div></div></div>'
        )
        fields = sx.extract_meta_fields(root)
        assert fields[normalize_label("حالة المشروع")] == "مفتوح"

    def test_path_b_flex_rows_require_exactly_two_element_children(self) -> None:
        # dom() wrapper keeps the profile card a DESCENDANT of the search root,
        # matching document_fromstring's full-document shape.
        root = dom(
            '<div class="profile-card">'
            '<div class="justify-between"><span>المشاريع المفتوحة</span><span>4</span></div>'
            '<div class="justify-between"><span>ثلاثي</span><span>a</span><span>b</span></div>'
            '<div class="justify-between"><span></span><span>بلا تسمية</span></div>'
            "</div>"
        )
        fields = sx.extract_meta_fields(root)
        assert fields[normalize_label("المشاريع المفتوحة")] == "4"
        assert normalize_label("ثلاثي") not in fields
        assert "" not in fields

    def test_path_c_gap_filler_repairs_concatenated_label(self) -> None:
        root = dom(
            '<div class="profile_card"><span>معدل التوظيف15.38%</span>'  # noqa: RUF001
            "<span>مشاريع منجزة</span><b>14</b></div>"
        )
        fields = sx.extract_meta_fields(root)
        # Exact-match sibling pair via next-sibling-ELEMENT.
        assert fields[normalize_label("مشاريع منجزة")] == "14"
        # Concatenated repair: ALL occurrences removed, punctuation trimmed.
        assert fields[normalize_label("معدل التوظيف")] == "15.38%"

    def test_path_c_never_overwrites_existing_keys(self) -> None:
        root = dom(
            '<div class="profile_card">'
            "<table class='table'><tr><td>مشاريع منجزة</td><td>27</td></tr></table>"
            "</div>"
        )
        # Table present => flex/gap-filler branch must NOT run at all (cs:269-281 else).
        fields = sx.extract_meta_fields(root)
        assert fields == {normalize_label("مشاريع منجزة"): "27"}

    def test_table_presence_disables_gap_fillers(self) -> None:
        root = dom(
            '<div class="profile_card"><table class="stats-table">'
            "<tr><td>مشاريع منجزة</td><td>8</td></tr></table>"
            "<span>معدل التوظيف11%</span></div>"  # noqa: RUF001
        )
        fields = sx.extract_meta_fields(root)
        assert fields.keys() == {normalize_label("مشاريع منجزة")}


class TestFindOwnerCard:
    def test_profile_card_class_wins_first(self) -> None:
        root = dom('<div class="profile_card"><h5>صاحب</h5></div>')
        card = sx.find_owner_card(root)
        assert card is not None
        assert card.get("class") == "profile_card"

    def test_kebab_variant_also_matches(self) -> None:
        root = dom('<div class="profile-card box"></div>')
        assert sx.find_owner_card(root) is not None

    def test_semantic_owner_label_fallback(self) -> None:
        root = dom(
            '<section><div class="wrap"><span>صاحب المشروع</span><b>x</b><i>y</i></div></section>'
        )
        card = sx.find_owner_card(root)
        assert card is not None
        assert card.get("class") == "wrap"

    def test_details_area_u_link_card_ancestor_fallback(self) -> None:
        root = fromstring(
            """
            <div id="projectDetailsTab">
              <div class="outer"><div class="card"><a href="/u/someone">اسم</a></div></div>
            </div>
            """
        )
        card = sx.find_owner_card(root)
        assert card is not None
        assert card.get("class") == "card"

    def test_nothing_found_returns_none(self) -> None:
        assert sx.find_owner_card(dom("<b>لا شيء</b>")) is None


class TestWalkToValueLadder:
    def test_rung1_next_sibling_of_label(self) -> None:
        root = dom("<em>عدد المقترحات</em><strong>12</strong>")
        value, method = sx._walk_to_value(root[0])
        assert (value, method) == ("12", "next_sibling_of_label")

    def test_rung2_next_td_skips_empty_non_td_siblings(self) -> None:
        root = dom("<tr><td>مدة التنفيذ</td><th></th><td>20 يوما</td></tr>")
        value, method = sx._walk_to_value(root[0][0])
        assert (value, method) == ("20 يوما", "next_td")

    def test_rung3_parent_next_sibling(self) -> None:
        root = dom("<li><b>حالة المشروع</b></li><li>منفذ</li>")
        value, method = sx._walk_to_value(root[0][0])
        assert (value, method) == ("منفذ", "parent_next_sibling")

    def test_rung4_parent_text_minus_label(self) -> None:
        root = dom("<p><span>تاريخ النشر:</span> منذ شهرين</p>")
        value, method = sx._walk_to_value(root[0][0])  # the <span> is the label
        assert (value, method) == ("منذ شهرين", "parent_text_minus_label")

    def test_rung5_grandparent_sibling_cell(self) -> None:
        # Value element PRECEDES the label wrapper: only the grandparent rung
        # can see it (cs:528-549 redesign shape).
        root = dom("<div><em>3 محادثات</em><div><span>التواصلات الجارية</span></div></div>")
        value, method = sx._walk_to_value(root[0][1][0])  # wrapper > outer-div > lbl-div > span
        assert (value, method) == ("3 محادثات", "grandparent_sibling_cell")

    def test_exhausted_ladder_returns_none_pair(self) -> None:
        root = dom("<div><span>تاريخ الصفقة</span></div>")
        assert sx._walk_to_value(root[0][0]) == (None, None)


class TestLabelDrivenExtract:
    def test_first_label_element_with_a_value_wins(self) -> None:
        root = dom("<div><span>المهارات</span></div><b>PHP و CSS</b>")
        fields = sx.label_driven_extract(root)
        assert fields[normalize_label("المهارات")] == "PHP و CSS"

    def test_labels_without_any_ladder_hit_are_absent(self) -> None:
        root = dom("<b>لا عناوين هنا</b>")
        assert sx.label_driven_extract(root) == {}

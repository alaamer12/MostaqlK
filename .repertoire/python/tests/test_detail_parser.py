"""DetailParser unit tests (Wave B5).

Fixture + synthetic-DOM coverage of the field combinator ported from
DetailParser.cs: title chain and suffix stripping, description chain, skills,
structural-first/inference-fallback provenance, the completed-only gates, the
data-bid-item override (trap §4.1-4), owner assembly incl. the synthetic hash
(trap §4.1-3), numeric parser edges, and placeholder-as-valid-resolution
provenance (trap §4.1-12).

The sibling-owned InferenceEngine is NEVER exercised for real: an autouse seam
replaces ``detail._infer_fields_once`` with a controlled fake, so these tests
stay valid regardless of when inference.py lands. The seam delegates to
``InferenceEngine.infer_fields(root).fields`` in production.
"""

from pathlib import Path

import pytest

from mostaql.errors import ParseException
from mostaql.scraping.parsers import detail as detail_module
from mostaql.scraping.parsers.detail import DetailParser, parse_leading_int, parse_percent

FIXTURES_DIR = Path(__file__).parent / "regression" / "fixtures" / "detail"


def load_fixture(name: str) -> str:
    return (FIXTURES_DIR / name).read_text(encoding="utf-8")


def page(body: str, *, head: str = "", title: str | None = "مشروع تجريبي") -> str:
    title_tag = f"<title>{title}</title>" if title is not None else ""
    return f"<html><head>{title_tag}{head}</head><body><h1>عنوان</h1>{body}</body></html>"


def meta_row(label: str, value: str) -> str:
    return (
        '<div class="meta-row">'
        f'<div class="meta-label">{label}</div>'
        f'<div class="meta-value">{value}</div></div>'
    )


@pytest.fixture(autouse=True)
def _no_real_inference(monkeypatch: pytest.MonkeyPatch) -> None:
    """Structural-success pages must never depend on the real engine."""
    monkeypatch.setattr(detail_module, "_infer_fields_once", lambda root: {})


def prov(details, key: str):  # type: ignore[no-untyped-def]
    return details.field_provenance[key]


class TestErrorContracts:
    def test_blank_html_raises_parse_001(self) -> None:
        with pytest.raises(ParseException) as excinfo:
            DetailParser.parse(1, "   \n  ")
        assert excinfo.value.error.code == "PARSE-001"

    def test_missing_title_raises_parse_002(self) -> None:
        with pytest.raises(ParseException) as excinfo:
            DetailParser.parse(77, load_fixture("missing_title.html"))
        error = excinfo.value.error
        assert error.code == "PARSE-002"
        assert "77" in error.internal_message


class TestTitleChain:
    @pytest.mark.parametrize(
        ("raw", "expected"),
        [
            ("عنوان المشروع - مستقل", "عنوان المشروع"),
            ("عنوان المشروع | Mostaql", "عنوان المشروع"),
            ("عنوان المشروع – Mostaqlk", "عنوان المشروع"),  # noqa: RUF001 (en-dash separator)
            # LAST separator wins, earlier separators survive.
            ("أول - ثانٍ - مستقل", "أول - ثانٍ"),
            # Non-site suffixes are kept untouched.
            ("نص المشروع - مدونة أخرى", "نص المشروع - مدونة أخرى"),
            # Bare trailing keyword second pass (case-insensitive).
            ("مشروع تجربة مستقل", "مشروع تجربة"),
            ("مشروع تجربة MOSTAQLK", "مشروع تجربة"),
        ],
    )
    def test_suffix_stripping_variants(self, raw: str, expected: str) -> None:
        html = page("", title=raw).replace("<h1>عنوان</h1>", "")
        details = DetailParser.parse(9, html)
        assert details.title == expected

    def test_h1_preferred_over_og_and_title_tag(self) -> None:
        html = page(
            "",
            head='<meta property="og:title" content="عنوان أو جي" />',
            title="عنوان التاج - مستقل",
        )
        details = DetailParser.parse(9, html)
        assert details.title == "عنوان"

    def test_og_title_fallback_when_h1_missing(self) -> None:
        html = page(
            "",
            head='<meta name="og:title" content="من الميتا" />',
            title="تاج الصفحة",
        ).replace("<h1>عنوان</h1>", "")
        details = DetailParser.parse(9, html)
        assert details.title == "من الميتا"

    def test_title_tag_is_last_resort(self) -> None:
        html = "<html><head><title>فقط التاج - مستقل</title></head><body></body></html>"
        details = DetailParser.parse(9, html)
        assert details.title == "فقط التاج"


class TestMetaPanelTableFixture:
    def test_full_end_to_end_mapping(self) -> None:
        details = DetailParser.parse(1001, load_fixture("meta_panel_table.html"))

        assert details.title == "مشروع متجر إلكتروني"  # " - مستقل" stripped
        assert details.url == ""  # trap §4.1-2: scraper attaches later
        assert details.description == (
            "المهام:\n- تصميم واجهة المتجر\n- ربط بوابة الدفع\nالفترة: أسبوعان"  # noqa: RUF001
        )
        assert details.enrichment_status.value == "Enriched"
        assert details.discovered_at == details.enriched_at  # same instant, trap §4.1-22

        assert details.budget == "100 - 250 $"
        assert details.delivery_days == 10
        assert details.project_status == "مفتوح"
        assert details.publish_time_number == 3
        assert details.publish_time_text == "منذ 3 أيام"

        assert details.skills[0].name == "PHP"
        assert details.skills[0].url == "/skills/php"
        assert details.skills[1].name == "تصميم شعارات"
        assert details.skills[1].url is None

        owner = details.owner
        assert owner.owner_id == 424242  # data-user-id on the card itself
        assert owner.name == "أحمد المصمم"
        assert owner.profile_url == "https://mostaql.com/u/ahmed-designer"
        assert owner.hiring_rate_percent == 92.5
        assert owner.completed_projects_count == 27
        assert owner.registered_at == "2020/05/11"

    def test_structural_provenance_confidences(self) -> None:
        details = DetailParser.parse(1001, load_fixture("meta_panel_table.html"))

        for key in ("budget", "duration", "project_status", "published_date"):
            resolution = prov(details, key)
            assert (resolution.source, resolution.confidence) == ("structural", 1.0)

        # No bids and no proposal label anywhere => deterministic null.
        proposal = prov(details, "proposal_count")
        assert (proposal.value, proposal.source, proposal.confidence) == (None, "none", 0.0)
        assert details.mismatches == []


class TestMetaPanelFlexFixture:
    def test_meta_container_fallback_and_path_b_stats(self) -> None:
        details = DetailParser.parse(2002, load_fixture("meta_panel_flex.html"))

        assert details.budget == "50 - 100 $"
        owner = details.owner
        assert owner.owner_id == 777
        assert owner.open_projects_count == 4
        assert owner.in_progress_projects_count == 2
        assert prov(details, "open_projects_count").value == "4"


class TestLabelOnlyLadder:
    def test_every_rung_resolves_without_any_panel(self) -> None:
        details = DetailParser.parse(3003, load_fixture("label_only.html"))

        assert details.delivery_days == 20  # next_td rung
        assert details.project_status == "منفذ"  # parent_next_sibling rung
        assert details.publish_time_number == 2  # منذ شهرين dual
        assert details.owner.ongoing_communications_count == 3  # grandparent rung
        # Proposal LABEL genuinely present + zero bids => text value survives.
        assert details.proposal_count == 12
        assert details.proposal_count_text == "12 عروضا"
        # Identifier-blind skill sweep (/skills/, /tag/).
        assert [skill.name for skill in details.skills] == ["Python", "Django"]


class TestInferenceFallbackSeam:
    def test_inference_used_once_and_gated_by_label_presence(
        self, monkeypatch: pytest.MonkeyPatch
    ) -> None:
        calls: list[int] = []

        def fake_infer(root: object) -> dict[str, tuple[str | None, float]]:
            calls.append(1)
            return {
                "budget": ("150 - 300 $", 0.87),
                "started_since": ("منذ 3 أيام", 0.50),
                "project_status": ("مغلق", 0.90),
                "ongoing_conversations": ("4 محادثات", 0.70),
            }

        monkeypatch.setattr(detail_module, "_infer_fields_once", fake_infer)
        details = DetailParser.parse(4004, load_fixture("inference_needed.html"))

        # Lazily computed ONCE even though budget AND hire_rate both fail sanity.
        assert len(calls) == 1

        budget = prov(details, "budget")
        assert (budget.value, budget.source, budget.confidence) == (
            "150 - 300 $",
            "inference",
            0.87,
        )
        assert details.budget == "150 - 300 $"

        # Completed-only pass 1: بدأ تنفيذه منذ appears NOWHERE on the page,
        # so the inference-sourced value has nothing genuine to latch onto.
        started = prov(details, "started_since")
        assert (started.value, started.source, started.confidence) == (None, "none", 0.0)

        # التواصلات الجارية IS present (gap-filler span) => inference survives.
        ongoing = prov(details, "ongoing_conversations")
        assert ongoing.source == "inference"
        assert details.owner.ongoing_communications_count == 4

    def test_mismatch_recorded_and_inference_overrides_failed_sanity(
        self, monkeypatch: pytest.MonkeyPatch
    ) -> None:
        monkeypatch.setattr(
            detail_module,
            "_infer_fields_once",
            lambda root: {"hire_rate": ("45%", 0.6)},
        )
        details = DetailParser.parse(4004, load_fixture("inference_needed.html"))

        # hire_rate's structural candidate "قابل للتفاوض" is non-null but FAILS
        # numeric sanity -> inference consulted -> disagreement recorded ->
        # inference overrides because sanity failed (cs:141-147).
        assert [(m.field, m.structural_value, m.inference_value) for m in details.mismatches] == [
            ("hire_rate", "قابل للتفاوض", "45%")
        ]
        assert details.owner.hiring_rate_percent == 45.0
        resolution = prov(details, "hire_rate")
        assert (resolution.value, resolution.source, resolution.confidence) == (
            "45%",
            "inference",
            0.6,
        )

    def test_first_field_is_never_cross_validated(self) -> None:
        """Faithful C# sequencing quirk (trap §4.1-11): project_status is the
        FIRST field processed, so inference cannot exist yet at its turn — a
        trusted structural value there is always blind, even when the engine
        would later disagree."""
        details = DetailParser.parse(4004, load_fixture("meta_panel_table.html"))
        # meta_panel_table has no sanity failures at all => engine never runs,
        # hence zero mismatches anywhere.
        assert details.mismatches == []
        assert prov(details, "project_status").source == "structural"


class TestBidOverride:
    def test_single_bid_row_still_emits_plural_string(self) -> None:
        details = DetailParser.parse(5005, load_fixture("bid_rows.html"))
        proposal = prov(details, "proposal_count")
        assert (proposal.value, proposal.source, proposal.confidence) == (
            "1 عروض",
            "structural",
            1.0,
        )
        assert details.proposal_count == 1
        assert details.proposal_count_text == "1 عروض"

    def test_multiple_bid_rows_counted(self) -> None:
        rows = "".join(f'<div data-bid-item="{i}">عرض</div>' for i in range(3))
        details = DetailParser.parse(5006, page(rows))
        assert details.proposal_count_text == "3 عروض"

    def test_no_bids_and_no_labels_forces_null(self) -> None:
        details = DetailParser.parse(5007, page("<p>وصف فقط</p>"))
        proposal = prov(details, "proposal_count")
        assert (proposal.value, proposal.source, proposal.confidence) == (None, "none", 0.0)


class TestCompletionStatusGate:
    def test_completed_project_keeps_deal_fields(self) -> None:
        details = DetailParser.parse(6006, load_fixture("completed_project.html"))

        assert details.project_status == "مكتمل"
        assert prov(details, "started_since").value == "منذ 5 أيام"
        assert prov(details, "deal_date").value == "2024/03/01"
        assert prov(details, "delivery_date").value == "2024/03/15"
        assert details.owner.completed_projects_count == 8

    def test_open_project_nulls_deal_fields_but_keeps_completed_count(self) -> None:
        details = DetailParser.parse(6007, load_fixture("open_project.html"))

        assert details.project_status == "مفتوح للعروض"
        for key in ("started_since", "deal_date", "delivery_date"):
            resolution = prov(details, key)
            # Source/confidence PRESERVED; only the value is nulled (trap §4.1-13).
            assert (resolution.value, resolution.source, resolution.confidence) == (
                None,
                "structural",
                1.0,
            )
        # completed_projects_count is EXEMPT from the completion-status gate.
        assert details.owner.completed_projects_count == 8


class TestPlaceholderProvenance:
    def test_placeholders_resolve_valid_then_null_with_provenance(self) -> None:
        details = DetailParser.parse(7007, load_fixture("placeholder_fields.html"))

        # SanityOk treats placeholders as VALID structural resolutions; they are
        # nulled afterwards while source/confidence survive (trap §4.1-12).
        for key in ("budget", "hire_rate", "ongoing_conversations", "project_status"):
            resolution = prov(details, key)
            assert (resolution.value, resolution.source, resolution.confidence) == (
                None,
                "structural",
                1.0,
            )

        assert details.budget is None
        assert details.owner.hiring_rate_percent is None
        # Status value nulled by the placeholder pass => completion gate also fires.
        assert details.project_status is None


class TestConcatenatedAndArabicNumerics:
    def test_concatenated_label_repair_end_to_end(self) -> None:
        details = DetailParser.parse(8008, load_fixture("concatenated_label.html"))
        assert details.owner.hiring_rate_percent == 15.38
        assert details.owner.completed_projects_count == 14
        assert prov(details, "hire_rate").value == "15.38%"

    def test_arabic_indic_digits_through_both_parsers(self) -> None:
        details = DetailParser.parse(9009, load_fixture("arabic_digits.html"))
        assert details.delivery_days == 15  # ١٥ يوما  # noqa: RUF003
        assert details.owner.hiring_rate_percent == 36.36  # ٣٦.٣٦%
        assert details.owner.completed_projects_count == 1250  # ١,٢٥٠ grouped  # noqa: RUF003
        assert details.budget == "$٢٠٠٠"  # raw text preserved verbatim


class TestOwnerAssembly:
    def test_synthetic_hash_id_deterministic(self) -> None:
        details = DetailParser.parse(1111, load_fixture("owner_hash.html"))
        # h*31+ord(c), wrapped signed 64-bit, abs() — stable across runs.
        assert details.owner.owner_id == 2920507799877821
        assert details.owner.name == "خالد المبدع"
        assert details.owner.profile_url == "https://mostaql.com/u/khaled_dev"

    def test_display_name_hash_last_resort(self) -> None:
        body = '<div class="profile_card"><h5 class="profile__name">مجرد اسم</h5></div>'
        details = DetailParser.parse(1112, page(body))

        expected = 0
        for ch in "مجرد اسم":
            expected = (expected * 31 + ord(ch)) & 0xFFFFFFFFFFFFFFFF
        if expected >= 1 << 63:
            expected -= 1 << 64

        assert details.owner.owner_id == abs(expected)
        assert details.owner.name == "مجرد اسم"

    @pytest.mark.parametrize(
        ("href", "expected_url"),
        [
            ("/u/sara", "https://mostaql.com/u/sara"),
            ("ar/u/sara", "https://mostaql.com/ar/u/sara"),  # slash inserted
            ("https://mostaql.com/u/sara", "https://mostaql.com/u/sara"),
            ("http://ext.example/u/sara", "http://ext.example/u/sara"),
        ],
    )
    def test_profile_url_absolutization(self, href: str, expected_url: str) -> None:
        body = f'<div class="profile-card"><a href="{href}">اسم</a></div>'
        details = DetailParser.parse(1113, page(body))
        assert details.owner.profile_url == expected_url


class TestDescriptionFallbacks:
    def test_page_wide_wrapper_when_tab_absent(self) -> None:
        html = page('<div class="x text-wrapper-div y">وصف بلا تبويب</div>')
        assert DetailParser.parse(1212, html).description == "وصف بلا تبويب"

    def test_og_description_single_line_fallback(self) -> None:
        html = page(
            "",
            head='<meta property="og:description" content="وصف   ميتا" />',
        )
        html = html.replace("<h1>عنوان</h1>", "")
        assert DetailParser.parse(1213, html).description == "وصف ميتا"

    def test_densest_block_beats_og_only_when_strictly_longer(self) -> None:
        long_text = "كلمة " * 60
        html = page(
            f"<div>{long_text}</div>",
            head='<meta property="og:description" content="موجز" />',
        )
        html = html.replace("<h1>عنوان</h1>", "")
        assert DetailParser.parse(1214, html).description.startswith(long_text.strip())

        short_html = page(
            "<div>قصير</div>",
            head='<meta property="og:description" content="' + long_text.strip() + '" />',
        )
        short_html = short_html.replace("<h1>عنوان</h1>", "")
        assert DetailParser.parse(1215, short_html).description == long_text.strip()


class TestNumericParsers:
    @pytest.mark.parametrize(
        ("text", "expected"),
        [
            ("92.5%", 92.5),
            ("٣٦.٣٦٪", 36.36),
            ("6,25 %", 6.25),
            ("12", 12.0),
            ("abc", None),
            ("", None),
            (None, None),
        ],
    )
    def test_parse_percent(self, text: str | None, expected: float | None) -> None:
        assert parse_percent(text) == expected

    @pytest.mark.parametrize(
        ("text", "expected"),
        [
            ("10 يوم", 10),
            ("1,250 مشاريع", 1250),  # grouped alternative preferred
            ("1.250.000", 1250000),  # multi-group
            ("1234", 1234),  # plain run fallback at position 0
            ("99999999999", None),  # Int32 TryParse overflow -> null
            ("abc", None),
            (None, None),
        ],
    )
    def test_parse_leading_int(self, text: str | None, expected: int | None) -> None:
        assert parse_leading_int(text) == expected


class TestSanityAndAgreementUnits:
    def test_placeholder_is_valid_resolution_for_numeric_fields(self) -> None:
        assert detail_module._sanity_ok("budget", "لم يحسب بعد") is True
        assert detail_module._is_placeholder("غير محدد") is True

    def test_numeric_field_requires_some_digit(self) -> None:
        assert detail_module._sanity_ok("hire_rate", "قابل للتفاوض") is False
        assert detail_module._sanity_ok("hire_rate", "٤٠%") is True
        assert detail_module._sanity_ok("project_status", "قابل للتفاوض") is True

    def test_values_agree_containment_and_null_semantics(self) -> None:
        agree = detail_module._values_agree
        assert agree(None, "x") is True  # null on either side agrees (trap §4.1-15)
        assert agree("x", None) is True
        assert agree("100 $", "100 $ ") is True  # trim-equal
        assert agree("100", "100 - 250 $") is True  # containment either direction
        assert agree("250 - 100", "100") is True  # ...including reversed containment
        assert agree("مفتوح", "مغلق") is False  # genuine disagreement

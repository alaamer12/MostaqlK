"""Malformed-input matrix (plan §13 Hardening + §19 batch semantics).

One malformed record never aborts the batch unless the C# parser aborts.
Each matrix row pins EITHER a successful parse with documented defaults OR
the exact C#-faithful PARSE-* exception — no new exception types anywhere.

Fixtures live in ``regression/fixtures/malformed/``; negative-control rows
reuse existing ``listing/`` and ``detail/`` fixtures so both matrix branches
(success / exact PARSE code) stay exercised without duplicating files.
Duplicate-label semantics were VERIFIED against C# StructuralExtractor.cs:261
(plain dictionary assignment ⇒ LAST occurrence wins) before pinning.
"""

from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import pytest

from mostaql.models import EnrichmentStatus, ProjectDetails, ProjectSummary
from mostaql.scraping.parsers.detail import DetailParser
from mostaql.scraping.parsers.errors import ParseException
from mostaql.scraping.parsers.listing import ListingParser

FIXTURES_ROOT = Path(__file__).parent / "regression" / "fixtures"
SAMPLE_PROJECT_ID = 4242


def load_html(relative_path: str) -> str:
    return (FIXTURES_ROOT / relative_path).read_text(encoding="utf-8")


# ---------------------------------------------------------------------------
# Per-fixture verifiers (successful-parse branch)
# ---------------------------------------------------------------------------


def _verify_tier3_fallback(records: list[ProjectSummary]) -> None:
    """No tier-1/tier-2 cards, but plain links exist: sweep rescues the batch;
    junk anchors skipped individually; duplicate id keeps FIRST (trap 14)."""
    assert [r.project_id for r in records] == [7001, 7002, 7003]
    for record in records:
        assert record.title != ""
        assert record.url.startswith("/project/") or record.url.startswith(
            "https://mostaql.com/project/"
        )
        assert record.proposal_count == 0
        assert record.proposal_count_text == ""
        assert record.publish_time_number == 0
        assert record.publish_time_text == ""
        assert record.client_name == ""
        assert record.description == ""
        assert record.enrichment_status is EnrichmentStatus.PENDING
        assert record.is_unread is True


def _verify_mojibake_survives(records: list[ProjectSummary]) -> None:
    """Replacement characters flow through normalization untouched; the intact
    Arabic time meta still classifies as a time signal."""
    assert len(records) == 1
    record = records[0]
    assert "\ufffd" in record.title
    assert "تحليل بيانات" in record.title
    assert "\ufffd" in record.description
    assert record.publish_time_text == "منذ ساعتين"
    assert record.publish_time_number == 2


def _verify_no_meta_defaults(details: ProjectDetails) -> None:
    """Every field lands on its documented default instead of failing."""
    assert details.title == "مشروع بلا لوحة بيانات"
    assert details.description == "وصف قصير بدون أي حقول هيكلية."
    assert details.budget is None
    assert details.delivery_days is None
    assert details.project_status is None
    assert details.publish_time_number == 0
    assert details.publish_time_text == ""
    assert details.proposal_count == 0
    assert details.proposal_count_text == ""
    assert details.skills == []
    assert details.owner.owner_id == 0
    assert details.owner.name == ""
    assert details.owner.profile_url is None
    assert details.discovered_at == details.enriched_at  # trap §4.1-22
    provenance = details.field_provenance
    assert provenance["proposal_count"].source == "none"
    assert provenance["proposal_count"].value is None
    assert provenance["budget"].value is None
    assert details.mismatches == []


def _verify_empty_description(details: ProjectDetails) -> None:
    """Description chain exhausted everywhere -> empty STRING, not None."""
    assert details.description == ""


def _verify_weird_skills(details: ProjectDetails) -> None:
    """Empty/whitespace lis skipped silently; nested tags flatten via
    text_content; entity decoded by lxml before normalize sees it."""
    names = [skill.name for skill in details.skills]
    assert names == ["PHP", "Laravel & Vue", "Python"]
    urls = {skill.name: skill.url for skill in details.skills}
    assert urls["PHP"] is None
    assert urls["Laravel & Vue"] is None
    assert urls["Python"] == "/skills/python"


def _verify_absolute_date_quirk(details: ProjectDetails) -> None:
    """Trap §4.1-5: digit-run precedence poisons PublishTimeNumber
    ("2024/05/01" -> 2024); raw text survives verbatim; no absolute-date
    parsing exists anywhere."""
    assert details.publish_time_number == 2024
    assert details.publish_time_text == "2024/05/01"
    provenance = details.field_provenance["published_date"]
    assert provenance.source == "structural"
    assert provenance.confidence == 1.0
    assert provenance.value == "2024/05/01"


def _verify_duplicate_labels_last_wins(details: ProjectDetails) -> None:
    """VERIFIED vs C# StructuralExtractor.cs:261 — `results[key] = value` over
    document-order rows means the LAST occurrence wins (the task's 'first
    structural wins' hypothesis was checked and corrected). The Python port
    assigns identically; gap-fillers only fill MISSING keys."""
    assert details.budget == "250 $"
    provenance = details.field_provenance["budget"]
    assert provenance.source == "structural"
    assert provenance.confidence == 1.0
    assert provenance.value == "250 $"


# ---------------------------------------------------------------------------
# Matrix definition + parametrized runner
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class MatrixCase:
    fixture_path: str
    page: str  # "listing" | "detail"
    expected_code: str | None  # None => successful parse, run verifier
    verify: Callable[..., None] | None = None


MATRIX: list[MatrixCase] = [
    MatrixCase(
        "malformed/listing_no_cards_but_links.html",
        "listing",
        None,
        _verify_tier3_fallback,
    ),
    MatrixCase(
        "malformed/listing_broken_encoding.html",
        "listing",
        None,
        _verify_mojibake_survives,
    ),
    MatrixCase("malformed/detail_no_meta.html", "detail", None, _verify_no_meta_defaults),
    MatrixCase(
        "malformed/detail_empty_description.html", "detail", None, _verify_empty_description
    ),
    MatrixCase("malformed/detail_weird_skills.html", "detail", None, _verify_weird_skills),
    MatrixCase(
        "malformed/detail_absolute_date_publish.html",
        "detail",
        None,
        _verify_absolute_date_quirk,
    ),
    MatrixCase(
        "malformed/detail_duplicate_labels.html",
        "detail",
        None,
        _verify_duplicate_labels_last_wins,
    ),
    # Negative controls reusing existing fixtures: exact C#-faithful PARSE codes.
    MatrixCase("listing/blank.html", "listing", "PARSE-001"),
    MatrixCase("listing/empty_body.html", "listing", "PARSE-003"),
    MatrixCase("detail/missing_title.html", "detail", "PARSE-002"),
]


@pytest.mark.parametrize(
    ("case",),
    [(case,) for case in MATRIX],
    ids=[case.fixture_path for case in MATRIX],
)
def test_malformed_matrix(case: MatrixCase) -> None:
    html = load_html(case.fixture_path)

    if case.expected_code is not None:
        if case.page == "listing":
            with pytest.raises(ParseException) as excinfo:
                ListingParser.parse(html)
        else:
            with pytest.raises(ParseException) as excinfo:
                DetailParser.parse(SAMPLE_PROJECT_ID, html)
        assert excinfo.value.error.code == case.expected_code
        return

    assert case.verify is not None
    if case.page == "listing":
        records = ListingParser.parse(html)
        case.verify(records)
    else:
        details = DetailParser.parse(SAMPLE_PROJECT_ID, html)
        case.verify(details)


def test_matrix_never_introduces_new_exception_types() -> None:
    """Every PARSE failure in the matrix is a ParseException carrying a stable
    PARSE-* code — the hardening suite adds no new exception types."""
    for case in MATRIX:
        if case.expected_code is None:
            continue
        html = load_html(case.fixture_path)
        parser = ListingParser if case.page == "listing" else DetailParser
        args: tuple[Any, ...] = (
            (html,)
            if case.page == "listing"
            else (
                SAMPLE_PROJECT_ID,
                html,
            )
        )
        try:
            parser.parse(*args)
        except ParseException as exc:
            assert exc.error.code == case.expected_code
        else:  # pragma: no cover - guarded by the matrix rows above
            pytest.fail(f"{case.fixture_path} was expected to raise {case.expected_code}")

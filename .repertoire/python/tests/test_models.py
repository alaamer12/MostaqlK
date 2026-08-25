"""Tests for mostaql.models defaults and mostaql.errors contracts (plan §8)."""

from dataclasses import FrozenInstanceError
from datetime import UTC, datetime

import pytest

from mostaql.errors import (
    BackboneError,
    DiffStateError,
    DomainError,
    HttpRequestError,
    HttpUnexpectedError,
    NetworkTimeoutError,
    ParseException,
    SchemaMismatchError,
    StoreOperationError,
    diff_known_state_unavailable,
    empty_html,
    enrich_max_attempts_exhausted,
    enrich_unexpected,
    missing_title,
    no_project_rows,
    poll_cancelled,
    poll_listing_fetch_failed,
    schema_mismatch,
    store_query_failed,
)
from mostaql.models import (
    EnrichmentStatus,
    FieldMismatch,
    FieldResolution,
    Owner,
    ProjectDetails,
    ProjectSkill,
    ProjectSummary,
)

NOW = datetime(2026, 8, 25, 12, 0, 0, tzinfo=UTC)


def make_summary(**overrides):
    overrides.setdefault("project_id", 42)
    overrides.setdefault("title", "مشروع تجريبي")
    overrides.setdefault("discovered_at", NOW)
    return ProjectSummary(**overrides)


class TestEnrichmentStatus:
    def test_string_values_match_csharp_column_values(self):
        assert EnrichmentStatus.PENDING.value == "Pending"
        assert EnrichmentStatus.ENRICHED.value == "Enriched"
        assert EnrichmentStatus.FAILED.value == "Failed"

    def test_is_str_enum(self):
        assert isinstance(EnrichmentStatus.PENDING, str)
        assert f"{EnrichmentStatus.PENDING}" == "Pending"


class TestProjectSkill:
    def test_defaults(self):
        skill = ProjectSkill(name="PHP")
        assert skill.name == "PHP"
        assert skill.url is None

    def test_frozen(self):
        skill = ProjectSkill(name="PHP")
        with pytest.raises(FrozenInstanceError):
            skill.name = "Go"


class TestOwnerDefaults:
    def test_snapshot(self):
        owner = Owner()
        assert owner.owner_id == 0
        assert owner.name == ""
        assert owner.profile_url is None
        assert owner.avatar_url is None
        assert owner.rating is None
        assert owner.completed_projects_count is None
        assert owner.hiring_rate_percent is None
        assert owner.registered_at is None
        assert owner.open_projects_count is None
        assert owner.in_progress_projects_count is None
        assert owner.ongoing_communications_count is None


class TestProjectSummaryDefaults:
    def test_snapshot_empty_string_not_null(self):
        summary = make_summary()
        assert summary.project_id == 42
        assert summary.title == "مشروع تجريبي"
        assert summary.url == ""
        assert summary.client_name == ""
        assert summary.publish_time_number == 0
        assert summary.publish_time_text == ""
        assert summary.proposal_count == 0
        assert summary.proposal_count_text == ""
        assert summary.description == ""
        assert summary.skills_text == ""
        assert summary.budget is None
        assert summary.delivery_days is None
        assert summary.project_status is None

    def test_lifecycle_defaults(self):
        summary = make_summary()
        assert summary.is_unread is True
        assert summary.enrichment_status is EnrichmentStatus.PENDING
        assert summary.enriched_at is None

    def test_discovered_at_required(self):
        with pytest.raises(TypeError):
            ProjectSummary(project_id=1, title="t")

    def test_frozen(self):
        summary = make_summary()
        with pytest.raises(FrozenInstanceError):
            summary.project_id = 7


class TestFieldRecords:
    def test_field_resolution_shape(self):
        resolution = FieldResolution(value=None, source="none", confidence=0.0)
        assert resolution.value is None
        assert resolution.source == "none"
        assert resolution.confidence == 0.0

    def test_field_mismatch_shape(self):
        mismatch = FieldMismatch(field="hire_rate", structural_value="5%", inference_value=None)
        assert mismatch.field == "hire_rate"
        assert mismatch.structural_value == "5%"
        assert mismatch.inference_value is None


class TestProjectDetails:
    def test_inherits_project_summary(self):
        details = ProjectDetails(project_id=1, title="t", discovered_at=NOW)
        assert isinstance(details, ProjectSummary)

    def test_default_collections_and_owner(self):
        details = ProjectDetails(project_id=1, title="t", discovered_at=NOW)
        assert isinstance(details.owner, Owner)
        assert details.owner.owner_id == 0
        assert details.skills == []
        assert details.field_provenance == {}
        assert details.mismatches == []

    def test_explicit_provenance_and_mismatches(self):
        provenance = {"project_status": FieldResolution("مفتوح", "structural", 1.0)}
        mismatches = [FieldMismatch("budget", "100$", None)]
        details = ProjectDetails(
            project_id=1,
            title="t",
            discovered_at=NOW,
            enriched_at=NOW,
            enrichment_status=EnrichmentStatus.ENRICHED,
            field_provenance=provenance,
            mismatches=mismatches,
        )
        assert details.field_provenance == provenance
        assert details.mismatches == mismatches
        assert details.enrichment_status is EnrichmentStatus.ENRICHED

    def test_slots_through_inheritance_no_instance_dict(self):
        details = ProjectDetails(project_id=1, title="t", discovered_at=NOW)
        assert not hasattr(details, "__dict__")

    def test_frozen(self):
        details = ProjectDetails(project_id=1, title="t", discovered_at=NOW)
        with pytest.raises(FrozenInstanceError):
            details.title = "other"


class TestDomainErrorStr:
    def test_str_format(self):
        error = DomainError(code="X-001", internal_message="boom", external_message="خطأ")
        assert str(error) == "[X-001] boom"

    def test_optional_fields_default(self):
        error = DomainError(code="X-001", internal_message="m", external_message="e")
        assert error.fix_message is None
        assert error.cause is None


class TestBackboneExceptions:
    def test_carries_error_attribute(self):
        error = poll_cancelled()
        exc = StoreOperationError(error)
        assert exc.error is error
        assert str(exc) == "[POLL-002] Poll cycle was cancelled."

    def test_hierarchy(self):
        error = DomainError(code="C", internal_message="m", external_message="e")
        for cls in (
            NetworkTimeoutError,
            HttpRequestError,
            HttpUnexpectedError,
            ParseException,
            SchemaMismatchError,
            StoreOperationError,
            DiffStateError,
        ):
            exc = cls(error)
            assert isinstance(exc, BackboneError)
            assert exc.error is error


class TestPollFactories:
    def test_poll_listing_fetch_failed_exact_wording(self):
        cause = RuntimeError("socket closed")
        error = poll_listing_fetch_failed(cause)
        assert error.code == "POLL-001"
        assert error.internal_message == "Failed to fetch listing page: socket closed"
        assert error.external_message == "تعذر تحديث قائمة المشاريع."
        assert error.fix_message == "سيتم إعادة المحاولة عند دورة الفحص التالية."
        assert error.cause is cause

    def test_poll_cancelled_exact_wording(self):
        error = poll_cancelled()
        assert error.code == "POLL-002"
        assert error.internal_message == "Poll cycle was cancelled."
        assert error.external_message == "تم إيقاف الفحص."
        assert error.fix_message is None


class TestDiffFactory:
    def test_diff_known_state_unavailable_exact_wording(self):
        cause = ValueError("db locked")
        error = diff_known_state_unavailable(cause)
        assert error.code == "DIFF-001"
        assert error.internal_message == "Failed to load known-state for diffing: db locked"
        assert error.external_message == "تعذر مقارنة المشاريع الجديدة بالمشاريع المعروفة."
        assert error.cause is cause


class TestEnrichFactories:
    def test_max_attempts_exhausted_exact_wording(self):
        last_cause = RuntimeError("timeout")
        last = DomainError(
            code="HTTP-003",
            internal_message="Request timed out",
            external_message="e",
            cause=last_cause,
        )
        error = enrich_max_attempts_exhausted(77, 5, last)
        assert error.code == "ENRICH-001"
        assert error.internal_message == (
            "Project 77 failed enrichment after 5 attempts. Last error: Request timed out"
        )
        assert error.external_message == "تعذر جلب تفاصيل المشروع بعد عدة محاولات."
        assert error.fix_message == (
            "سيتم تجاهل هذا المشروع؛ قد يظهر مجدداً في الفحص التالي إن لم يتم حفظه."
        )
        assert error.cause is last_cause

    def test_enrich_unexpected_exact_wording(self):
        cause = RuntimeError("segfault")
        error = enrich_unexpected(9, cause)
        assert error.code == "ENRICH-002"
        assert error.internal_message == (
            "Unexpected exception while enriching project 9: segfault"
        )
        assert error.external_message == "حدث خطأ غير متوقع أثناء معالجة أحد المشاريع."
        assert error.fix_message == (
            "تم تجاوز هذا المشروع والاستمرار في المعالجة؛ راجع سجل الأحداث للتفاصيل."
        )
        assert error.cause is cause


class TestStoreFactories:
    def test_store_query_failed_exact_wording(self):
        cause = RuntimeError("disk I/O error")
        error = store_query_failed("UpsertDetailsAsync", cause)
        assert error.code == "DB-002"
        assert error.internal_message == "Query 'UpsertDetailsAsync' failed: disk I/O error"
        assert error.external_message == "حدث خطأ أثناء الوصول إلى البيانات المحفوظة."
        assert error.cause is cause

    def test_schema_mismatch_db_003_wording(self):
        error = schema_mismatch(current=2, expected=1)
        assert error.code == "DB-003"
        assert error.internal_message == (
            "Database schema is invalid or out of date: Database schema version 2 does "
            "not match the version expected by this build (1) and no migration path exists yet."
        )
        assert error.external_message == "قاعدة البيانات غير متوافقة مع هذا الإصدار من التطبيق."


class TestParseFactories:
    def test_empty_html_verbatim(self):
        error = empty_html("ListingParser")
        assert error.code == "PARSE-001"
        assert error.internal_message == "ListingParser.Parse received empty HTML."

    def test_missing_title_verbatim(self):
        error = missing_title(123)
        assert error.code == "PARSE-002"
        assert error.internal_message == (
            "DetailParser.Parse could not locate a title (h1) for project 123."
        )

    def test_no_project_rows_verbatim_including_div_project_card_trap(self):
        # Plan §4.1 trap 1: message says div.project-card though tier-2 class is project-item.
        error = no_project_rows()
        assert error.code == "PARSE-003"
        assert error.internal_message == (
            "ListingParser.Parse could not locate any project rows "
            "(tr.project-row or div.project-card)."
        )

    def test_parse_factories_raise_wrapped_exception(self):
        error = empty_html("DetailParser")
        exc = ParseException(error)
        assert exc.error.code == "PARSE-001"

"""Structured domain errors and typed exception hierarchy (C# Core/DomainError + module Errors.cs).

Pure leaf module: imports nothing internal (import-linter ``pure-leaves`` contract).
Every factory mirrors the exact code and wording of its C# counterpart.
"""

from dataclasses import dataclass

__all__ = [
    "BackboneError",
    "DiffStateError",
    "DomainError",
    "HttpRequestError",
    "NetworkTimeoutError",
    "ParseException",
    "SchemaMismatchError",
    "StoreOperationError",
    "diff_known_state_unavailable",
    "empty_html",
    "enrich_max_attempts_exhausted",
    "enrich_unexpected",
    "missing_title",
    "no_project_rows",
    "poll_cancelled",
    "poll_listing_fetch_failed",
    "schema_mismatch",
    "store_query_failed",
]


@dataclass(frozen=True, slots=True)
class DomainError:
    """Immutable structured error carried by failing operations (plan §8).

    Mirrors ``MostaqlK.Core.DomainError``: stable ``code``, developer-facing
    ``internal_message``, user-facing Arabic ``external_message``, optional
    ``fix_message`` and optional ``cause``.
    """

    code: str
    internal_message: str
    external_message: str
    fix_message: str | None = None
    cause: BaseException | None = None

    def __str__(self) -> str:
        return f"[{self.code}] {self.internal_message}"


class BackboneError(Exception):
    """Base exception carrying a :class:`DomainError` on ``.error``."""

    def __init__(self, error: DomainError) -> None:
        super().__init__(str(error))
        self.error = error


class NetworkTimeoutError(BackboneError):
    """HTTP timeout against Mostaql (C# taxonomy Timeout)."""


class HttpRequestError(BackboneError):
    """Transport-level request failure (C# taxonomy RequestFailed)."""


class HttpUnexpectedError(BackboneError):
    """Unexpected HTTP failure (C# taxonomy Unexpected)."""


class ParseException(BackboneError):
    """Mostaql page structure did not match the expected shape (PARSE-* codes)."""


class SchemaMismatchError(BackboneError):
    """Database schema version invalid or incompatible with this build (DB-003)."""


class StoreOperationError(BackboneError):
    """SQLite query/command failure inside the store (DB-002/DB-004)."""


class DiffStateError(BackboneError):
    """Known-state providers failed during diffing (DIFF-001)."""


def poll_listing_fetch_failed(cause: BaseException) -> DomainError:
    """POLL-001 — listing page fetch failed (C# PollErrors.ListingFetchFailed)."""
    return DomainError(
        code="POLL-001",
        internal_message=f"Failed to fetch listing page: {cause}",
        external_message="تعذر تحديث قائمة المشاريع.",
        fix_message="سيتم إعادة المحاولة عند دورة الفحص التالية.",
        cause=cause,
    )


def poll_cancelled() -> DomainError:
    """POLL-002 — poll cycle was cancelled (C# PollErrors.PollCancelled)."""
    return DomainError(
        code="POLL-002",
        internal_message="Poll cycle was cancelled.",
        external_message="تم إيقاف الفحص.",
    )


def diff_known_state_unavailable(cause: BaseException) -> DomainError:
    """DIFF-001 — known-state load for diffing failed (C# DiffErrors.KnownStateUnavailable)."""
    return DomainError(
        code="DIFF-001",
        internal_message=f"Failed to load known-state for diffing: {cause}",
        external_message="تعذر مقارنة المشاريع الجديدة بالمشاريع المعروفة.",
        cause=cause,
    )


def enrich_max_attempts_exhausted(
    project_id: int, attempts: int, last_error: DomainError
) -> DomainError:
    """ENRICH-001 — retry ladder exhausted (C# EnrichErrors.MaxAttemptsExhausted)."""
    return DomainError(
        code="ENRICH-001",
        internal_message=(
            f"Project {project_id} failed enrichment after {attempts} attempts. "
            f"Last error: {last_error.internal_message}"
        ),
        external_message="تعذر جلب تفاصيل المشروع بعد عدة محاولات.",
        fix_message="سيتم تجاهل هذا المشروع؛ قد يظهر مجدداً في الفحص التالي إن لم يتم حفظه.",
        cause=last_error.cause,
    )


def enrich_unexpected(project_id: int, cause: BaseException) -> DomainError:
    """ENRICH-002 — unexpected worker exception (C# EnrichErrors.Unexpected)."""
    return DomainError(
        code="ENRICH-002",
        internal_message=f"Unexpected exception while enriching project {project_id}: {cause}",
        external_message="حدث خطأ غير متوقع أثناء معالجة أحد المشاريع.",
        fix_message="تم تجاوز هذا المشروع والاستمرار في المعالجة؛ راجع سجل الأحداث للتفاصيل.",
        cause=cause,
    )


def store_query_failed(operation: str, cause: BaseException) -> DomainError:
    """DB-002 — store query failed (C# DatabaseErrors.QueryFailed)."""
    return DomainError(
        code="DB-002",
        internal_message=f"Query '{operation}' failed: {cause}",
        external_message="حدث خطأ أثناء الوصول إلى البيانات المحفوظة.",
        cause=cause,
    )


def schema_mismatch(current: int, expected: int) -> DomainError:
    """DB-003 — schema version mismatch (C# DatabaseErrors.SchemaInvalid + SchemaVersionMismatch).

    Combines both C# wordings: DB-003's prefix as the internal message frame, and the
    SchemaVersionMismatch sentence as the details payload.
    """
    return DomainError(
        code="DB-003",
        internal_message=(
            "Database schema is invalid or out of date: Database schema version "
            f"{current} does not match the version expected by this build ({expected}) "
            "and no migration path exists yet."
        ),
        external_message="قاعدة البيانات غير متوافقة مع هذا الإصدار من التطبيق.",
    )


def empty_html(parser_name: str) -> DomainError:
    """PARSE-001 — parser received empty HTML (C# ParseErrors.EmptyHtml)."""
    return DomainError(
        code="PARSE-001",
        internal_message=f"{parser_name}.Parse received empty HTML.",
        external_message="تعذر تحليل بيانات موقع مستقل.",
    )


def missing_title(project_id: int) -> DomainError:
    """PARSE-002 — detail title chain exhausted (C# ParseErrors.MissingTitle)."""
    return DomainError(
        code="PARSE-002",
        internal_message=(
            f"DetailParser.Parse could not locate a title (h1) for project {project_id}."
        ),
        external_message="تعذر تحليل بيانات موقع مستقل.",
    )


def no_project_rows() -> DomainError:
    """PARSE-003 — listing yielded zero project rows (C# ParseErrors.NoProjectRows).

    Message kept verbatim including the misleading ``div.project-card`` mention
    (plan §4.1 trap 1; tier-2 class is actually ``project-item``).
    """
    return DomainError(
        code="PARSE-003",
        internal_message=(
            "ListingParser.Parse could not locate any project rows "
            "(tr.project-row or div.project-card)."
        ),
        external_message="تعذر تحليل بيانات موقع مستقل.",
    )

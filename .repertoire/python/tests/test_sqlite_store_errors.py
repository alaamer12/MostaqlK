"""Hardening: forced sqlite3 failures must surface as StoreOperationError(DB-002).

C# failure-behavior spec (Infrastructure/Database/ProjectRepository.cs +
steering v1/tech/error-handling-and-resilience.md): every repository method
catches SqliteException and returns Result.Err carrying DatabaseErrors
.QueryFailed/.CommandFailed; UpsertDetailsAsync rethrows NON-SQLite exceptions
after fault logging (ProjectRepository.cs:246-250). The Python store mirrors
the taxonomy by raising StoreOperationError whose DomainError keeps code DB-002
and the exact C# operation name; non-sqlite3 exceptions escape unwrapped.

Fault injection is done by swapping ``store._conn`` through ``monkeypatch``
(connection-level failures) or wrapping the real connection with a passthrough
whose cursors explode (mid-transaction failures, exercising the rollback path).
"""

import asyncio
import sqlite3
from dataclasses import replace
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import pytest

from mostaql.errors import SchemaMismatchError, StoreOperationError
from mostaql.models import Owner, ProjectDetails, ProjectSkill, ProjectSummary
from mostaql.storage.schema import CURRENT_SCHEMA_VERSION
from mostaql.storage.sqlite_store import SQLiteStore

T0 = datetime(2026, 8, 25, 12, 0, 0, tzinfo=UTC)


def make_summary(project_id: int, **over: Any) -> ProjectSummary:
    base = ProjectSummary(
        project_id=project_id,
        title=f"مشروع {project_id}",
        url=f"https://mostaql.com/project/{project_id}",
        discovered_at=T0,
    )
    return replace(base, **over)


def make_details(project_id: int, **over: Any) -> ProjectDetails:
    base = ProjectDetails(
        project_id=project_id,
        title=f"مشروع {project_id}",
        url=f"https://mostaql.com/project/{project_id}",
        description="وصف المشروع بالكامل",
        discovered_at=T0,
        enriched_at=T0,
        owner=Owner(owner_id=7, name="مالك"),
        skills=[ProjectSkill(name="PHP")],
    )
    return replace(base, **over)


# ---------------------------------------------------------------------------
# Fault-injection stand-ins
# ---------------------------------------------------------------------------


class _ExplodingCursor:
    """Cursor whose execute/executemany always raise the injected error."""

    def __init__(self, error: Exception) -> None:
        self._error = error

    def execute(self, sql: str, *args: object) -> None:
        raise self._error

    def executemany(self, sql: str, seq: object) -> None:
        raise self._error


class LockedConnection:
    """Connection-level failure: every ``execute()`` raises immediately.

    For transactional methods this fails at ``BEGIN IMMEDIATE`` itself;
    for single-statement methods it fails at the statement — both funnel
    into the same ``except sqlite3.Error`` wrappers under test.
    """

    def __init__(self, error: Exception | None = None) -> None:
        self._error = error if error is not None else sqlite3.OperationalError("database is locked")

    def execute(self, sql: str, *args: object):
        raise self._error

    def cursor(self):
        return _ExplodingCursor(self._error)

    def rollback(self) -> None:
        pass

    def commit(self) -> None:
        pass


class MidTransactionFailure:
    """Passthrough until ``cursor()``: BEGIN succeeds, first cursor use raises.

    Drives the REAL begin/rollback/commit machinery so the atomicity of a
    failed multi-statement transaction is asserted against actual SQLite
    semantics (plan §11 phase 12: "no partial transactions").
    """

    def __init__(self, inner: sqlite3.Connection, error: Exception | None = None) -> None:
        self._inner = inner
        self._error = (
            error
            if error is not None
            else sqlite3.OperationalError("injected mid-transaction failure")
        )

    def execute(self, sql: str, *args: object):
        return self._inner.execute(sql, *args)

    def cursor(self):
        return _ExplodingCursor(self._error)

    def rollback(self) -> None:
        self._inner.rollback()

    def commit(self) -> None:
        self._inner.commit()


class NullRowConnection:
    """fetchone() -> None stand-in pinning CountTrackedAsync's defensive branch.

    Mirrors C# ProjectRepository.cs:563-566 where ``reader.ReadAsync() == false``
    yields Ok((0, 0)) instead of dereferencing the reader.
    """

    def __init__(self) -> None:
        self.last_sql = ""

    def execute(self, sql: str, *args: object):
        self.last_sql = sql
        return self

    def fetchone(self):
        return None

    def fetchall(self):
        return []


@pytest.fixture
async def store(tmp_path: Path):
    impl = SQLiteStore(tmp_path / "errors.db")
    yield impl
    impl.close()


# ---------------------------------------------------------------------------
# Transactional write paths
# ---------------------------------------------------------------------------


async def test_insert_summary_connection_error_raises_db002(store, monkeypatch) -> None:
    monkeypatch.setattr(store, "_conn", LockedConnection())

    with pytest.raises(StoreOperationError) as excinfo:
        await store.insert_summary(make_summary(1))

    error = excinfo.value.error
    assert error.code == "DB-002"
    assert "'InsertSummaryAsync'" in error.internal_message
    assert isinstance(excinfo.value.__cause__, sqlite3.OperationalError)

    # Recovery: once the injected fault is gone, the same operation succeeds.
    monkeypatch.undo()
    assert await store.insert_summary(make_summary(1)) is True


async def test_insert_summary_mid_transaction_failure_is_atomic(store, monkeypatch) -> None:
    assert await store.insert_summary(make_summary(1)) is True
    real_conn = store._conn
    monkeypatch.setattr(store, "_conn", MidTransactionFailure(real_conn))

    with pytest.raises(StoreOperationError):
        await store.insert_summary(make_summary(2))

    probe = sqlite3.connect(store.db_path)
    try:
        count = probe.execute("SELECT COUNT(*) FROM projects").fetchone()[0]
    finally:
        probe.close()
    assert count == 1, "failed summary insert leaked a partial row"


async def test_upsert_details_connection_error_raises_db002(store, monkeypatch) -> None:
    monkeypatch.setattr(store, "_conn", LockedConnection())

    with pytest.raises(StoreOperationError) as excinfo:
        await store.upsert_details(make_details(5))

    error = excinfo.value.error
    assert error.code == "DB-002"
    assert "'UpsertDetailsAsync'" in error.internal_message
    assert isinstance(excinfo.value.__cause__, sqlite3.OperationalError)


async def test_upsert_details_mid_transaction_failure_is_atomic(store, monkeypatch) -> None:
    real_conn = store._conn
    monkeypatch.setattr(store, "_conn", MidTransactionFailure(real_conn))

    with pytest.raises(StoreOperationError):
        await store.upsert_details(make_details(6))

    probe = sqlite3.connect(store.db_path)
    try:
        projects = probe.execute("SELECT COUNT(*) FROM projects").fetchone()[0]
        skills = probe.execute("SELECT COUNT(*) FROM project_skills").fetchone()[0]
        fts_rows = probe.execute("SELECT COUNT(*) FROM projects_fts").fetchone()[0]
    finally:
        probe.close()
    assert (projects, skills, fts_rows) == (0, 0, 0), "upsert rolled back partially"


async def test_upsert_details_non_sqlite_exception_rethrown_unwrapped(store, monkeypatch) -> None:
    """C# ProjectRepository.cs:246-250 analog: only SqliteException becomes a
    Result.Err; any other exception escapes raw (fault logging belongs to the
    worker layer's ENRICH-002 handling)."""

    class AlienFailure(RuntimeError):
        pass

    real_conn = store._conn
    monkeypatch.setattr(store, "_conn", MidTransactionFailure(real_conn, AlienFailure("boom")))

    with pytest.raises(AlienFailure) as excinfo:
        await store.upsert_details(make_details(7))

    assert not isinstance(excinfo.value, StoreOperationError)


async def test_owner_upsert_failure_raises_db002(store, monkeypatch) -> None:
    monkeypatch.setattr(store, "_conn", LockedConnection())

    with pytest.raises(StoreOperationError) as excinfo:
        await store.upsert_owner(Owner(owner_id=3, name="مالك"))

    error = excinfo.value.error
    assert error.code == "DB-002"
    assert "'UpsertAsync'" in error.internal_message


# ---------------------------------------------------------------------------
# Single-statement read/command paths
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("invoke", "operation"),
    [
        (lambda s: s.get_all_known_project_ids(), "GetAllKnownProjectIdsAsync"),
        (lambda s: s.add_to_backlog(9), "AddToBacklogAsync"),
        (lambda s: s.remove_from_backlog(9), "RemoveFromBacklogAsync"),
        (lambda s: s.get_backlog_ids(), "GetBacklogIdsAsync"),
        (lambda s: s.clean_old_backlog(days=30), "CleanOldBacklogAsync"),
        (lambda s: s.get_recent(limit=10), "GetRecentAsync"),
        (lambda s: s.count_tracked(), "CountTrackedAsync"),
        (lambda s: s.count_added_today(), "CountAddedTodayAsync"),
        (lambda s: s.search("تصميم"), "SearchAsync"),
        (lambda s: s.mark_as_read(9), "MarkAsReadAsync"),
        (lambda s: s.mark_all_as_read(), "MarkAllAsReadAsync"),
    ],
    ids=[
        "get-all-known",
        "add-backlog",
        "remove-backlog",
        "get-backlog",
        "clean-backlog",
        "get-recent",
        "count-tracked",
        "count-added-today",
        "search",
        "mark-as-read",
        "mark-all-as-read",
    ],
)
async def test_single_statement_failures_raise_db002(store, monkeypatch, invoke, operation) -> None:
    monkeypatch.setattr(store, "_conn", LockedConnection())

    with pytest.raises(StoreOperationError) as excinfo:
        await invoke(store)

    error = excinfo.value.error
    assert error.code == "DB-002"
    assert f"'{operation}'" in error.internal_message
    assert isinstance(excinfo.value.__cause__, sqlite3.Error)


async def test_count_tracked_empty_result_defensive_zero_pair(store, monkeypatch) -> None:
    """C# ProjectRepository.cs:563-566: no row read => Ok((0, 0))."""
    monkeypatch.setattr(store, "_conn", NullRowConnection())

    tracked, unread = await store.count_tracked()

    assert (tracked, unread) == (0, 0)


# ---------------------------------------------------------------------------
# Schema bootstrap on a corrupted existing file
# ---------------------------------------------------------------------------


def test_corrupted_existing_file_user_version_99_raises_db003(tmp_path: Path) -> None:
    db_path = tmp_path / "corrupted.db"
    raw = sqlite3.connect(db_path)
    try:
        raw.execute("PRAGMA user_version = 99;")
    finally:
        raw.close()

    with pytest.raises(SchemaMismatchError) as excinfo:
        SQLiteStore(db_path)

    error = excinfo.value.error
    assert error.code == "DB-003"
    assert "99" in error.internal_message
    assert str(CURRENT_SCHEMA_VERSION) in error.internal_message


# ---------------------------------------------------------------------------
# Concurrent-writer smoke: WAL + busy_timeout absorb cross-connection contention
# ---------------------------------------------------------------------------


async def test_concurrent_writers_complete_without_locked_error(tmp_path: Path) -> None:
    """Two stores over ONE db file writing simultaneously (bounded loop, small N).

    schema.connect sets PRAGMA journal_mode=WAL + busy_timeout=5000 per open,
    mirroring C# SqliteConnectionFactory; neither writer may leak an escaping
    "database is locked" StoreOperationError.
    """
    db_path = tmp_path / "shared.db"
    writer_a = SQLiteStore(db_path)
    writer_b = SQLiteStore(db_path)
    try:
        per_writer = 25

        async def churn(writer: SQLiteStore, first_id: int) -> list[bool]:
            results: list[bool] = []
            for offset in range(per_writer):
                project_id = first_id + offset
                results.append(await writer.insert_summary(make_summary(project_id)))
                await writer.add_to_backlog(project_id)
            return results

        outcomes = await asyncio.gather(churn(writer_a, 1), churn(writer_b, 100))

        expected_ids = {*range(1, 1 + per_writer), *range(100, 100 + per_writer)}
        assert all(outcomes[0]) and all(outcomes[1])
        known = await writer_a.get_all_known_project_ids()
        assert known == expected_ids
        backlog = await writer_b.get_backlog_ids()
        assert sorted(backlog) == sorted(expected_ids)
    finally:
        writer_a.close()
        writer_b.close()

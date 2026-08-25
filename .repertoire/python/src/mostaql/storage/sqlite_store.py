"""SQLite implementation of ProjectStore (C# ProjectRepository + OwnerRepository + FtsQueryService).

Every statement translates the C# SQL verbatim minus the dropped assets scope;
all operations execute via :func:`asyncio.to_thread` behind a single lock that
serializes access to one shared connection (single-writer discipline mirroring
WAL reality). Errors surface as typed :class:`StoreOperationError` carrying the
C# operation name in the DB-002 payload. No logging here — that belongs to the
worker layer.
"""

import asyncio
import sqlite3
import threading
from collections.abc import Iterator
from contextlib import contextmanager
from datetime import datetime
from pathlib import Path

from mostaql.errors import StoreOperationError, store_query_failed
from mostaql.models import EnrichmentStatus, Owner, ProjectDetails, ProjectSummary
from mostaql.storage.protocol import ProjectStore
from mostaql.storage.schema import (
    SchemaVerificationFlag,
    connect,
    ensure_schema,
)
from mostaql.storage.search import build_fts_query
from mostaql.storage.timestamps import current_utc, dotnet_o_format, parse_dotnet_o

__all__ = ["SQLiteStore"]

_INSERT_SUMMARY_SQL = """
INSERT OR IGNORE INTO projects
    (project_id, title, url, client_name, publish_time_number, publish_time_text,
     proposal_count, proposal_count_text, description,
     is_unread, enrichment_status, discovered_at)
VALUES
    (?, ?, ?, ?, ?, ?,
     ?, ?, ?,
     ?, ?, ?);
"""

_INSERT_SUMMARY_FTS_SQL = """
INSERT INTO projects_fts (project_id, title, description, skills)
VALUES (?, ?, ?, '');
"""

_UPSERT_DETAILS_SQL = """
INSERT INTO projects
    (project_id, title, url, client_name, publish_time_number, publish_time_text,
     proposal_count, proposal_count_text, description, budget, delivery_days,
     project_status, owner_id, enrichment_status, discovered_at, enriched_at)
VALUES
    (?, ?, ?, ?, ?, ?,
     ?, ?, ?, ?, ?,
     ?, ?, ?, ?, ?)
ON CONFLICT(project_id) DO UPDATE SET
    title = excluded.title,
    url = excluded.url,
    client_name = excluded.client_name,
    publish_time_number = CASE
        WHEN excluded.publish_time_number = 0
        THEN projects.publish_time_number
        ELSE excluded.publish_time_number
    END,
    publish_time_text = CASE
        WHEN excluded.publish_time_text = ''
        THEN projects.publish_time_text
        ELSE excluded.publish_time_text
    END,
    proposal_count = CASE
        WHEN excluded.proposal_count = 0
        THEN projects.proposal_count
        ELSE excluded.proposal_count
    END,
    proposal_count_text = CASE
        WHEN excluded.proposal_count_text = ''
        THEN projects.proposal_count_text
        ELSE excluded.proposal_count_text
    END,
    description = excluded.description,
    budget = excluded.budget,
    delivery_days = excluded.delivery_days,
    project_status = excluded.project_status,
    owner_id = excluded.owner_id,
    enrichment_status = excluded.enrichment_status,
    enriched_at = excluded.enriched_at;
"""

_DELETE_SKILLS_SQL = "DELETE FROM project_skills WHERE project_id = ?;"
_INSERT_SKILL_SQL = "INSERT INTO project_skills (project_id, name, url) VALUES (?, ?, ?);"
_DELETE_FTS_SQL = "DELETE FROM projects_fts WHERE project_id = ?;"
_INSERT_FTS_SQL = """
INSERT INTO projects_fts (project_id, title, description, skills)
VALUES (?, ?, ?, ?);
"""

_UPSERT_OWNER_SQL = """
INSERT INTO owners
    (owner_id, name, profile_url, avatar_url, rating, completed_projects_count,
     hiring_rate_percent, registered_at, open_projects_count,
     in_progress_projects_count, ongoing_communications_count, last_seen_at)
VALUES
    (?, ?, ?, ?, ?, ?,
     ?, ?, ?,
     ?, ?, ?)
ON CONFLICT(owner_id) DO UPDATE SET
    last_seen_at = excluded.last_seen_at,
    rating = excluded.rating,
    completed_projects_count = excluded.completed_projects_count,
    hiring_rate_percent = excluded.hiring_rate_percent,
    registered_at = excluded.registered_at,
    open_projects_count = excluded.open_projects_count,
    in_progress_projects_count = excluded.in_progress_projects_count,
    ongoing_communications_count = excluded.ongoing_communications_count;
"""

_GET_ALL_IDS_SQL = "SELECT project_id FROM projects;"

_GET_RECENT_SQL = """
SELECT p.project_id, p.title, p.url, p.client_name,
       p.publish_time_number, p.publish_time_text,
       p.proposal_count, p.proposal_count_text,
       p.description, p.budget, p.delivery_days,
       p.is_unread, p.enrichment_status, p.discovered_at, p.project_status,
       COALESCE((SELECT group_concat(name, ', ') FROM project_skills s
                 WHERE s.project_id = p.project_id), '') AS skills_text,
       p.enriched_at
FROM projects p
ORDER BY (p.enriched_at IS NULL) ASC, p.enriched_at DESC, p.discovered_at DESC
LIMIT ?;
"""

_COUNT_ADDED_TODAY_SQL = "SELECT COUNT(*) FROM projects WHERE date(discovered_at) = date('now');"

_ADD_TO_BACKLOG_SQL = "INSERT OR IGNORE INTO discovery_backlog (project_id) VALUES (?);"
_REMOVE_FROM_BACKLOG_SQL = "DELETE FROM discovery_backlog WHERE project_id = ?;"
_GET_BACKLOG_IDS_SQL = "SELECT project_id FROM discovery_backlog ORDER BY discovered_at ASC;"
_CLEAN_OLD_BACKLOG_SQL = (
    "DELETE FROM discovery_backlog WHERE discovered_at < datetime('now', '-' || ? || ' days');"
)

_COUNT_TRACKED_SQL = "SELECT COUNT(*), COALESCE(SUM(is_unread), 0) FROM projects;"
_MARK_AS_READ_SQL = "UPDATE projects SET is_unread = 0 WHERE project_id = ? AND is_unread = 1;"
_MARK_ALL_AS_READ_SQL = "UPDATE projects SET is_unread = 0 WHERE is_unread = 1;"

_SEARCH_SQL = """
SELECT p.project_id, p.title, p.url, p.client_name,
       p.publish_time_number, p.publish_time_text,
       p.proposal_count, p.proposal_count_text,
       p.is_unread, p.enrichment_status, p.discovered_at, p.description, p.budget, p.delivery_days,
       p.project_status,
       COALESCE((SELECT group_concat(name, ', ') FROM project_skills s
                 WHERE s.project_id = p.project_id), '') AS skills_text
FROM projects_fts f
JOIN projects p ON p.project_id = f.project_id
WHERE f.projects_fts MATCH ?
ORDER BY rank;
"""


class SQLiteStore(ProjectStore):
    """Concrete :class:`ProjectStore` over one shared WAL-mode SQLite connection."""

    def __init__(self, db_path: Path | str) -> None:
        path = Path(db_path)
        path.parent.mkdir(parents=True, exist_ok=True)
        self.db_path = path
        self._lock = threading.Lock()
        self._schema_flag = SchemaVerificationFlag()
        self._conn = connect(path)
        ensure_schema(self._conn, schema_verified_flag=self._schema_flag)

    def close(self) -> None:
        """Close the underlying connection (test/runtime teardown helper)."""
        with self._lock:
            self._conn.close()

    async def insert_summary(self, s: ProjectSummary) -> bool:
        return await asyncio.to_thread(self._insert_summary, s)

    async def upsert_details(self, d: ProjectDetails) -> None:
        await asyncio.to_thread(self._upsert_details, d)

    async def upsert_owner(self, o: Owner) -> None:
        await asyncio.to_thread(self._upsert_owner, o)

    async def get_all_known_project_ids(self) -> set[int]:
        return await asyncio.to_thread(self._get_all_known_project_ids)

    async def add_to_backlog(self, project_id: int) -> None:
        await asyncio.to_thread(self._add_to_backlog, project_id)

    async def remove_from_backlog(self, project_id: int) -> None:
        await asyncio.to_thread(self._remove_from_backlog, project_id)

    async def get_backlog_ids(self) -> list[int]:
        return await asyncio.to_thread(self._get_backlog_ids)

    async def clean_old_backlog(self, days: int = 30) -> int:
        return await asyncio.to_thread(self._clean_old_backlog, days)

    async def get_recent(self, limit: int) -> list[ProjectSummary]:
        return await asyncio.to_thread(self._get_recent, limit)

    async def mark_as_read(self, project_id: int) -> None:
        await asyncio.to_thread(self._mark_as_read, project_id)

    async def mark_all_as_read(self) -> None:
        await asyncio.to_thread(self._mark_all_as_read)

    async def count_added_today(self) -> int:
        return await asyncio.to_thread(self._count_added_today)

    async def count_tracked(self) -> tuple[int, int]:
        return await asyncio.to_thread(self._count_tracked)

    def search(self, query: str) -> list[ProjectSummary]:
        """C# ``FtsQueryService.SearchAsync`` verbatim: quoted-prefix MATCH, rank order."""
        enhanced_query = build_fts_query(query)
        if not enhanced_query:
            return []
        try:
            with self._lock:
                rows = self._conn.execute(_SEARCH_SQL, (enhanced_query,)).fetchall()
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("SearchAsync", exc)) from exc
        return [_summary_from_recent_row(row) for row in rows]

    @contextmanager
    def _transaction(self) -> Iterator[sqlite3.Cursor]:
        with self._lock:
            self._conn.execute("BEGIN IMMEDIATE;")
            try:
                yield self._conn.cursor()
            except BaseException:
                self._conn.rollback()
                raise
            else:
                self._conn.commit()

    def _insert_summary(self, s: ProjectSummary) -> bool:
        try:
            with self._transaction() as cur:
                cur.execute(
                    _INSERT_SUMMARY_SQL,
                    (
                        s.project_id,
                        s.title,
                        s.url,
                        s.client_name,
                        s.publish_time_number,
                        s.publish_time_text,
                        s.proposal_count,
                        s.proposal_count_text,
                        s.description,
                        int(s.is_unread),
                        s.enrichment_status.value,
                        dotnet_o_format(s.discovered_at),
                    ),
                )
                is_new_row = cur.rowcount > 0
                if is_new_row:
                    cur.execute(
                        _INSERT_SUMMARY_FTS_SQL,
                        (s.project_id, s.title, s.description),
                    )
            return is_new_row
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("InsertSummaryAsync", exc)) from exc

    def _upsert_details(self, d: ProjectDetails) -> None:
        try:
            skills_text = " ".join(skill.name for skill in d.skills)
            with self._transaction() as cur:
                cur.execute(
                    _UPSERT_DETAILS_SQL,
                    (
                        d.project_id,
                        d.title,
                        d.url,
                        d.owner.name,
                        d.publish_time_number,
                        d.publish_time_text,
                        d.proposal_count,
                        d.proposal_count_text,
                        d.description,
                        d.budget,
                        d.delivery_days,
                        d.project_status,
                        None if d.owner.owner_id == 0 else d.owner.owner_id,
                        d.enrichment_status.value,
                        dotnet_o_format(d.discovered_at),
                        None if d.enriched_at is None else dotnet_o_format(d.enriched_at),
                    ),
                )
                cur.execute(_DELETE_SKILLS_SQL, (d.project_id,))
                cur.executemany(
                    _INSERT_SKILL_SQL,
                    [(d.project_id, skill.name, skill.url) for skill in d.skills],
                )
                cur.execute(_DELETE_FTS_SQL, (d.project_id,))
                cur.execute(
                    _INSERT_FTS_SQL,
                    (d.project_id, d.title, d.description, skills_text),
                )
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("UpsertDetailsAsync", exc)) from exc

    def _upsert_owner(self, o: Owner) -> None:
        try:
            with self._lock:
                self._conn.execute(
                    _UPSERT_OWNER_SQL,
                    (
                        o.owner_id,
                        o.name,
                        o.profile_url,
                        o.avatar_url,
                        o.rating,
                        o.completed_projects_count,
                        o.hiring_rate_percent,
                        o.registered_at,
                        o.open_projects_count,
                        o.in_progress_projects_count,
                        o.ongoing_communications_count,
                        dotnet_o_format(current_utc()),
                    ),
                )
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("UpsertAsync", exc)) from exc

    def _get_all_known_project_ids(self) -> set[int]:
        try:
            with self._lock:
                rows = self._conn.execute(_GET_ALL_IDS_SQL).fetchall()
        except sqlite3.Error as exc:
            raise StoreOperationError(
                store_query_failed("GetAllKnownProjectIdsAsync", exc)
            ) from exc
        return {int(row["project_id"]) for row in rows}

    def _add_to_backlog(self, project_id: int) -> None:
        try:
            with self._lock:
                self._conn.execute(_ADD_TO_BACKLOG_SQL, (project_id,))
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("AddToBacklogAsync", exc)) from exc

    def _remove_from_backlog(self, project_id: int) -> None:
        try:
            with self._lock:
                self._conn.execute(_REMOVE_FROM_BACKLOG_SQL, (project_id,))
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("RemoveFromBacklogAsync", exc)) from exc

    def _get_backlog_ids(self) -> list[int]:
        try:
            with self._lock:
                rows = self._conn.execute(_GET_BACKLOG_IDS_SQL).fetchall()
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("GetBacklogIdsAsync", exc)) from exc
        return [int(row["project_id"]) for row in rows]

    def _clean_old_backlog(self, days: int) -> int:
        try:
            with self._lock:
                cursor = self._conn.execute(_CLEAN_OLD_BACKLOG_SQL, (days,))
                deleted = cursor.rowcount
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("CleanOldBacklogAsync", exc)) from exc
        return max(deleted, 0)

    def _get_recent(self, limit: int) -> list[ProjectSummary]:
        try:
            with self._lock:
                rows = self._conn.execute(_GET_RECENT_SQL, (limit,)).fetchall()
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("GetRecentAsync", exc)) from exc
        return [_summary_from_recent_row(row) for row in rows]

    def _mark_as_read(self, project_id: int) -> None:
        try:
            with self._lock:
                self._conn.execute(_MARK_AS_READ_SQL, (project_id,))
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("MarkAsReadAsync", exc)) from exc

    def _mark_all_as_read(self) -> None:
        try:
            with self._lock:
                self._conn.execute(_MARK_ALL_AS_READ_SQL)
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("MarkAllAsReadAsync", exc)) from exc

    def _count_added_today(self) -> int:
        try:
            with self._lock:
                row = self._conn.execute(_COUNT_ADDED_TODAY_SQL).fetchone()
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("CountAddedTodayAsync", exc)) from exc
        return 0 if row is None else int(row[0])

    def _count_tracked(self) -> tuple[int, int]:
        try:
            with self._lock:
                row = self._conn.execute(_COUNT_TRACKED_SQL).fetchone()
        except sqlite3.Error as exc:
            raise StoreOperationError(store_query_failed("CountTrackedAsync", exc)) from exc
        if row is None:
            return (0, 0)
        return (int(row[0]), int(row[1]))


def _summary_from_recent_row(row: sqlite3.Row) -> ProjectSummary:
    """Map a GetRecent/search row BY NAME (storage trap 14).

    Search rows carry no ``enriched_at`` column at all — absent keys map to
    None exactly like C# leaving the property unset.
    """
    columns = tuple(row.keys())
    has_enriched_at = "enriched_at" in columns
    return ProjectSummary(
        project_id=int(row["project_id"]),
        title=_req_str(row, "title"),
        url=_req_str(row, "url"),
        client_name=_opt_str(row, "client_name") or "",
        publish_time_number=_num_or(row, "publish_time_number", 0),
        publish_time_text=_opt_str(row, "publish_time_text") or "",
        proposal_count=_num_or(row, "proposal_count", 0),
        proposal_count_text=_opt_str(row, "proposal_count_text") or "",
        description=_opt_str(row, "description") or "",
        budget=_opt_str(row, "budget"),
        delivery_days=None if row["delivery_days"] is None else int(row["delivery_days"]),
        skills_text=_opt_str(row, "skills_text") or "",
        project_status=_opt_str(row, "project_status"),
        is_unread=_bool_int(row, "is_unread"),
        enrichment_status=EnrichmentStatus(str(row["enrichment_status"])),
        discovered_at=parse_dotnet_o(_req_str(row, "discovered_at")),
        enriched_at=None if not has_enriched_at else _ts_or_none(row, "enriched_at"),
    )


def _req_str(row: sqlite3.Row, key: str) -> str:
    return str(row[key])


def _opt_str(row: sqlite3.Row, key: str) -> str | None:
    value = row[key]
    return None if value is None else str(value)


def _num_or(row: sqlite3.Row, key: str, default: int) -> int:
    value = row[key]
    return default if value is None else int(value)


def _bool_int(row: sqlite3.Row, key: str) -> bool:
    value = row[key]
    return False if value is None else int(value) != 0


def _ts_or_none(row: sqlite3.Row, key: str) -> datetime | None:
    value = row[key]
    return None if value is None else parse_dotnet_o(str(value))

"""Faithful in-memory ProjectStore fake for the contract suite (plan §13).

Replicates the SQLite implementation's ordering, sentinel, and duplicate
semantics using the same frozen models: TEXT-ordering is mimicked by sorting on
the same formatted string keys the store would persist (dotnet "O" stamps,
CURRENT_TIMESTAMP-style backlog stamps), and search approximates FTS5 quoted-
prefix matching with token prefix hits in insertion order (rank order is NOT
replicated — rank-dependent assertions are SQLite-arm only).
"""

import re
from dataclasses import replace
from datetime import UTC, datetime, timedelta

from mostaql.models import Owner, ProjectDetails, ProjectSummary
from mostaql.storage.protocol import ProjectStore

__all__ = ["InMemoryStore"]

_TOKEN_RE = re.compile(r"\w+")

_STAMP_FORMAT = "%Y%m%d%H%M%S%f"


def _stamp(value: datetime) -> str:
    return value.strftime(_STAMP_FORMAT)


class InMemoryStore(ProjectStore):
    """Contract-twin of :class:`~mostaql.storage.sqlite_store.SQLiteStore`."""

    def __init__(self) -> None:
        self.projects: dict[int, ProjectSummary] = {}
        self.skills: dict[int, list[tuple[str, str | None]]] = {}
        self.owners: dict[int, Owner] = {}
        self.owner_last_seen: dict[int, str] = {}
        self.fts: dict[int, tuple[str, str, str]] = {}
        self.backlog: dict[int, str] = {}

    async def insert_summary(self, s: ProjectSummary) -> bool:
        if s.project_id in self.projects:
            return False
        self.projects[s.project_id] = s
        self.fts[s.project_id] = (s.title, s.description, "")
        return True

    async def upsert_details(self, d: ProjectDetails) -> None:
        existing = self.projects.get(d.project_id)
        if existing is None:
            base = d
        else:
            base = replace(
                existing,
                title=d.title,
                url=d.url,
                client_name=d.owner.name,
                publish_time_number=(
                    existing.publish_time_number
                    if d.publish_time_number == 0
                    else d.publish_time_number
                ),
                publish_time_text=(
                    existing.publish_time_text if d.publish_time_text == "" else d.publish_time_text
                ),
                proposal_count=(
                    existing.proposal_count if d.proposal_count == 0 else d.proposal_count
                ),
                proposal_count_text=(
                    existing.proposal_count_text
                    if d.proposal_count_text == ""
                    else d.proposal_count_text
                ),
                description=d.description,
                budget=d.budget,
                delivery_days=d.delivery_days,
                project_status=d.project_status,
                enrichment_status=d.enrichment_status,
                enriched_at=d.enriched_at,
            )
        self.projects[d.project_id] = base
        self.skills[d.project_id] = [(s.name, s.url) for s in d.skills]
        self.fts[d.project_id] = (
            d.title,
            d.description,
            " ".join(s.name for s in d.skills),
        )

    async def upsert_owner(self, o: Owner) -> None:
        last_seen = _backlog_stamp()
        existing = self.owners.get(o.owner_id)
        if existing is None:
            self.owners[o.owner_id] = o
            self.owner_last_seen[o.owner_id] = last_seen
            return
        merged = replace(
            existing,
            rating=o.rating,
            completed_projects_count=o.completed_projects_count,
            hiring_rate_percent=o.hiring_rate_percent,
            registered_at=o.registered_at,
            open_projects_count=o.open_projects_count,
            in_progress_projects_count=o.in_progress_projects_count,
            ongoing_communications_count=o.ongoing_communications_count,
        )
        self.owners[o.owner_id] = merged
        self.owner_last_seen[o.owner_id] = last_seen

    async def get_all_known_project_ids(self) -> set[int]:
        return set(self.projects)

    async def add_to_backlog(self, project_id: int) -> None:
        if project_id not in self.backlog:
            self.backlog[project_id] = _backlog_stamp()

    async def remove_from_backlog(self, project_id: int) -> None:
        self.backlog.pop(project_id, None)

    async def get_backlog_ids(self) -> list[int]:
        ordered = sorted(self.backlog.items(), key=lambda item: item[1])
        return [project_id for project_id, _ in ordered]

    async def clean_old_backlog(self, days: int = 30) -> int:
        cutoff = _backlog_cutoff(days)
        stale = [pid for pid, stamp in self.backlog.items() if stamp < cutoff]
        for pid in stale:
            del self.backlog[pid]
        return len(stale)

    async def get_recent(self, limit: int) -> list[ProjectSummary]:
        rows = list(self.projects.values())
        rows.sort(key=lambda r: _stamp(r.discovered_at), reverse=True)
        rows.sort(
            key=lambda r: "" if r.enriched_at is None else _stamp(r.enriched_at),
            reverse=True,
        )
        rows.sort(key=lambda r: r.enriched_at is None)
        return [self._with_skills_text(r) for r in rows[:limit]]

    async def mark_as_read(self, project_id: int) -> None:
        row = self.projects.get(project_id)
        if row is not None and row.is_unread:
            self.projects[project_id] = replace(row, is_unread=False)

    async def mark_all_as_read(self) -> None:
        for pid, row in list(self.projects.items()):
            if row.is_unread:
                self.projects[pid] = replace(row, is_unread=False)

    async def count_added_today(self) -> int:
        today = datetime.now(UTC).date()
        return sum(
            1 for row in self.projects.values() if row.discovered_at.astimezone(UTC).date() == today
        )

    async def count_tracked(self) -> tuple[int, int]:
        tracked = len(self.projects)
        unread = sum(1 for row in self.projects.values() if row.is_unread)
        return (tracked, unread)

    def search(self, query: str) -> list[ProjectSummary]:
        terms = [t.lower() for t in query.split(" ") if t.strip()]
        if not terms:
            return []
        hits: list[int] = []
        for pid, (title, description, skills) in self.fts.items():
            tokens = {t.lower() for t in _TOKEN_RE.findall(f"{title} {description} {skills}")}
            if all(any(token.startswith(term) for token in tokens) for term in terms):
                hits.append(pid)
        return [
            replace(self._with_skills_text(self.projects[pid]), enriched_at=None) for pid in hits
        ]

    def _with_skills_text(self, row: ProjectSummary) -> ProjectSummary:
        skill_names = self.skills.get(row.project_id, [])
        skills_text = ", ".join(name for name, _ in skill_names)
        return replace(row, skills_text=skills_text)


def _backlog_stamp() -> str:
    """Mimic SQLite CURRENT_TIMESTAMP: UTC, second precision, space separator."""
    return datetime.now(UTC).strftime("%Y-%m-%d %H:%M:%S")


def _backlog_cutoff(days: int) -> str:
    return (datetime.now(UTC) - timedelta(days=days)).strftime("%Y-%m-%d %H:%M:%S")

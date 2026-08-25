"""ProjectStore Protocol: the business-facing storage abstraction (plan §8 contract).

Pipeline code sees only this Protocol; the SQLite implementation lives in
``mostaql.storage.sqlite_store`` (forbidden import there — plan §10 contract).
C# ``Result<T>`` outcomes become typed returns/exceptions: ``Ok(false)`` from
``InsertSummaryAsync`` (duplicate-not-error) becomes a plain ``False`` return,
and every C# ``Result.Err`` (DB-002 QueryFailed / DB-004 CommandFailed) becomes
a raised :class:`StoreOperationError`.
"""

from typing import Protocol

from mostaql.models import Owner, ProjectDetails, ProjectSummary

__all__ = ["ProjectStore"]


class ProjectStore(Protocol):
    """Minimal persistence surface mirroring ProjectRepository + OwnerRepository."""

    async def insert_summary(self, s: ProjectSummary) -> bool:
        """C# ``ProjectRepository.InsertSummaryAsync``: write-once discovery insert.

        Transactional INSERT OR IGNORE into ``projects`` plus an FTS row
        ``(project_id,title,description,'')`` only when a new row landed.
        Returns True for a new row; False means duplicate (not an error).
        Raises :class:`StoreOperationError` on query failure.
        """
        ...

    async def upsert_details(self, d: ProjectDetails) -> None:
        """C# ``ProjectRepository.UpsertDetailsAsync``: single enrichment transaction.

        ON CONFLICT DO UPDATE with per-column sentinel guards (0 / '' keep old),
        ``discovered_at`` never updated on conflict, skills delete+reinsert, and
        FTS delete+reinsert with ``' '``-joined skill names. The one exception to
        the write-once policy. Raises :class:`StoreOperationError` on failure.
        """
        ...

    async def upsert_owner(self, o: Owner) -> None:
        """C# ``OwnerRepository.UpsertAsync``: selective owner upsert.

        Identity columns (name/profile_url/avatar_url) are INSERT-only; conflict
        refreshes last_seen_at + rating + counts + registered_at only.
        Raises :class:`StoreOperationError` on failure.
        """
        ...

    async def get_all_known_project_ids(self) -> set[int]:
        """C# ``GetAllKnownProjectIdsAsync``: SELECT ALL project_id as a set."""
        ...

    async def add_to_backlog(self, project_id: int) -> None:
        """C# ``AddToBacklogAsync``: INSERT OR IGNORE into discovery_backlog."""
        ...

    async def remove_from_backlog(self, project_id: int) -> None:
        """C# ``RemoveFromBacklogAsync``: DELETE by id; normal return when absent."""
        ...

    async def get_backlog_ids(self) -> list[int]:
        """C# ``GetBacklogIdsAsync``: ids ordered by discovered_at ASC (re-hydration)."""
        ...

    async def clean_old_backlog(self, days: int = 30) -> int:
        """C# ``CleanOldBacklogAsync``: delete backlog rows older than days; return count."""
        ...

    async def get_recent(self, limit: int) -> list[ProjectSummary]:
        """C# ``GetRecentAsync`` ordering: pending-last, then enriched_at DESC, discovered DESC.

        Summaries carry ``skills_text`` from a ``group_concat(name, ', ')``
        subquery and are mapped BY NAME (storage trap 14).
        """
        ...

    async def mark_as_read(self, project_id: int) -> None:
        """C# ``MarkAsReadAsync``: guarded UPDATE ... WHERE is_unread = 1."""
        ...

    async def mark_all_as_read(self) -> None:
        """C# ``MarkAllAsReadAsync``: UPDATE projects SET is_unread = 0 WHERE is_unread = 1."""
        ...

    async def count_added_today(self) -> int:
        """C# ``CountAddedTodayAsync``: COUNT(*) WHERE date(discovered_at) = date('now')."""
        ...

    async def count_tracked(self) -> tuple[int, int]:
        """C# ``CountTrackedAsync``: (COUNT(*), COALESCE(SUM(is_unread), 0))."""
        ...

    def search(self, query: str) -> list[ProjectSummary]:
        """C# ``FtsQueryService.SearchAsync``: FTS MATCH with per-term quoted prefixes.

        Sync helper; whitespace-only query returns [] without touching storage.
        Result summaries carry NO enriched_at (null), like C#. Ordered by rank.
        """
        ...

"""Diff engine: fresh listing vs known state (plan §8 contract).

Port of C# ``Services/Pipeline/DiffEngine/*``: ``DiffEngine.cs``, ``IKnownStateProvider.cs``,
``SqliteCommittedProvider.cs``, ``InFlightSetProvider.cs``. Pure set logic over two
known-state sources -- the committed SQLite store (permanent backstop) and the in-flight
tracker (memory window). Output preserves the polled order.
"""

from collections.abc import Sequence
from dataclasses import dataclass, field
from typing import Protocol

from mostaql.errors import DiffStateError, diff_known_state_unavailable
from mostaql.models import ProjectSummary
from mostaql.pipeline.inflight import InFlightTracker
from mostaql.storage.protocol import ProjectStore

__all__ = [
    "CommittedIdsProvider",
    "DiffEngine",
    "DiffResult",
    "InFlightSetProvider",
    "KnownStateProvider",
]


class KnownStateProvider(Protocol):
    """Source of already-known project IDs (C# ``IKnownStateProvider``)."""

    async def known_project_ids(self) -> set[int]:
        """Return every project ID considered known right now."""
        ...


class CommittedIdsProvider:
    """Known-state provider backed by the persisted projects table.

    C# ``SqliteCommittedProvider`` -- the permanent backstop guaranteeing a project is
    never re-enriched once committed. Store failures surface as exceptions so
    :class:`DiffEngine` can wrap them into DIFF-001 and fail the poll cycle gracefully.
    """

    def __init__(self, store: ProjectStore) -> None:
        self._store = store

    async def known_project_ids(self) -> set[int]:
        return await self._store.get_all_known_project_ids()


class InFlightSetProvider:
    """Known-state provider backed by :class:`InFlightTracker`.

    C# ``InFlightSetProvider`` -- covers IDs already enqueued / being enriched but not yet
    committed to SQLite.
    """

    def __init__(self, tracker: InFlightTracker) -> None:
        self._tracker = tracker

    async def known_project_ids(self) -> set[int]:
        return self._tracker.snapshot()


@dataclass(frozen=True, slots=True)
class DiffResult:
    """Partitioned polled IDs (C# ``DiffResult``); both lists preserve polled order."""

    new_project_ids: list[int] = field(default_factory=list)
    already_known_ids: list[int] = field(default_factory=list)


class DiffEngine:
    """Compares a freshly polled listing against committed + in-flight state.

    C# origin: ``Services/Pipeline/DiffEngine/DiffEngine.cs``.
    """

    def __init__(
        self, committed_provider: KnownStateProvider, in_flight_provider: KnownStateProvider
    ) -> None:
        self._committed_provider = committed_provider
        self._in_flight_provider = in_flight_provider

    async def diff(self, polled: Sequence[ProjectSummary]) -> DiffResult:
        """Partition ``polled`` into new vs already-known IDs, preserving order.

        Provider failure raises :class:`DiffStateError` carrying DIFF-001
        (C# ``DiffErrors.KnownStateUnavailable``); caller cancellation propagates.
        """
        try:
            committed = await self._committed_provider.known_project_ids()
            in_flight = await self._in_flight_provider.known_project_ids()
        except Exception as exc:
            raise DiffStateError(diff_known_state_unavailable(exc)) from exc

        known = committed | in_flight
        result = DiffResult()
        for project in polled:
            if project.project_id in known:
                result.already_known_ids.append(project.project_id)
            else:
                result.new_project_ids.append(project.project_id)
        return result

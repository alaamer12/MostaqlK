"""DiffEngine + provider tests: set logic, ordering, DIFF-001 wrap (C# DiffEngine/*)."""

import asyncio
from datetime import UTC, datetime

import pytest

from mostaql.errors import DiffStateError, StoreOperationError, diff_known_state_unavailable
from mostaql.models import ProjectSummary
from mostaql.pipeline.diff import (
    CommittedIdsProvider,
    DiffEngine,
    InFlightSetProvider,
)
from mostaql.pipeline.inflight import InFlightTracker


def summary(pid: int) -> ProjectSummary:
    return ProjectSummary(
        project_id=pid, title=f"t{pid}", discovered_at=datetime(2026, 1, 1, tzinfo=UTC)
    )


class FakeStore:
    """Satisfies the ProjectStore slice consumed by CommittedIdsProvider."""

    def __init__(self, ids: set[int]) -> None:
        self.ids = ids

    async def get_all_known_project_ids(self) -> set[int]:
        return set(self.ids)


class ExplodingProvider:
    async def known_project_ids(self) -> set[int]:
        raise StoreOperationError(diff_known_state_unavailable(RuntimeError("db down")))


class CancelledProvider:
    async def known_project_ids(self) -> set[int]:
        raise asyncio.CancelledError


def engine(committed: set[int], tracker: InFlightTracker | None = None) -> DiffEngine:
    active = tracker if tracker is not None else InFlightTracker()
    return DiffEngine(CommittedIdsProvider(FakeStore(committed)), InFlightSetProvider(active))


async def test_new_and_known_partition_preserves_polled_order() -> None:
    tracker = InFlightTracker()
    tracker.try_mark_in_flight(4)
    eng = engine({2}, tracker)

    result = await eng.diff([summary(1), summary(2), summary(3), summary(4)])

    assert result.new_project_ids == [1, 3]
    assert result.already_known_ids == [2, 4]


async def test_committed_union_in_flight_excluded_from_new() -> None:
    tracker = InFlightTracker()
    tracker.try_mark_in_flight(30)
    eng = engine({10, 20}, tracker)

    result = await eng.diff([summary(10), summary(20), summary(30), summary(40)])

    assert result.new_project_ids == [40]
    assert sorted(result.already_known_ids) == [10, 20, 30]


async def test_duplicate_polled_entries_preserved_verbatim() -> None:
    result = await engine(set()).diff([summary(7), summary(7)])
    assert result.new_project_ids == [7, 7]


async def test_empty_listing_yields_empty_result() -> None:
    result = await engine({1}).diff([])
    assert result.new_project_ids == []
    assert result.already_known_ids == []


async def test_provider_failure_wrapped_as_diff_state_error() -> None:
    eng = DiffEngine(ExplodingProvider(), InFlightSetProvider(InFlightTracker()))

    with pytest.raises(DiffStateError) as err:
        await eng.diff([summary(1)])

    assert err.value.error.code == "DIFF-001"
    assert isinstance(err.value.error.cause, StoreOperationError)


async def test_cancellation_through_provider_propagates_unwrapped() -> None:
    eng = DiffEngine(CancelledProvider(), InFlightSetProvider(InFlightTracker()))

    with pytest.raises(asyncio.CancelledError):
        await eng.diff([summary(1)])


async def test_committed_ids_provider_delegates_to_store() -> None:
    provider = CommittedIdsProvider(FakeStore({5, 6}))
    assert await provider.known_project_ids() == {5, 6}


async def test_in_flight_set_provider_reflects_tracker_snapshot_isolation() -> None:
    tracker = InFlightTracker()
    tracker.try_mark_in_flight(3)
    provider = InFlightSetProvider(tracker)

    first = await provider.known_project_ids()
    tracker.try_mark_in_flight(4)

    assert first == {3}
    assert await provider.known_project_ids() == {3, 4}

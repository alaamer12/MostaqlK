"""InFlightTracker unit tests: atomic claim semantics (C# InFlightTracker.cs)."""

from mostaql.pipeline.inflight import InFlightTracker


async def test_first_claim_wins_and_duplicate_claimant_rejected() -> None:
    tracker = InFlightTracker()

    assert tracker.try_mark_in_flight(11) is True
    assert tracker.try_mark_in_flight(11) is False

    assert tracker.is_in_flight(11) is True


async def test_claims_are_per_project_id() -> None:
    tracker = InFlightTracker()

    assert tracker.try_mark_in_flight(1) is True
    assert tracker.try_mark_in_flight(2) is True
    assert tracker.is_in_flight(1)
    assert tracker.is_in_flight(2)


async def test_mark_complete_releases_and_allows_reclaim() -> None:
    tracker = InFlightTracker()
    tracker.try_mark_in_flight(42)

    tracker.mark_complete(42)

    assert tracker.is_in_flight(42) is False
    assert tracker.try_mark_in_flight(42) is True


async def test_mark_complete_of_unknown_id_is_a_no_op() -> None:
    tracker = InFlightTracker()
    tracker.mark_complete(999)
    assert tracker.snapshot() == set()


async def test_snapshot_returns_isolated_copy() -> None:
    tracker = InFlightTracker()
    tracker.try_mark_in_flight(7)
    tracker.try_mark_in_flight(8)

    snap = tracker.snapshot()
    snap.add(99)
    snap.discard(7)

    assert tracker.snapshot() == {7, 8}


async def test_release_after_snapshot_does_not_mutate_the_copy() -> None:
    tracker = InFlightTracker()
    tracker.try_mark_in_flight(5)
    snap = tracker.snapshot()

    tracker.mark_complete(5)

    assert snap == {5}
    assert tracker.snapshot() == set()

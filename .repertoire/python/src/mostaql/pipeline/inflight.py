"""In-memory window of project IDs currently being enriched (plan §8 contract).

Port of C# ``Services/Pipeline/InFlightTracker.cs`` (a ``ConcurrentDictionary<long, byte>``).
The SQLite-committed store is the permanent backstop; this tracker only covers the
in-memory window so the same ID is never enqueued twice while still in-flight.
"""

__all__ = ["InFlightTracker"]


class InFlightTracker:
    """Tracks in-flight project IDs with atomic test-and-add claims (C# ``InFlightTracker``).

    Single-event-loop assumption: claim/release/contains run without await points, so each
    sequence is atomic against every other coroutine on the loop (the Python analogue of
    ``ConcurrentDictionary`` thread-safety for our asyncio-only process).
    """

    def __init__(self) -> None:
        self._in_flight: set[int] = set()

    def try_mark_in_flight(self, project_id: int) -> bool:
        """Atomically claim an ID; False means another actor already holds it.

        C# ``ConcurrentDictionary.TryAdd`` -- losing claimants are skipped upstream
        (race with another poll cycle or worker).
        """
        if project_id in self._in_flight:
            return False
        self._in_flight.add(project_id)
        return True

    def mark_complete(self, project_id: int) -> None:
        """Release an ID (C# ``TryRemove``). Called in the worker ``finally`` ALWAYS,
        success or failure, so no ID can get stuck permanently in-flight."""
        self._in_flight.discard(project_id)

    def is_in_flight(self, project_id: int) -> bool:
        """Whether the ID is currently claimed (C# ``ContainsKey``)."""
        return project_id in self._in_flight

    def snapshot(self) -> set[int]:
        """Copy of the current in-flight set (C# ``Snapshot``); isolated from later mutation."""
        return set(self._in_flight)

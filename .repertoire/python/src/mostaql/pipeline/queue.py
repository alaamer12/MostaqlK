"""FIFO discovery channel of project IDs awaiting enrichment (plan §8 contract).

Port of C# ``Services/Pipeline/DiscoveryQueue.cs`` (a ``Channel<long>`` with
``UnboundedChannelOptions { SingleReader = false, SingleWriter = false }``): producers
(poller) and consumers (workers) run concurrently without extra locking; ``drain_all``
mirrors ``Reader.ReadAllAsync`` semantics -- buffered items are still delivered after
``complete()``, and the iterator ends only once completed AND drained.
"""

import asyncio
from collections import deque
from collections.abc import AsyncIterator

__all__ = ["DiscoveryQueue"]


class DiscoveryQueue:
    """Unbounded multi-producer/multi-consumer FIFO of project IDs (C# ``DiscoveryQueue``)."""

    def __init__(self) -> None:
        self._items: deque[int] = deque()
        self._completed = False
        self._state_changed = asyncio.Event()

    @property
    def count(self) -> int:
        """Approximate number of IDs queued awaiting enrichment (C# ``Channel.Reader.Count``).

        Used by status subscribers to detect a draining backlog.
        """
        return len(self._items)

    async def enqueue(self, project_id: int) -> None:
        """Append one ID to the queue (C# ``Writer.WriteAsync`` on an unbounded channel).

        Raises :class:`RuntimeError` after :meth:`complete` -- the Python analogue of
        the C# ``ChannelClosedException`` a late writer would receive.
        """
        if self._completed:
            raise RuntimeError("DiscoveryQueue.EnqueueAsync: the queue has been completed.")
        self._items.append(project_id)
        self._state_changed.set()

    def complete(self) -> None:
        """Mark the queue closed; idempotent (C# ``Writer.TryComplete``).

        Already-buffered items remain deliverable to every drain.
        """
        self._completed = True
        self._state_changed.set()

    async def drain_all(self, cancel: asyncio.Event) -> AsyncIterator[int]:
        """Yield items until completed-and-drained or ``cancel`` fires (C# ``ReadAllAsync``).

        Multi-consumer safe: each item goes to exactly one consumer, in FIFO order.
        Buffered items are delivered even when ``complete()`` was already called;
        cancellation takes effect once the buffer is empty (pool stop relies on this
        to drain what is already queued before workers exit).
        """
        while True:
            while self._items:
                yield self._items.popleft()
            if self._completed or cancel.is_set():
                return
            push_wait = asyncio.ensure_future(self._state_changed.wait())
            cancel_wait = asyncio.ensure_future(cancel.wait())
            try:
                await asyncio.wait({push_wait, cancel_wait}, return_when=asyncio.FIRST_COMPLETED)
            finally:
                for task in (push_wait, cancel_wait):
                    task.cancel()
                await asyncio.gather(push_wait, cancel_wait, return_exceptions=True)
            self._state_changed.clear()

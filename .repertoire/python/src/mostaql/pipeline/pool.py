"""Fixed-size pool of enrichment workers over one shared discovery queue (plan §8 contract).

Port of C# ``Services/Pipeline/WorkerPool/WorkerPool.cs`` minus the dropped live-reconfigure
quirk (plan §12 ledger 4 -- pool size is fixed at start): startup re-hydrates the persistent
backlog into the queue, prunes entries older than 30 days fire-and-forget, then spawns the
configured worker count. Stop completes the queue, sets cancellation and awaits the workers.
"""

import asyncio
from contextlib import suppress
from typing import Protocol

from mostaql.models import ProjectDetails
from mostaql.pipeline.enrich import EnrichmentService
from mostaql.pipeline.inflight import InFlightTracker
from mostaql.pipeline.queue import DiscoveryQueue
from mostaql.pipeline.worker import RETRY_DELAYS_SECONDS, EnrichmentWorker, SleepFn
from mostaql.storage.protocol import ProjectStore

__all__ = ["WorkerPool", "WorkerPoolEvents"]

_BACKLOG_PRUNE_DAYS = 30


class WorkerPoolEvents(Protocol):
    """Callbacks consumed by the pool and its workers (plan §8 ``PipelineEvents`` slice).

    Replaces the C# ``GlobalAppStatusService`` radar/worker-card surface (ledger 11);
    the runtime composition root provides one object implementing every callback
    (Wave D), which structurally satisfies this slice and :class:`WorkerEvents`.
    """

    def on_project_discovered(self, project_id: int, title: str) -> None:
        """Radar detection pulse for a discovered or re-hydrated project."""
        ...

    def on_queue_count_changed(self, count: int) -> None:
        """Discovery-queue depth changed (C# ``GlobalAppStatusService.UpdateQueueCount``)."""
        ...

    def on_worker_state(self, worker_id: int, state: str) -> None:
        """Radar segment state change for one worker."""
        ...

    def on_enriched(self, details: ProjectDetails) -> None:
        """A project finished enrichment; extensible notification hook point."""
        ...


class WorkerPool:
    """Owns a fixed set of :class:`EnrichmentWorker` tasks draining the shared queue.

    C# origin: ``Services/Pipeline/WorkerPool/WorkerPool.cs``.
    """

    WORKER_COUNT = 3

    def __init__(
        self,
        queue: DiscoveryQueue,
        enrichment: EnrichmentService,
        tracker: InFlightTracker,
        store: ProjectStore,
        events: WorkerPoolEvents,
        worker_count: int = WORKER_COUNT,
        sleep: SleepFn = asyncio.sleep,
        retry_delays: tuple[float, ...] = RETRY_DELAYS_SECONDS,
    ) -> None:
        self._queue = queue
        self._enrichment = enrichment
        self._tracker = tracker
        self._store = store
        self._events = events
        self._worker_count = max(1, worker_count)
        self._sleep = sleep
        self._retry_delays = retry_delays
        self._worker_tasks: list[asyncio.Task[None]] = []
        self._prune_task: asyncio.Task[None] | None = None
        self._cancel: asyncio.Event | None = None

    async def start(self, cancel: asyncio.Event) -> None:
        """Re-hydrate the backlog, kick off pruning, spawn the workers (C# ``StartAsync``).

        Re-hydrated IDs are claimed in-flight first so a concurrent poll cannot double-
        enqueue them; each fires a discovery pulse with an empty title because the radar
        has no summary yet (C# ``NotifyProjectDiscovered(projectId)``).
        """
        self._cancel = cancel
        backlog_ids = await self._store.get_backlog_ids()
        for project_id in backlog_ids:
            if self._tracker.try_mark_in_flight(project_id):
                await self._queue.enqueue(project_id)
                self._events.on_project_discovered(project_id, "")
        self._events.on_queue_count_changed(self._queue.count)

        # Cleanup: prune very old backlog entries to prevent bloat (C# discards this task;
        # documented fire-and-forget exception to the fixed-concurrency rule).
        self._prune_task = asyncio.ensure_future(self._prune_old_backlog())

        for worker_id in range(self._worker_count):
            worker = EnrichmentWorker(
                worker_id,
                self._queue,
                self._enrichment,
                self._tracker,
                self._store,
                self._events,
                retry_delays=self._retry_delays,
                sleep=self._sleep,
            )
            self._worker_tasks.append(asyncio.ensure_future(worker.run(cancel)))

    async def stop(self) -> None:
        """Complete the queue, cancel the loops, await every worker (C# ``StopAsync``).

        Buffered items are still drained by the workers before they exit -- the queue's
        ReadAllAsync semantics guarantee no already-discovered ID is dropped on shutdown.
        """
        self._queue.complete()
        if self._cancel is not None:
            self._cancel.set()
        await asyncio.gather(*self._worker_tasks, return_exceptions=True)
        if self._prune_task is not None:
            self._prune_task.cancel()
            with suppress(asyncio.CancelledError):
                await self._prune_task

    async def _prune_old_backlog(self) -> None:
        with suppress(Exception):
            await self._store.clean_old_backlog(_BACKLOG_PRUNE_DAYS)

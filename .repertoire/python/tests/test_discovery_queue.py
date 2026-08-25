"""DiscoveryQueue tests: Channel<long> semantics (C# DiscoveryQueue.cs)."""

import asyncio

import pytest

from mostaql.pipeline.queue import DiscoveryQueue


async def collect(queue: DiscoveryQueue, cancel: asyncio.Event) -> list[int]:
    out: list[int] = []
    async for item in queue.drain_all(cancel):
        out.append(item)
    return out


async def test_fifo_order_preserved() -> None:
    queue = DiscoveryQueue()
    for pid in (5, 3, 9, 1):
        await queue.enqueue(pid)
    cancel = asyncio.Event()
    consumer = asyncio.ensure_future(collect(queue, cancel))
    await asyncio.sleep(0.05)  # buffer fully consumed; consumer parked
    cancel.set()

    assert await asyncio.wait_for(consumer, timeout=3.0) == [5, 3, 9, 1]


async def test_complete_then_drain_delivers_buffered_items() -> None:
    queue = DiscoveryQueue()
    for pid in (1, 2, 3):
        await queue.enqueue(pid)
    queue.complete()

    received = await collect(queue, asyncio.Event())

    assert received == [1, 2, 3]
    assert queue.count == 0


async def test_drain_waits_for_new_items_then_finishes_on_complete() -> None:
    queue = DiscoveryQueue()
    cancel = asyncio.Event()
    consumer = asyncio.ensure_future(collect(queue, cancel))
    await asyncio.sleep(0.01)

    await queue.enqueue(10)
    await queue.enqueue(20)
    await asyncio.sleep(0.01)
    queue.complete()

    assert await asyncio.wait_for(consumer, timeout=3.0) == [10, 20]


async def test_cancel_stops_an_idle_drain() -> None:
    queue = DiscoveryQueue()
    cancel = asyncio.Event()
    consumer = asyncio.ensure_future(collect(queue, cancel))
    await asyncio.sleep(0.01)

    cancel.set()
    assert await asyncio.wait_for(consumer, timeout=3.0) == []


async def test_multi_consumer_each_item_delivered_exactly_once() -> None:
    queue = DiscoveryQueue()
    cancel = asyncio.Event()
    results = [asyncio.ensure_future(collect(queue, cancel)) for _ in range(3)]
    await asyncio.sleep(0.01)

    for pid in range(30):
        await queue.enqueue(pid)
    queue.complete()

    merged: list[int] = []
    for task in results:
        merged.extend(await asyncio.wait_for(task, timeout=5.0))

    assert sorted(merged) == list(range(30))


async def test_count_tracks_buffered_items() -> None:
    queue = DiscoveryQueue()
    assert queue.count == 0

    await queue.enqueue(1)
    await queue.enqueue(2)
    assert queue.count == 2


async def test_complete_is_idempotent() -> None:
    queue = DiscoveryQueue()
    queue.complete()
    queue.complete()
    assert queue.count == 0


async def test_enqueue_after_complete_is_rejected() -> None:
    queue = DiscoveryQueue()
    queue.complete()

    with pytest.raises(RuntimeError):
        await queue.enqueue(4)


async def test_items_enqueued_after_consumer_blocked_are_received() -> None:
    """Producer/consumer interleaving must not lose wakeups (late producer wins)."""
    queue = DiscoveryQueue()
    cancel = asyncio.Event()
    consumer = asyncio.ensure_future(collect(queue, cancel))
    await asyncio.sleep(0.05)  # consumer now parked inside the race wait

    for pid in (100, 200):
        await queue.enqueue(pid)
        await asyncio.sleep(0.01)
    queue.complete()

    assert await asyncio.wait_for(consumer, timeout=3.0) == [100, 200]

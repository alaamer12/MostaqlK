# Concurrency model

[← Back to wiki home](../../base/tech/README.md)

## Table of contents
- [What needs to be thread-safe](#what-needs-to-be-thread-safe)
- [In-flight set implementation](#in-flight-set-implementation)
- [Lifecycle of an ID through the set](#lifecycle-of-an-id-through-the-set)
- [DB-level backstop](#db-level-backstop)
- [Transaction boundary](#transaction-boundary)
- [Restart / crash behavior](#restart--crash-behavior)

## What needs to be thread-safe

Two things run concurrently and touch shared state:

1. The **poll loop** — periodically reads the in-flight set (via the [diff engine](diff-engine.md)) and adds newly enqueued IDs to it.
2. The **worker pool** ([worker-pool-and-rate-limiter.md](worker-pool-and-rate-limiter.md)) — multiple async workers remove IDs from the in-flight set as they finish (success or permanent failure).

Both can happen at genuinely the same moment — a poll firing while three workers are mid-enrichment. The in-flight set is the one piece of mutable shared state in the whole pipeline, so it's the only thing that needs deliberate concurrency handling; everything else (DB writes, HTTP calls) is either already transactional (DB) or inherently isolated (each HTTP call is independent).

## In-flight set implementation

.NET has a built-in concurrent set-like collection suited to this: `ConcurrentDictionary<long, byte>` used as a set (there's no `ConcurrentHashSet<T>` in the BCL, so `ConcurrentDictionary` with a discarded value is the idiomatic substitute), or `System.Collections.Concurrent` primitives wrapped in a small typed class so the rest of the codebase doesn't deal with the dictionary-as-set awkwardness directly.

```csharp
public sealed class InFlightTracker
{
    private readonly ConcurrentDictionary<long, byte> _ids = new();

    public bool TryMarkInFlight(long projectId) =>
        _ids.TryAdd(projectId, 0);

    public void MarkComplete(long projectId) =>
        _ids.TryRemove(projectId, out _);

    public HashSet<long> Snapshot() =>
        _ids.Keys.ToHashSet(); // used by DiffEngine's InFlightSetProvider
}
```

`TryMarkInFlight` returning `false` (already present) matters: it means two near-simultaneous poll cycles that both see the same "new" ID can't both enqueue it — whichever calls `TryMarkInFlight` first wins, the second is naturally rejected without needing a separate lock.

## Lifecycle of an ID through the set

```
Diff engine reports "unseen"
        │
        ▼
InFlightTracker.TryMarkInFlight(id)
        │
   (false → already in flight, drop; true → proceed)
        │
        ▼
Enqueue onto worker queue
        │
        ▼
Worker dequeues, fetches detail, parses, commits to DB
        │
        ▼
finally { InFlightTracker.MarkComplete(id) }   ← always runs, success or failure
```

The `finally` is not optional — a worker that throws mid-enrichment (network error, parse error) must still release the ID, or it becomes permanently invisible to future polls (stuck as "in flight" forever with no worker actually processing it). See [error-handling-and-resilience.md](error-handling-and-resilience.md) for what happens after release on failure (retry vs. drop).

## DB-level backstop

Even with correct in-flight tracking, treat `project_id` as `PRIMARY KEY` in SQLite and insert with `INSERT OR IGNORE` (or the equivalent conflict-do-nothing clause). This is deliberate defense in depth, not redundant caution:

- A bug in the tracker, a restart mid-cycle, or a future change to the diff engine's wiring could theoretically reintroduce a duplicate attempt.
- With the constraint in place, that duplicate attempt fails cleanly and silently rather than throwing an unhandled exception or corrupting the row count.
- Cost of having this backstop is effectively zero; cost of not having it is a possible unhandled crash from a scenario that's otherwise just "this one row didn't need to be inserted twice."

## Transaction boundary

A single enrichment's DB writes (the `projects` row insert, plus whatever else v2+ adds — skills, assets, FTS index) should commit as **one transaction**. Partial writes (e.g. the project row commits but the skills rows fail) would leave the DB in a state the rest of the app doesn't expect. `MarkComplete` on the in-flight tracker should only be called after that transaction successfully commits — not before, and not from inside the transaction itself.

## Restart / crash behavior

`InFlightTracker` is in-memory only, by design — it does not need to persist across restarts. On a fresh process start, the set is empty, which is exactly correct: anything that was mid-enrichment but not committed before the previous process ended is, by definition, not in the DB either, so the next poll's diff engine will correctly see it as `unseen` again and reprocess it. This is the same mechanism that makes the [backlog-handling model](../../base/product/architecture-pipeline.md#backlog-handling-no-special-cold-start) work without a dedicated resume/recovery path — a crash mid-backlog just looks like a large `unseen` set on the next poll, handled identically to any other backlog.

# pipeline-worker.md — Wave C Report

**Agent:** pipeline-worker · **Date:** 2026-08-25 · **Scope:** plan §3.B rows / §8 pipeline signatures / §12 ledger 1–4

## Goal

Implement the complete orchestration layer (`mostaql.pipeline`) behaviorally faithful to
`Services/Pipeline/*` (C# V1), fully typed, asyncio-native, no HTML/SQL dialects, plus the
full unit + integration test suite and green quality gates.

## What was done

Read in order: AGENTS.md → all 10 C# sources (PollService, DiscoveryQueue, InFlightTracker,
EnrichmentService, TokenBucketRateLimiter, DiffEngine + 3 providers, WorkerPool,
EnrichmentWorker) → refactor-python-plan.md in full → existing Python surfaces (errors,
models, storage.protocol, sqlite_store, http.client, scraper, diagnostics).

### Source modules (9)

| File | Contents | C# origin | Cov |
|---|---|---|---|
| `pipeline/ratelimit.py` | `TokenBucketRateLimiter`: capacity=rpm(≥1), refill rpm/60·s⁻¹ (×10 unsafe), 1s safe spacing, lazy refill-on-acquire, computed-wait loop with 10ms floor, monotonic clock (ledger #2 documented), `reconfigure` clamps tokens, `available_tokens` refill-on-read, injected clock seam | TokenBucketRateLimiter.cs | 100% |
| `pipeline/queue.py` | `DiscoveryQueue`: deque FIFO, multi-producer/consumer, `drain_all(cancel)` mirrors `ReadAllAsync` (buffered delivered after `complete()`; cancel only stops *waiting*), idempotent `complete()`, `count`, post-complete enqueue raises RuntimeError (ChannelClosedException analog) | DiscoveryQueue.cs | 100% |
| `pipeline/inflight.py` | `InFlightTracker`: atomic test-and-add claim (single-loop, no await points), release/contains/snapshot-copy | InFlightTracker.cs | 100% |
| `pipeline/diff.py` | `KnownStateProvider` Protocol, `CommittedIdsProvider(store)`, `InFlightSetProvider(tracker)`, `DiffResult`, `DiffEngine.diff` order-preserving; provider failure → `DiffStateError(DIFF-001)`; CancelledError propagates unwrapped | DiffEngine/*.cs | 100% |
| `pipeline/enrich.py` | local `DetailFetcher` Protocol + `EnrichmentService.enrich` = token → fetch (asset path dropped per ledger #6) | EnrichmentService.cs | 100% |
| `pipeline/worker.py` | `RETRY_DELAYS_SECONDS=(60,120,240,480,900)`; `EnrichmentWorker.run/_process`: ladder with injected sleep, ENRICH-001 exhaustion returns normally (row stays Pending, backlog removed), ENRICH-002 unexpected containment (backlog KEPT), owner gating `name!=""or id>0`, UpsertFailed swallow+continue→`on_enriched`, finally releases tracker + delayed idle timer guarded by cancel | WorkerPool/EnrichmentWorker.cs | 98% |
| `pipeline/pool.py` | `WorkerPool.start`: backlog re-hydration (claim→enqueue→discovered-with-empty-title), fire-and-forget `clean_old_backlog(30)` (suppress(Exception)), fixed worker spawn; `stop`: complete→cancel→gather(return_exceptions) + prune-task reaping | WorkerPool.cs | 97% |
| `pipeline/poller.py` | `PollServiceStatus` enum; `PollService`: immediate first poll unless paused; tick race (interval vs check-now vs stop vs parent-cancel); check-now bypasses pause; interval re-read per tick clamp≥1; status machine POLLING→BACKLOG_DRAINING/IDLE, ERROR on failure; Fail()-equivalent inside `poll_once` exactly once per failing cycle ("FetchListing"/"Diff"/"Unexpected") then loop adds "Cycle" summary log; first-wins summaries map; add_backlog→insert_summary→enqueue ordering; MissingSummary MARK; GlobalAppStatus progress fields dropped as UI-only | PollService.cs | 94% |
| `pipeline/__init__.py` | Public re-exports of all classes/constants/enums | — | 100% |

Every public symbol docstring cites its C# origin file.

### Tests (9 files)

`tests/test_ratelimit.py` (11 — fake clock + patched `_SLEEP` seam proving computed waits:
28s fractional-refill ladder, 3s fast-mode, spacing 1s, zero-spacing, shrink-clamp, 10ms
floor, cancellation, lazy capped refill) · `tests/test_inflight.py` (6) ·
`tests/test_diff.py` (8 — ordering, union exclusion, DIFF-001 wrap, cancellation) ·
`tests/test_discovery_queue.py` (8 — FIFO, complete-then-drain, multi-consumer exactly-once,
count, idempotent complete, closed-enqueue rejection, late-producer wakeup) ·
`tests/test_enrichment_service.py` (4 — token-before-fetch ordering, passthrough) ·
`tests/test_worker_pool.py` (12 — ladder timing, ENRICH-001 Pending+backlog-cleared,
ENRICH-002 backlog-kept+survivor, owner gating, DB-002 swallow w/ on_enriched still firing,
rehydration, stop-drains-buffered, bounded concurrency ≤ workers, idle timer, direct drain
exit) · `tests/test_poll_service.py` (13 — immediate-first-poll, pause/check-now bypass,
interval re-read (2s→1s gap contrast) + ≥1s clamp, duplicate-race skip, store-ordering
timeline, BACKLOG_DRAINING, ERROR + recovery next tick, DIFF-001, POLL-001 wrap,
MissingSummary, parent-cancel stop, check-now idempotence) ·
`tests/integration/test_pipeline_e2e.py` (2) + `tests/integration/__init__.py`.

E2E uses REAL SQLiteStore(tmp_path), REAL MostaqlScraper/PageFetcher over httpx.MockTransport
serving `regression/fixtures/listing/table_rows.html` + `detail/owner_hash.html`, real limiter
(rpm=600 unsafe), real queue/workers/poller: asserts 3 projects land Enriched, skills persisted
(`search("Illustrator")` → skills_text), backlog emptied, no in-flight residue, graceful stop
within timeout, concurrency ≤ workers+listing. Crash-restart: seeded summary+backlog row
5005, fresh SQLiteStore instance, always-404 transport, micro ladder `(0,)*5` → ENRICH path
exhausts instantly, row stays **Pending**, backlog cleared, worker survives.

## Verification (trimmed gate output, from `.repertoire\python`)

```
uv sync                      OK (mostaql installed)
uv run ruff format .         71 files left unchanged
uv run ruff check .          All checks passed!
uv run mypy src              Success: no issues found in 43 source files   (--strict, zero ignores)
uv run xenon src -b B        (silent — no block above B)
uv run lint-imports          Contracts: 3 kept, 0 broken
uv run pytest -q --cov       444 passed · TOTAL 94.28%  (≥85 required)
```

Scratch debug scripts used during root-causing were deleted (`scratch/` emptied).

## Deviations (with justification)

1. **`WorkerPool.__init__` gained `retry_delays=RETRY_DELAYS_SECONDS`.** Goal G listed only
   `worker_count`/`sleep`; plan §8's pool signature had neither. The mandated e2e scenario
   requires "micro retry_delays → exhausts instantly", so pass-through injection is the only
   way to satisfy it without monkeypatching module constants. Consistent extension of the
   goal's own extension of §8.
2. **Local structural event Protocols instead of runtime's `PipelineEvents`** (runtime.py =
   Wave D, untouchable): `worker.WorkerEvents`, `pool.WorkerPoolEvents` (superset satisfying
   WorkerEvents), `poller.PollEvents`. One object implementing all slices composes cleanly.
3. **`on_queue_count_changed` added to the events surface.** Absent from plan §8's
   PipelineEvents listing but explicitly mandated throughout goals F/G/H (mirrors C#
   `GlobalAppStatusService.UpdateQueueCount`). Wave D must implement it.
4. **Two localized `cast(FailureLike, …)` adapters** (`_log_failure` helpers in worker.py /
   poller.py): frozen `DomainError` structurally satisfies diagnostics' mutable-field
   `FailureLike` protocol; fixing the Protocol would require editing `interaction_log.py`,
   outside my allowed file set. Casts ≠ ignores; mypy --strict stays ignore-free.
5. **Poller `clock` parameter accepted but unused** — signature-mandated; documented as a
   reserved parity seam.
6. **`DiscoveryQueue.enqueue` after `complete()` raises RuntimeError** — unspecified by goal;
   chosen as the faithful analog of C# `ChannelClosedException`.

## Proposed ledger additions (plan §12)

| # | Difference | Reason |
|---|---|---|
| 12 | `UpdateQueueCount` preserved as `PipelineEvents.on_queue_count_changed` (missing from §8 contract listing) | goal-mandated radar parity |
| 13 | Retry ladder injectable into pool/workers (`retry_delays` tuple) vs C# hard-coded array | testability + instant-exhaust restart scenarios |
| 14 | Late `enqueue` after `complete()` raises `RuntimeError` | mirrors C# `ChannelClosedException` |

## Concerns

- **Wave D contract:** runtime must supply ONE events object implementing every slice
  callback including `on_queue_count_changed`; otherwise composition will fail structurally.
- Cross-test fake imports (`from test_worker_pool import …`) rely on pytest's default
  prepend import mode with flat test layout — revisit if importmode/rootdir changes.
- `storage/sqlite_store.py` sits at 84% (unreached StoreOperationError branches, Wave B
  scope); does not threaten the 85% wave target (total 94.28%) but worth a look in hardening.
- Idle-state timers are fire-and-forget by design (documented exception); they are cancel-
  guarded but not awaited anywhere — acceptable per constraint wording, noted for reviewers.

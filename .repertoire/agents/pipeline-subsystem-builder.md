# pipeline-subsystem-builder — Step 3: Pipeline Subsystem Implementation

## Goal
Implement the real end-to-end discovery pipeline (poll → parse → diff → enqueue →
rate-limited enrich → commit → notify), replacing stub bodies, per
`.repertoire/.steering/base/system-components.md`.

## Files Modified
- `Infrastructure/Http/HttpErrors.cs` — added `Timeout`, `NotFound`, `Unexpected`, `ParseFailed` factories.
- `Infrastructure/Http/MostaqlScraper.cs` — real `HttpClient` GETs (15s timeout via linked CTS) for
  listing (`https://mostaql.com/projects`) and detail (`https://mostaql.com/project/{id}`) pages,
  parsed via the pre-existing `ListingParser`/`DetailParser`; all failure modes mapped to
  `Result<T>.Err` via `HttpErrors`.
- `Services/Pipeline/TokenBucketRateLimiter.cs` — implemented `WaitForTokenAsync` using a lazy
  refill-on-acquire pattern (no background timer): each acquisition tops up `_tokens` from
  elapsed wall-clock time under a `lock`, consumes a token if available, otherwise computes
  the wait for the next fractional token and polls again. Chosen over a background `Timer` for
  simplicity — no extra thread/lifetime to manage, and semantics are identical for a bucket
  this small (default capacity currently 10 @ 1/s from `MauiProgram`; docs specify a default
  of 2 requests/min, capacity == rate — that config value should be revisited when
  configuration loading (Settings) is wired up, out of scope for this step).
- `Services/Pipeline/EnrichmentService.cs` — implemented `EnrichAsync`: await rate limiter token,
  delegate to `IProjectScraper.FetchProjectDetailsAsync`.
- `Services/Pipeline/PollService.cs` — implemented `StartAsync`/`PollOnceAsync`: `PeriodicTimer`
  loop at a 30s interval (per docs default) with an immediate first poll on start; each cycle
  acquires a rate-limiter token, fetches the listing, diffs via `DiffEngine`, and for each
  unseen ID atomically marks in-flight then writes to `DiscoveryQueue`. All expected failures
  (scraper/diff errors) are swallowed into a `Result<int>.Err` and the loop continues on the next
  tick; `OperationCanceledException` always propagates.
- `Services/Pipeline/DiffEngine/DiffEngine.cs` — implemented `DiffAsync`: unions
  `SqliteCommittedProvider` + `InFlightSetProvider` known-ID sets, partitions candidates into
  `NewProjectIds` / `AlreadyKnownProjectIds`; provider exceptions caught and wrapped via
  `DiffErrors.KnownStateUnavailable` instead of crashing the poll loop.
- `Services/Pipeline/DiffEngine/SqliteCommittedProvider.cs` — implemented by delegating to the
  already-declared `IProjectRepository.GetAllKnownProjectIdsAsync` (Step 4's job to back with a
  real `SELECT project_id FROM projects` query); a `Result.Err` from the repository (e.g. missing
  table before migrations exist) is turned into a thrown exception that `DiffEngine` catches and
  reports gracefully rather than crashing.
- `Services/Pipeline/WorkerPool/EnrichmentWorker.cs` — implemented `RunAsync`/`ProcessAsync`:
  reads IDs from `DiscoveryQueue`, retries `IEnrichmentService.EnrichAsync` on failure with
  1m/2m/4m/8m/15m backoff (max 5 attempts, per docs), then calls `IProjectRepository
  .UpsertDetailsAsync` (commit) and `INotificationDispatcher.NotifyNewProjectsAsync` (notify),
  tolerating `NotImplementedException` from both since Steps 4/5 aren't built yet.
  `InFlightTracker.MarkComplete` is always called from a `finally`.
- `Services/Pipeline/WorkerPool/EnrichErrors.cs` — new file, `ENRICH-001` permanent-failure error.
- `Services/Pipeline/WorkerPool/WorkerPool.cs` — updated constructor to also take
  `IProjectRepository` and `INotificationDispatcher`, threading them into each spawned
  `EnrichmentWorker`.
- `App.xaml.cs` — added an `IServiceProvider` constructor parameter (MAUI resolves `App` via DI,
  so this is honored automatically) and start `IPollService`/`WorkerPool` as fire-and-forget
  background loops off a single `CancellationTokenSource` owned by `App`, since MAUI has no
  ASP.NET-style `IHostedService`. Chosen over registering a custom hosted-service abstraction
  in `MauiProgram` because both services already expose idiomatic `StartAsync(CancellationToken)`
  methods — no extra abstraction needed for V1.

## Not Modified (used as-is per instructions)
- `Infrastructure/Http/Parsers/ListingParser.cs`, `DetailParser.cs` — untouched.
- `Core/Result.cs`, `Core/DomainError.cs` — untouched, used as designed.
- `Services/Pipeline/InFlightTracker.cs`, `Services/Pipeline/DiscoveryQueue.cs` — found already
  fully implemented from a prior session; left as-is.
- `MauiProgram.cs` — no changes needed; existing DI registrations already cover every new
  constructor parameter added in this step (all dependencies were already singletons).

## Build Status
`dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` — **succeeded, 0 errors**
(35 pre-existing warnings: NU1903 SQLitePCLRaw advisory, obsolete `Frame` XAML usages,
`CS8625` nullability in already-implemented parser files, `MVVMTK0045` AOT warnings — none
introduced by this step).

## Verification Approach
Given the ToS/rate-limiting risk of hitting mostaql.com directly, verification was done via
**option (b)**: confirming the project compiles cleanly with 0 errors across all new pipeline
code and that every new/changed class's constructor DI shape is satisfied by the existing
`MauiProgram` registrations (traced manually — every new constructor parameter added in this
step, e.g. `WorkerPool`'s `IProjectRepository`/`INotificationDispatcher`, `PollService`'s
`InFlightTracker`/`TokenBucketRateLimiter`, was already registered as a singleton before this
step). No live network requests were made. No throwaway mock-scraper script was created/left
behind.

## Integration Gaps (depend on later steps)
- **Step 4 (`ProjectRepository`/DB schema)**: `SqliteCommittedProvider` and `EnrichmentWorker`'s
  commit path both call into `IProjectRepository` methods that still `throw
  NotImplementedException()` (`GetAllKnownProjectIdsAsync`, `UpsertDetailsAsync`). Both call
  sites tolerate this today (caught/wrapped into graceful `Result` failures or explicitly
  caught `NotImplementedException`), so the pipeline runs end-to-end without a working DB, but
  no project is ever actually persisted or permanently deduplicated until Step 4 lands.
- **Step 5 (`NotificationDispatcher`)**: `EnrichmentWorker` calls
  `INotificationDispatcher.NotifyNewProjectsAsync`, which still throws
  `NotImplementedException()` under the hood (`WindowsToastSender`/`NotificationDispatcher` not
  implemented); this is caught and tolerated in the worker, so no toast is ever sent yet.
- **Configuration**: `poll_interval_seconds`, `max_requests_per_minute`, and
  `max_concurrent_detail_fetches` are currently hardcoded (30s, capacity 10 @ 1 token/s in
  `MauiProgram`, `WorkerCount = 3`) rather than sourced from the Settings feature — wiring these
  to user configuration is a natural follow-up once the Settings persistence layer exists.

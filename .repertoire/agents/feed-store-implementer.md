# feed-store-implementer — SourceFeedStore read model + optimistic interactions + settle re-timing

Mission (plan `rosy-pine-puffin.md` Steps 1, 3 (VM parts), 4-settle): make the render loop read
memory only — the DB feeds memory — with rows that always exist (Provisional → Confirmed / Stale /
Broken + retry), deferred persistence writes, and the `ContentSettle` mount re-timed off the
snapshot-applied paint-complete frame. Skills used: `frontend-ui-engineering` scope discipline via
the steering docs (`base/structure.md`, `v3/animation-safety.md`, `UNITS.md` read in full per task
brief; no `.cursor/skills` run script executed in this subagent env — followed the mandated doc set
instead).

## What was built

### 1. `Services/SourceFeedStore.cs` (new unit)
- `FeedRowState {Provisional, Confirmed, Stale, Broken}`, `SnapshotFreshness {Cold, Fresh, Stale}`,
  `CountsSnapshot`, `FeedItem`, immutable `FeedSnapshot {Source, Items, Counts, Generation,
  DateTimeOffset StampedAt (the plan's "Instant" — .NET has no Instant), Freshness, Query, Error}`.
- Build-next-then-pointer-swap: per-source `volatile FeedSnapshot` field; `Peek(source)` is a
  lock-free O(1) read + pure stamp arithmetic (`ReclassifiedAt`) — never awaits, never queries.
- `RefreshAsync(source, query?, ct)`: runs the existing bounded repo queries off the click path;
  **60 ms quiet-window burst coalescing → one query pass + one batched apply**; per-source monotonic
  `Generation`; supersession cancels the in-flight pass token AND the apply guard drops its results
  even if the provider ignores cancellation; per-pass linked CTS (caller token + run token) fed to
  the repository `CommandDefinition` ct plumbing. Cold requests publish the Provisional shell
  **synchronously** (5 skeleton rows, gen 0) so the UI is never blank/blocked.
- `ISourceFeedDataProvider` seam + `RepositorySourceFeedDataProvider` (`IProjectRepository` + FTS
  delegate — keeps the store Dapper/Sqlite/MAUI-free and linkable headlessly).
- Stale-while-revalidate: a refine pass dims existing rows to `Stale` first (same generation), then
  lands `Confirmed`. Failure ⇒ `Broken` batch: prior rows retained dimmed + honest `Error` copy, or
  a single `Broken` placeholder when there is nothing to retain — never a missing item.
- `ApplyOptimisticRead` (card tap): same-generation overlay + badge decrement, with a
  pending-overlay set so a refine racing the deferred DB write never flickers unread back.
- Warm-peer prefetch (`PrefetchPeersAsync`): after an INTERACTIVE apply, refreshes the other
  registered sources sequentially at lowest priority (30 s TTL skip on hot peers); the chain CTS is
  cancelled when any newer interactive refresh supersedes it.
- `event Action<FeedSnapshot> SnapshotUpdated` — dispatcher-marshalled via injected seam (the app
  posts with the teardown-guarded `Application.Current.Dispatcher` pattern; identity in tests).

### 2. `Core/Platform/DeferredPreferences.cs` (new unit)
Ordered **last-wins queue** (`key → newest write`), fire-and-forget background drain, one-at-a-time
in request order; `Set`/`SetGlobal` wrappers over `ISettingsRepository`; `FlushAsync()` drains +
awaits the in-flight write (awaited by Settings Save now; app-teardown call site is a parent
follow-up — `App.xaml.cs` is the click-path agent's file this wave). Thread-safety assumption
documented on the type (FilePreferences/`IPreferences` are lock-guarded; reads stay synchronous on
the UI thread, only the WRITE defers). Faults go to `FaultSink`.

### 3. `ProjectFeedViewModel` — now a snapshot RENDERER
- Repaints ONLY from `SnapshotUpdated` (generation-checked; stale batches dropped) + synchronous
  `Peek` paints. `Projects.Clear()` on switch is GONE (diff-apply: reuse-by-id / move / insert).
- **Double-trigger removal**: the fire-and-forget `async SwitchSourceAsync` race chain (rail event
  AND projection-changed event each starting `Projects.Clear()` + awaited DB reloads concurrently)
  is replaced by ONE synchronous idempotent `SwitchSource(source)` (second signal from any entry
  point returns instantly at the `source == _source` guard; the store collapses the refresh burst).
  A narrow, sync projection listener remains ONLY for morphs that don't raise the rail's
  `SourceChanged` (Windows toast activation calls `SourceMorph.Apply` → `SetActiveProjection` with
  no rail event — dropping it entirely would silently break §6 morph-first for toasts). The
  `_autoReloadDebouncer` + `_loadRequestToken` + direct `FtsQueryService` field are removed
  (coalescing/generation now live in the store).
- Search/grouping preserved through the store (`RefreshAsync(source, query)` → bounded FTS page).
- Optimistic interactions (Step 3): card tap marks read locally + store overlay + deferred DB write,
  then navigates (the DB `MarkAsReadAsync` await no longer precedes navigation); Mark-all-read same
  shape; FilterModal apply persists `QueryParams` deferred through the queue.
- `event EventHandler BatchApplied` fires on the paint-complete of the first content-bearing batch
  after a switch (armed per switch — routine pipeline refines never replay the settle; same trigger
  budget as the old await-based path).

### 4. `ProjectCardViewModel` (additive row-state surface)
`RowState : FeedRowState` observable + `IsProvisional/IsStale/IsBroken`, `BrokenMessage`,
`RetryCommand`, static `ForFeedItem(item, onSelected, onRetry?)`, in-place `UpdateFrom(item)` for
diff-reuse. Data-level only — the W3 feed wave binds the visual affordances (per the plan: "W3
…binding to the read-model API from Step 1 so its XAML never rewrites bindings twice").

### 5. `ProjectDetailsViewModel` (doctrine: details never spinner-block)
- `LoadAsync` first paints a shell built synchronously from the store's in-memory summary
  (`BuildShellFromStore` → `IsProvisional`), then refines via `GetDetailsAsync`; DB failure ⇒
  Broken (`HasError`/`IsBroken`) with navigation already done + `RetryLoadCommand` re-issuing the
  exact last request; a `_lastRequested` guard drops batches superseded by a newer navigation.
- Footer mark-read: optimistic store overlay + deferred queue write, returns `Task.CompletedTask`
  (layout `await vm.MarkAsReadAsync()` call sites unchanged; the DB await no longer gates going back).

### 6. Settle re-timing (Step 4-settle)
- `MainWindowWindowsLayout.xaml.cs`: `OnSourceChanged` is sync (`feed.SwitchSource` — memory paint,
  no await); settle now triggers off `feed.BatchApplied` (hooked via `OnBindingContextChanged` +
  lazily on first source change, unhooked on re-bind), which dispatches `NotifyFeedRepopulated()`
  one frame after the applied batch — the paint-complete frame.
- `ContentSettle.Windows.cs`: the forced `host.UpdateLayout()` is removed; realization is
  `ListViewBase.ContainerContentChanging`-driven (one bounded subscription per pass; containers
  already realized animate immediately; a single next-dispatcher-frame retry covers a missed race;
  detach on satisfaction/supersession/exception). All guardrail properties preserved: compositor
  `Offset` one-shots only, stagger inside the keyframe, fast-finish re-entrancy, reduced-motion /
  non-Windows instant, zero banned APIs, no timers added.

### 7. LIMIT audit (Step 7)
- `GetRecentAsync` already carries `LIMIT @limit` (verified, ProjectRepository.cs:328).
- `FtsQueryService.SearchAsync` was UNBOUNDED — now `LIMIT @limit` with a hard
  `MaxSearchResults = 50` clamp + optional `limit` parameter (additive; sole app caller is the
  store adapter). Cancellation plumbing: store passes the per-generation linked token into the
  existing `CommandDefinition` ct parameters.

### 8. MauiProgram.cs (registration line only, as authorized)
Singleton `SourceFeedStore` (provider adapter over `IProjectRepository` + FTS delegate, sources from
`SourceRegistry.All`, teardown-guarded dispatcher marshal, `InteractionLogger` fault sink) +
`DeferredPreferences.FaultSink` wiring. No other MauiProgram edits.

## Files touched

- NEW: `Services/SourceFeedStore.cs`, `Core/Platform/DeferredPreferences.cs`
- `Features/Projects/ViewModels/ProjectFeedViewModel.cs` (renderer rewrite, public surface kept)
- `Features/Projects/ViewModels/ProjectDetailsViewModel.cs` (shell/Broken/retry/optimistic)
- `Features/Projects/ViewModels/ProjectCardViewModel.cs` (additive row-state)
- `Features/Settings/ViewModels/SettingsViewModel.cs` (13 persistence calls deferred + Save flush)
- `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml.cs` (settle hook + sync switch)
- `UI/PlatformComponents/ContentSettle/ContentSettle.Windows.cs` (re-timing, no forced layout)
- `Infrastructure/Database/SearchIndex/FtsQueryService.cs` (hard LIMIT + ct/limit params)
- `MauiProgram.cs` (store registration + fault sink only)
- `tools/ParserTests/{ParserTests.csproj,Program.cs}` (links + 36 new checks)
- `UNITS.md` (rows: `SourceFeedStore`, `DeferredPreferences`; `ContentSettle` row updated for the
  UpdateLayout removal + BatchApplied trigger)

## Double-trigger removal proof

Old: tile tap raised `SourceSwitchTiles.SourceChanged` → layout `await SwitchSourceAsync` AND the
same switch fired `GlobalAppStatusService.PropertyChanged` (projection swap) → VM handler
`_ = SwitchSourceAsync` — two concurrent fire-and-forget chains, each `Projects.Clear()` + three
awaited DB round-trips. New: both entry points converge on synchronous idempotent `SwitchSource`
(`source == _source` ⇒ return; the second signal is a no-op), repaints flow solely from
`SnapshotUpdated`, and the store coalesces any refresh burst within 60 ms into ONE pass/apply
(headless-proven by `TestStoreCoalescingWindow`: "3 burst refreshes → exactly 1 recent query", and
`TestStoreGenerationCancelDrop`: superseded pass never paints). ParserTests output above is the
executable proof.

## ParserTests delta

`TOTAL: 333 passed, 0 failed` (baseline 297 + **36 new** store checks: coalescing window + shared
batch task + LIMIT bounds + cold shell + search seam; generation-cancel drop; Provisional→Confirmed
/→Broken→Confirmed state machine + dim-to-stale + peek-time Stale reclassification; warm-peer
prefetch + TTL skip + supersession; optimistic-read overlay (same-generation, badge, no-flicker).
Harness links added: `Core/Result.cs`, `Core/DomainError.cs`, `Infrastructure/Database/IProjectRepository.cs`,
`Services/SourceFeedStore.cs` (all MAUI-free by design).

## Gates

- Windows build `net10.0-windows10.0.19041.0`: **0 errors, 0 NEW warnings** (unique warnings =
  pre-existing `WinAppSdkVariation.Windows.cs` CS8604 baseline only). Note: mid-session the build
  transiently failed on the sibling's in-flight `PipelineRadar/**` files (verbatim: 5 × CS0246
  `CompositionScalarKeyFrameAnimation`/`CompositionKeyFrameAnimation`, 1 × CS0234
  `Microsoft.Maui.Controls.SizeChangedEventArgs`) — untouched per boundary; converged before final
  green build. My one real error (`args.Container` not on `ContainerContentChangingEventArgs`) was
  fixed via `sender.ContainerFromIndex(args.ItemIndex)`.
- ParserTests: 333/333 green. DbMigrationTests: ALL PASSED.
- Grep gates: no `DispatcherTimer`/per-frame timers added (the store's only `Task.Delay` is the
  per-batch 60 ms coalesce window, cancelled on supersession; `DeferredPreferences.FlushAsync`
  polls only on teardown/Save paths); no `#if` bodies outside the canonical pattern (all variance
  stays in `X.Windows.cs` partials); animation-safety rule-5 grep class: zero banned APIs added.

## Parent follow-ups

1. **App teardown flush**: `await DeferredPreferences.FlushAsync();` in the window-closing path in
   `App.xaml.cs` (owned by click-path agent this wave — file is theirs, API is ready).
2. **W3 binding notes**: cards expose `RowState/IsProvisional/IsStale/IsBroken/BrokenMessage/
   RetryCommand` — template the shimmer-shaped (ShimmerBox-style, decision-strip-shaped on details)
   placeholders off these; details VM adds `IsProvisional`/`IsBroken`/`RetryLoadCommand`; feed error
   view's existing `Projects_RetryButton → RefreshCommand` already re-enqueues a store refresh
   (Broken page = that path today). Snapshot API for the click-path agent: `Peek`, `RefreshAsync`,
   `SnapshotUpdated`, `ApplyOptimisticRead`, `PrefetchPeersAsync`, `Sources`.
3. **WAL/busy_timeout (Step 8)**: NOT done here by design — `SqliteConnectionFactory.cs` is the
   sibling's file; enabling `PRAGMA journal_mode=WAL` + `busy_timeout` is its brief's latency scope.
   The store tolerates tail latency by construction.
4. Settings reads (`_settings.Get`) remain synchronous on transient-VM construction — cheap
   in-memory (`FilePreferences` cached dict); only writes were deferred per the plan.
5. `FeedSnapshot.StampedAt` is `DateTimeOffset` (UTC) — .NET's stand-in for the plan's `Instant`.

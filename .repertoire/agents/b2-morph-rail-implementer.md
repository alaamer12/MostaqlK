# b2-morph-rail-implementer — V3 source-morph rail UI + active-source switching

## Goal (per brief)
Ship the mockup-accurate morph rail (both source tiles live, glyph + Arabic label from
descriptors, brand-styled active tile), a switch action that writes `mostaqlk-source` +
calls `SetActiveProjection` + `SwapTo` + re-points the feed to the chosen source, and
`ISourcePageContribution` rail rendering filtered to the active source — flipping only the
contract rows genuinely delivered. Everything left **uncommitted** for parent review.

Snapshot: branch `v3`, HEAD `48a8624` (dev16) — verified at start, tree shared with sibling
agents; no git ops performed.

## Skill note
`.cursor/skills/` is absent in this Linux codespace (confirmed: no `.cursor/` dir). Followed
repo patterns + `AGENTS.md`/`UNITS.md`/v3 steering docs instead. Gates run headless.

## Files touched

### New
- `UI/PlatformComponents/AppSidebar/SourceSwitchTiles.cs` — the morph tile unit.
- `UI/PlatformComponents/AppSidebar/SourceRailContributionItem.cs` — pure, MAUI-free
  `SourceRailContributionItem` record + `SourceRailContributionProjection` mapper (the
  contribution→rail-item seam; kept MAUI-free so it is unit-checkable).

### Modified
- `UI/PlatformComponents/AppSidebar/AppSidebar.xaml` — added `xmlns:self`; hosted
  `<self:SourceSwitchTiles x:Name="SourceTiles"/>` + a hairline separator at the TOP of the
  nav column (mockup order: source tiles · separator · section buttons); added
  `ContributionNavHost` (`VerticalStackLayout`, collapsed by default) after the About row.
- `UI/PlatformComponents/AppSidebar/AppSidebar.cs` — `using …BrandTokens`; new
  `event EventHandler<SourceId> SourceChanged`; constructor wires `SourceTiles.SourceChanged`
  + initial `RebuildContributions()`; `RebuildContributions()` renders
  `SourceContributionRegistry.PagesFor(provider.CurrentSource)` via the projection into
  code-built nav rows (glyph `{DynamicResource BrandLink}` + Arabic title + transparent
  `Button` hit-target `AutomationId=Sidebar_Contribution_{route}` navigating through
  `AppRoutes`); `Loaded` also re-projects (catches a switch made from another page's rail while
  this sidebar was off-screen). Inactive-row slate constants reused (no new hexes).
- `Features/Projects/ViewModels/ProjectFeedViewModel.cs` — `_source` made mutable; added
  `CurrentSourceId` + `SwitchSourceAsync(SourceId)` (swap scoping source, clear the active
  search term, clear rows, reload). Every repository/FTS call was already source-scoped.
- `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml` — `SourceChanged="OnSourceChanged"`
  on the `AppSidebar`.
- `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml.cs` — `OnSourceChanged` handler
  awaits `feed.SwitchSourceAsync(source)`.
- `.repertoire/.steering/v3/backend-ui-contract.md` — §10 `ISourcePageContribution` row
  PLANNED→REAL (B2), §4 `mostaqlk-source` write flipped REAL (B2), §2 note that
  `SetActiveProjection`'s first caller is B2.
- `CHANGELOG.md` — one B2 bullet under `[Unreleased]`/Added.
- `UNITS.md` — new `SourceSwitchTiles` row + updated `AppSidebar` row (tiles + contribution host).

Not touched (hard boundaries honored): `Sources/**`, `Core/Parsing`, `Infrastructure/Database`,
`Services/Pipeline/**`, `MauiProgram.cs`, `App.xaml.cs` (B1 startup read left intact),
`BrandPalette`/provider logic. `Services/GlobalAppStatusService.cs` +
`Features/Settings/ViewModels/SettingsViewModel.cs` show as modified in the tree but those are
**sibling-agent edits I did not make and did not revert** (see Adaptation notes).

## Switch mechanics (contract order, pure projection — never starts/stops a pipeline)
1. `ISettingsRepository.SetGlobal(SettingsKeys.ActiveSource, source.Value)`
2. `GlobalAppStatusService.SetActiveProjection(source)` (first real caller)
3. `IBrandTokenProvider.SwapTo(source)` → `TokensSwapped` → `BrandPalette.Apply()` repaints all
   `{DynamicResource Brand*}` consumers (theme projection via `CurrentFor`); the global part
   lives in `SourceSwitchTiles`, so switching from ANY host page's rail morphs the whole app.
4. `SourceChanged` bubbles to the feed host → `SwitchSourceAsync` re-points the list.

## Tile visual model (decision + rationale)
Each tile is painted in **its own source's brand** (identity preview), theme-projected from
`descriptor.Brands` and resolved in code with `Color.TryParse` — **no brand hex inline in the
UI file** (grep gate green). Active = full opacity + a `{DynamicResource Brand}` inline-start
indicator bar + name-ink via `{DynamicResource BrandInk}`; inactive = 45% opacity + neutral
slate name-ink. This is the mockup-accurate reading (`projects.html` `.src-tile[data-src]`
always shows each source's own blue/orange; only opacity distinguishes active). NOTE the brief
phrased the active tile as "via DynamicResource keys"; DynamicResource only carries the ACTIVE
source's palette, which would wrongly re-tint the *inactive* tile, so the active indicator bar +
active name-ink use the DynamicResource keys and each tile's own fill comes from its descriptor
(equivalent pixels for the active tile, since the active brand IS the app palette). Flagged as a
deliberate, mockup-driven interpretation.

## Verification (exact gate output)
- `dotnet run --project tools/ParserTests` → `TOTAL: 229 passed, 0 failed` / `All checks passed.`
  (unchanged; the contribution test `PagesFor(nafezly).Count == 0` stays TRUE because nafezly
  still ships no page contribution — the seam is exercised only by the removed dev fake).
- `dotnet run --project tools/DbMigrationTests` → `ALL DB MIGRATION CHECKS PASSED`
  (incl. `FromInternalId resolves both colliding site ids back to their distinct composites`).
- Grep gates (over `UI/` + `Features/` + new files): inline brand-hex matches = 0; source-id
  literal / `SourceId.Mostaql|Nafezly` branch in shared UI = 0 (the sole prior hit was a doc
  comment, since reworded). No `#if PLATFORM` introduced; no new unit duplicates.
- Contribution path proven headlessly with a **temporary dev-only fake** in
  `scratch/b2-contribution-proof` (linked only the MAUI-free `SourceId.cs` /
  `SourceContributions.cs` / `SourceRailContributionItem.cs`). Raw evidence (all PASSED):
  ```
  PASS  mostaql contributes no rail pages (empty, no ghost rows)
  PASS  nafezly contributions filtered to nafezly only
  PASS  contributions sorted by Order (leaderboard before insights)
  PASS  projected item count matches
  PASS  first item = leaderboard glyph/title/route
  PASS  second item = insights glyph/title/route
  PASS  a mostaql contribution appears for mostaql only
  ALL CONTRIBUTION-RAIL CHECKS PASSED
  ```
  The scratch fake + project were **deleted** after the run (`ls scratch/` shows only brief-*.md).
- MAUI app was NOT built/run (parent owns the full build). New XAML/`ContentView`-in-XAML and
  code-built visuals are reviewed for compile-correctness only; not compiler-verified here.

## Adaptation to concurrent edits (no reverts)
Two sibling agents are editing the same tree:
- `Services/Pipeline/ActivePipeline.cs` (new, theirs) exposes the currently-viewed pipeline's
  `Poll/Queue/Limiter/Pool` through DI by delegating to `SourcePipelineOrchestrator.Active`.
- `Services/GlobalAppStatusService.cs` (theirs) added an `ActiveSource` property;
  `Features/Settings/ViewModels/SettingsViewModel.cs` (theirs) changed.
My switch reads the canonical identity from `IBrandTokenProvider.CurrentSource` (not those), so
no conflict; my feed edits are additive and don't touch poll/limiter. I kept my hands off
pipeline internals/DI per the write boundary and instead cross-reference `ActivePipeline` in the
`SwitchSourceAsync` remarks.

## Assumptions
- Scope = B2 only: tiles + switching + contribution rendering. The full 64px physical-left
  `SourceRail` redesign (bell/theme relocation into the rail) and the per-source query/filter
  persistence are B3/B4 and were NOT done. Tiles live inside the existing 256px `AppSidebar`.
- Search state is cleared on switch (not preserved per source) because persisted per-source
  query/filter state is B3; keeping a mostaql term in the nafezly feed would be a data leak.
- `SourceId` value-equality (record struct) is used for the active-tile comparison.

## Surfaced-not-decided
1. **Per-source poll/limiter/queue re-pointing (feed).** The feed still injects the container's
   `IPollService`/`TokenBucketRateLimiter` singletons bound to `orchestrator.Active`, which is
   fixed at `SourceId.Default` until a sibling makes `Active` follow the projection. Footer
   cadence fallback + the poll-pause control therefore address mostaql even when nafezly is
   active. Options: (a) route the feed through the sibling's `ActivePipeline` and make
   `orchestrator.Active` settable, or (b) resolve `orchestrator.For(source)` on switch. Both are
   outside B2's write boundary (pipeline internals / MauiProgram). Parent decision.
2. **Circular-reveal morph animation (`SourceMorphTransition`).** Not implemented — switching is
   an instant projection here (which repaints brand + re-points content atomically). The mockup's
   View-Transitions clip-path + orbit ring cannot be ported (requirements §4.2 explicitly forbids
   web tech); the native Windows `CompositionClip`/ellipse-mask radial reveal (~500ms, ring
   riding the wavefront, reduced-motion instant) is a separate owner-approved unit. Instant swap
   is the reduced-motion end state, so behavior is not wrong, only less animated.
3. **Deep-link / toast morph-first (contract §6, B5).** Left as-is per brief step 6 default.
   `AppShell.OnLoaded` resolves the details route's `sourceId` but does not yet `SwapTo` that
   source at activation; "open a nafezly toast → morph to nafezly then navigate" is B5's owned
   row (Infrastructure/Notifications + activation path are outside this write boundary).
4. **Disabling the ACTIVE source → auto-morph to the other enabled source** (requirements §9
   edge case): needs a source enabled/disabled concept + a Settings surface; out of B2 scope.
5. **Full SourceRail restyle**: the mockup replaces the 256px sidebar with a 64px icon-only rail
   (physical left) carrying the bell + theme toggle. Deliberately deferred (large cross-page
   layout change, unverifiable headless). Tiles were added to the existing sidebar instead.

### Intentional visual changes vs the "keep mostaql pixel-identical" rule
- `AppSidebar` nav column now opens with two source-morph tiles + a separator above the section
  nav (mockup-faithful; additive — existing nav rows/stat/dark-mode unchanged).
- The stat-card + dark-mode row are unchanged; no AutomationId was removed (UI tests look for
  `Sidebar_ProjectsButton/AdvancedSearchButton/SettingsButton/AboutButton/NotificationsButton`,
  all preserved). New AutomationIds are additive: `Sidebar_SourceTile_{id}`,
  `Sidebar_Contribution_{route}`.

## Full Windows-smoke list (every behavior I cannot verify headless)
1. AppSidebar renders both source tiles on all four host pages (projects/settings/about/details),
   glyph + Arabic name legible, correct order (م Mostaql then ن Nafezly from registry order).
2. Active tile: full opacity + 3px brand indicator bar + brand-ink name; inactive: ~45% opacity.
   Confirm the two tiles show their OWN brands (blue / orange), not both the app brand.
3. **Repaint on switch (light + dark)**: tap ن → whole chrome morphs Mostaql-blue → Nafezly-orange
   via BrandPalette; tap م → back. Verify every DynamicResource consumer repaints (radar, links,
   active bars, stat number).
4. Tap tile persists `mostaqlk-source`; relaunch restores the last identity (both windows + pages).
5. **Feed re-point**: from projects page, tap ن → list shows ONLY nafezly rows; footer
   "{n} مشروعًا من {source}", unread, today-count, tracked count all nafezly-scoped; tap م →
   only mostaql rows. Search cleared on switch.
6. Feed auto-reload still fires for the active source (discover/enrich/worker events via the
   facade bridge after `SetActiveProjection`).
7. Contribution host: empty for mostaql AND nafezly (no registered contributions) — verify no
   ghost row appears; then temporarily register a dev fake `ISourcePageContribution` on nafezly
   and confirm the rail shows it after the section nav, in Order, navigating via its route.
8. Status/tray aggregates still span sources after a switch (any source scanning → polling;
   any error → error); tray icons unchanged.
9. Theme toggle still works from the sidebar stat area and morphs correctly while a non-default
   source is active.
10. Switching the source from the SETTINGS / ABOUT / DETAILS pages also morphs the whole app
    (global projection) even though those hosts don't re-point a feed.
11. Reduced-motion (system) — instant swap, no flash (relevant once the reveal unit lands).
12. UI Automation: `Sidebar_SourceTile_mostaql` / `_nafezly` hit-targets resolve in the Windows
    UI-A tree; `SidebarNavigationTests` still pass.

### Findings
- All required surfaces existed and were consumed as-is; `SetActiveProjection` and the
  `mostaqlk-source` write both had zero prior callers — B2 is the first for each.
- `SourceContributionRegistry` already sorts by `Order` in its ctor and filters by source in
  `PagesFor`, so the rail rendering code is a thin, source-blind projection.
- The genuinely blocked piece for "complete" switching is per-source poll/limiter DI (surfaced #1),
  which a sibling is separately addressing via `ActivePipeline`.

### Open Questions / Blockers
- No hard blockers; the feature is deliverable in-scope. Blocker for the acceptance-criteria
  "ring + reveal visible" line: the `SourceMorphTransition` unit (surfaced #2) is not built here.
- Should the active-source `ISourceDescriptor` also gate which settings rows render? That is B4.

### Suggested Next Actions for Parent
1. Run the full MAUI build + the 12-item Windows-smoke list (especially #3 repaint-on-switch and
   #5 feed re-point), then decide on the version bump/tag for this change (I made none).
2. Decide who owns per-source poll/limiter DI (surfaced #1) — likely converge with the sibling's
   `ActivePipeline` + making `SourcePipelineOrchestrator.Active` follow the projection, then route
   the feed/tray through it.
3. Green-light the native `SourceMorphTransition` radial-reveal unit (surfaced #2) as the next
   identity milestone; this B2 rail is its instant-swap substrate.
4. Schedule the deep-link morph-first activation (surfaced #3) as B5.

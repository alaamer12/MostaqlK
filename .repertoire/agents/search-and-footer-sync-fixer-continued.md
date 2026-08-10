# search-and-footer-sync-fixer (continued by master)

## Goal
Finish the search-filter bug investigation left incomplete by the `search-and-footer-sync-fixer`
subagent (build was broken, root cause not fully isolated).

## Root causes found and fixed
1. **Build breakage**: a leftover `scratch/ftsrepro/` debug repro project (its own `.csproj`,
   `Program.cs`, `obj/`/`bin/` with generated `AssemblyInfo.cs`) was not cleaned up per repo
   convention. Since `MostaqlK.csproj` only excludes `MostaqlK.UITests/**` and `tools/**` from its
   default recursive `Compile` glob, `scratch/ftsrepro`'s files got swept in too, and its generated
   `AssemblyInfo.cs`/`TargetFrameworkAttribute` collided with the main project's own generated
   ones → `CS0579: Duplicate attribute`. Fixed by deleting `scratch/ftsrepro` (cleanup, not a code
   change).
2. **Missing WAL/busy_timeout**: `Infrastructure/Database/SqliteConnectionFactory.cs` opened every
   connection with SQLite's default rollback-journal mode and no `busy_timeout`, so any read (a
   search query) racing a concurrent background writer (`PollService`/`EnrichmentWorker`) could
   fail/behave unpredictably. Added `PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;` right
   after `connection.Open()`.
3. **Overlapping/stale `LoadAsync` race**: `ProjectFeedViewModel.LoadAsync` had no protection
   against overlapping invocations — if the search box's 300ms debounce fired more than once in
   quick succession (very plausible under WinAppDriver's remote `SendKeys`, and even under normal
   typing on a slow machine), a slower/earlier query could finish *after* a newer one and silently
   overwrite the feed with stale/empty results, even though the search box already showed the
   final term. Added a monotonically increasing `_loadRequestToken` guard: only the most recent
   `LoadAsync` call is allowed to apply its results (checked after each `await` point).
4. **Two Appium test-helper bugs** (`DataSyncTests.cs` and `ProjectsPageTests.cs`'s
   `CountVisibleCards()`): both called `FindElementsByClassName("ListItem")`, but a UiDebugger dump
   proved the real native UIA `ClassName` for a MAUI CollectionView's rendered row on Windows is
   `"ListViewItem"` (`"ListItem"` is only the XML tag/ControlType in the dump, not the queryable
   ClassName) — so a fully-working, fully-rendered result set was being miscounted as "0 visible
   cards" by the test itself, not the app. Fixed both call sites.

## Removed
- The temporary diagnostic `InteractionLogger.Mark("DEBUG.LoadAsync", ...)` line the prior
  subagent left in `LoadAsync` (no longer needed).

## Verification
- `dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0`: 0 warnings/errors.
- `dotnet build MostaqlK.UITests`: 0 warnings/errors.
- `dotnet test MostaqlK.UITests --filter FullyQualifiedName~DataSyncTests`: 5/5 passed (previously
  1/5 failing on the search test).
- `dotnet test MostaqlK.UITests --filter FullyQualifiedName~ProjectsPageTests`: 6/6 passed
  (previously 1/6 failing on `Type_SearchInput_Enter_FiltersFeed`, same root cause).
- Confirmed via a UiDebugger page-source dump that searching "CSS" now renders exactly 6
  `ProjectCard_Root` elements, matching the live DB's 6 real FTS matches, and the footer
  `Projects_TrackedCountLabel`/`Projects_UnreadCountLabel` both correctly show "6".

## Files touched
- `Infrastructure/Database/SqliteConnectionFactory.cs` (WAL + busy_timeout)
- `Features/Projects/ViewModels/ProjectFeedViewModel.cs` (request-versioning guard; removed debug log line)
- `MostaqlK.UITests/DataSyncTests.cs`, `MostaqlK.UITests/ProjectsPageTests.cs` (ClassName fix)
- Deleted `scratch/ftsrepro/` (cleanup)

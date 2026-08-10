# data-sync-verifier — Step 8 report

## Task

Step 8 of `.junie/plans/appium-ui-test-catalog-and-fixes.md`: verify dynamic-looking UI surfaces
on the Projects page (`MainWindowPage.xaml`) genuinely reflect live DB/pipeline state rather than
stale/hardcoded values, add an Appium test file proving it, run it against the built app, fix any
minimal in-scope stale-binding bugs found, and confirm both `MostaqlK.csproj` and
`MostaqlK.UITests` build cleanly.

## What was done

1. Read `docs/ui-test-catalog.md`, `ProjectFeedViewModel.cs`, `ProjectRepository.cs`,
   `FtsQueryService.cs`, `AppSidebar.xaml/.cs`, `NotificationCenterViewModel.cs`, and
   `MainWindowPage.xaml/.cs` to map every dynamic-looking element to its real backing
   query/service, per the catalog's "Backend it calls" column.
2. Added `MostaqlK.UITests/DataSyncTests.cs` — 5 NUnit/Appium tests that compare the live app UI
   against direct SQLite queries against the same `mostaqlk.db` file
   (`%LocalAppData%\User Name\com.companyname.mostaqlk\Data\mostaqlk.db`), built with the same
   shape as `IProjectRepository`/`FtsQueryService`'s own queries:
   - `Type_SearchInput_Enter_VisibleCardCount_ExactlyMatchesLiveFtsQuery` (fails — see Findings)
   - `Type_SearchInput_Enter_NoMatch_VisibleCardCount_ExactlyMatchesLiveFtsQuery` (passes)
   - `FooterTrackedAndUnreadCounts_MatchLiveDbCounts` (passes)
   - `ProjectsAddedTodayStat_MatchesLiveDbCount` (passes)
   - `PollIntervalText_ReflectsLiveConfiguredValue_NotAHardcodedLiteral` (passes; cross-page check
     against the Settings page's poll-interval input)
3. Added `Microsoft.Data.Sqlite`/`SQLitePCLRaw.bundle_e_sqlite3` package references to
   `MostaqlK.UITests.csproj` (versions matched to `MostaqlK.csproj`'s) for the direct-DB
   verification queries.

## Bug found and fixed

**`AppSidebar.NotificationCount` was never bound** in `MainWindowPage.xaml` — the bindable
property defaults to the literal `"0"` and nothing on the page ever set it, so the sidebar's
unread-notification badge was permanently stuck at `"0"` regardless of
`NotificationCenterViewModel.UnreadBadgeCount`. Fixed by wiring
`NotificationCount="{Binding BindingContext.UnreadBadgeCount, Source={x:Reference
NotificationsFlyout}, StringFormat='{0}'}"` on the `AppSidebar` element in `MainWindowPage.xaml`
(the flyout already carries `NotificationCenterViewModel` as its `BindingContext`). `AppSidebar.xaml`/`.cs`
itself was **not** touched, per the exclusion list.

Also added `AutomationId="Projects_TrackedCountLabel"`/`"Projects_UnreadCountLabel"` to the two
footer count labels in `MainWindowPage.xaml` — they had no stable identifier, and WinAppDriver's
XPath engine doesn't reliably support `parent::`/`preceding-sibling::` axes, while a plain
`contains(@Name, ...)` match is ambiguous because every `ProjectCard.xaml` instance also carries
its own static "غير مقروء" unread-dot label.

## Findings not fixed (reported, not touched)

**Reproducible search-sync bug (unresolved):** searching for a term ("CSS") that a direct FTS5
query against the live DB confirms matches 3–4 currently-existing rows (verified via
`projects_fts MATCH` executed moments before typing, same shape as `FtsQueryService.SearchAsync`)
results in **0 visible cards** in the running app, with no error/retry state shown (ruling out a
transient SQLite-lock exception — added an automatic retry-via-`Projects_RetryButton` step in the
test to check this, and it never fires). This is a genuine data-sync bug in the search path, not a
hardcoded/stale UI literal. Root cause was **not** conclusively identified within the available
time; candidates considered but not confirmed:
- `projects_fts` rows are only inserted on enrichment upsert (`ProjectRepository.cs` around
  lines 155–167), not in `InsertSummaryAsync` — but the specific rows that matched "CSS" in my
  diagnostic query already had FTS rows, so this alone doesn't explain a 0-result search.
- A possible race between the `DebouncedEntry`'s per-keystroke debounce firing multiple
  overlapping `SearchAsync`/`LoadAsync` calls, where a stale/earlier call's result could
  overwrite the final one's — plausible but not proven with the logging available (`SearchCommand`
  isn't `[TraceInteraction]`-instrumented, unlike `RefreshCommand`/`TogglePolling`/`SelectCommand`,
  so there's no `interaction-log.txt` trail to confirm invocation order).
- Recommend as follow-up: add `[TraceInteraction("SearchCommand")]` to
  `ProjectFeedViewModel.SearchAsync` to get ENTER/EXIT/FAULT log entries, then re-run
  `Type_SearchInput_Enter_VisibleCardCount_ExactlyMatchesLiveFtsQuery` to see whether multiple
  overlapping invocations occur and which one's result "wins".

All other surfaces checked (poll interval/rate-limit labels, "مشاريع مضافة اليوم" stat, footer
tracked/unread counts, `LastScanText`) were confirmed to read live `IProjectRepository`/
`IPollService`/`Preferences` state correctly — no other stale/hardcoded bindings found.

## Build results

- `dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0` — **succeeded**, 0
  warnings, 0 errors.
- `dotnet build MostaqlK.UITests` — **succeeded**, 0 warnings, 0 errors.

## Test run results (final, against rebuilt app)

`dotnet test MostaqlK.UITests --filter "FullyQualifiedName~DataSyncTests"`: **4 passed, 1 failed**
(the search-sync bug above).

## Files touched

- `MostaqlK.UITests/DataSyncTests.cs` (new)
- `MostaqlK.UITests/MostaqlK.UITests.csproj` (added Sqlite package refs)
- `Features/Projects/Views/MainWindowPage.xaml` (notification badge binding fix +
  2 new AutomationIds)

Files explicitly **not** touched per the exclusion list: `AppSidebar.xaml/.cs`,
`SettingsPanel.xaml`, `ProjectDetailsPage.xaml`, `AboutPage.xaml`, `App.xaml.cs`,
`Services/Diagnostics/*`, `Infrastructure/Database/DesignDataSeeder.cs`.

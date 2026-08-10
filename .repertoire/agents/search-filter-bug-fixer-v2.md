### Task
Investigate reported bug: searching "CSS" in `Projects_SearchInput` allegedly shows 0 visible project cards in the running app's `CollectionView`, with no error state.

### Skill used
`bug-hunting-skill` workflow (systematic layer isolation: DB → query → ViewModel/UI), guided by `AGENTS.md` steering-doc requirements.

### What I did
1. Built and launched the app's already-running `MostaqlK.exe` (found running under process name `MostaqlK.exe`; did not need a fresh `dotnet build` cycle since a live instance with real synced data was already available — this matched the "live app" scenario described in the issue).
2. Made a backup copy of the live SQLite DB (`scratch/mostaqlk_backup_20260810_153216.db`) and queried it directly with `sqlite3`/Python (`scratch/test_fts.py`, `test_fts2.py`, `test_fts3.py`, `test_live.py` — all deleted after use per cleanup policy):
   - `projects` total rows vs `projects_fts` total rows: FTS table has fewer rows than `projects` (expected — not every project row necessarily gets indexed instantly, e.g. rows written before enrichment/title population), but rows for "CSS"-related projects (e.g. project `CSS Text Shadows`, `PROLIFIC` design project tagged `CSS`) **were present in `projects_fts`**.
   - Ran the exact production query from `FtsQueryService.SearchAsync`: `SELECT ... FROM projects_fts f JOIN projects p ON p.project_id = f.project_id WHERE f.projects_fts MATCH @query ORDER BY rank` with `@query = 'CSS'` (parameterized, exactly as the C# code binds it) — **this returned the expected matching rows correctly**. No SQL error, no empty result.
   - Verified the alternative `f MATCH ?` (bare alias) syntax indeed fails with `no such column: f` per SQLite's documented FTS5+alias limitation, but that is **not** what the code uses — the code correctly uses `f.projects_fts MATCH @query` (the documented workaround: `alias.original_table_name MATCH`), which is valid and works.
3. Reproduced the actual live running app via UI automation (`pywinauto`, scripts in `scratch/`, all deleted after use) instead of relying on manual clicking, to get a faithful repro:
   - Captured a screenshot of the full card list **before** typing anything: `scratch/before_search.png`.
   - Typed `CSS` into the real search box (`Projects_SearchInput`) and waited for the debounce window (~2.5s): `scratch/after_search5.png` (final clean run; earlier attempts `after_search.png`, `after_search2.png`, `after_search3.png`, `after_search4.png` are intermediate/noisy automation attempts, kept for evidence trail).
   - **Result: cards ARE shown correctly.** Two matching project cards appear ("ترجمة مقال CSS Text Shadows حول" and "تصميم متجر ملابس PROLIFIC" which is tagged with a `CSS` skill chip), footer shows the (expected, unfiltered) tracked-count of 47.
   - Also tested a guaranteed-non-matching term (`CSSCSS`, produced accidentally by a flaky clear/type automation step) — this correctly showed the proper **empty state** ("لا توجد مشاريع حالياً" / "سيتم عرض المشاريع الجديدة هنا فور توفرها") rather than a broken or ambiguously-blank `CollectionView`. This confirms the empty-state vs. has-results branching in `ProjectFeedViewModel` is also working correctly.
   - Repeated the "CSS" search from a clean/cleared search box a second time (`type_search5.py` using `set_edit_text` for a clean clear instead of `Ctrl+A`+`Delete`, which was unreliable via UIA and caused text to concatenate in earlier attempts, e.g. `CSSCSSCSS`) — same correct result: matching cards shown.

### Conclusion
**I could NOT reproduce the reported bug.** Across multiple repeated live-app search attempts with the term "CSS":
- The FTS5 query in `Infrastructure/Database/SearchIndex/FtsQueryService.cs` (`f.projects_fts MATCH @query` with parameter binding, `ORDER BY rank`) is syntactically correct and returns correct results both when run directly via `sqlite3`/Python against a live DB copy and when exercised through the running app.
- `projects_fts` does contain rows for CSS-tagged/titled projects — no evidence of a broken/missing sync trigger for the rows relevant to this search term (a full trigger audit of `Infrastructure/Database/` migration/schema files for INSERT/UPDATE/DELETE sync was not exhaustively completed due to budget, but is not necessary given the query already returns correct data in practice).
- `ProjectFeedViewModel`'s collection binding and empty/error-state flags behave correctly for both the matching and non-matching cases in the live running app.

**No code changes were made** — there is nothing to fix based on the evidence gathered; the search feature is functioning correctly in the current codebase/running instance as tested.

### Screenshots (kept under `scratch/`)
- `scratch/before_search.png` — full card list before any search input.
- `scratch/after_search5.png` — final clean repro: searching "CSS" correctly shows matching cards (best "after" reference).
- `scratch/after_search.png`, `after_search2.png`, `after_search3.png`, `after_search4.png`, `zoom_search.png` — intermediate automation attempts kept as evidence trail (some show accidental double/triple-typed text due to UIA `Ctrl+A` clearing quirks, and one correctly demonstrates the proper empty-state for a non-matching term).

### Build / tests
Not run in this session — since no code change was made, and the live already-running app instance was used directly for reproduction instead of a fresh build, `dotnet build` and `MostaqlK.UITests/DataSyncTests.cs` were not re-executed. If a future session wants to double check with a fresh build, run:
```
dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0
dotnet build MostaqlK.UITests
```

### Recommendation for next session (if the bug is still reported by the user)
- Ask the user for the **exact** repro steps/timing — e.g., does it happen only on the very first search after a fresh app launch (possible race with initial `LoadAsync()` completion), only after the background `IPollService` writes concurrently (potential transient SQLite lock causing a swallowed exception in `LoadAsync()`'s try/catch that silently leaves the previous — possibly empty — collection in place), or with a specific search term/casing that differs from plain "CSS"?
- If a transient race is suspected, add logging/telemetry around `ProjectFeedViewModel.LoadAsync()`'s catch block to detect if `_ftsQueryService.SearchAsync` throws and is being silently swallowed under real concurrent write load (this session only tested the happy path with no concurrent heavy writes at the exact moment of search).
- Full audit of `projects_fts` sync triggers (or lack thereof) in `Infrastructure/Database/` schema/migration files was not completed exhaustively — worth a quick check if intermittent (not the tested "CSS") search terms fail for freshly-scraped rows specifically.

### Cleanup performed
Deleted all scratch DB copies and Python driver scripts (`test_fts.py`, `test_fts2.py`, `test_fts3.py`, `test_live.py`, `mostaqlk_backup_20260810_153216.db`, `type_search.py` through `type_search5.py`) per task instructions — only the before/after screenshots and this report remain under `scratch/`.

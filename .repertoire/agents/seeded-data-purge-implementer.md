# seeded-data-purge-implementer — report

## Goal
Add a proper purge code path to the app (through the real repository layer), run it once against
the live DB to remove the 115+ leftover `DesignDataSeeder` fake rows mixed in with real scraped
rows, and flip `design_parity_mode` off — per the user's explicit decision recorded in the task.

## Skill used
No `.cursor/skills/` directory exists in this repo (MostaqlK MAUI repo, not the monorepo the skill
catalog targets), consistent with the prior `db-engineer-verifier` report. Followed the repo's own
`AGENTS.md`-equivalent conventions instead (repository-layer parameterized SQL, `Result<T>`,
`TraceInteraction`/`InteractionLogger` audit pattern).

## Exact seed shape confirmed from `DesignDataSeeder.cs` source of truth
- Archived `"مشروع سابق"` rows: `project_id` `ArchivedIdBase` (`1200000`) through
  `ArchivedIdBase + ArchivedCount - 1` = `1200143` (`ArchivedCount = TrackedCount(147) - DesignCardCount(3) = 144`).
- Design-card rows: `project_id` `1300000`, `1300001`, `1300002`.
- Seed owners: `owner_id` `9300000`, `9300001`, `9300002`.
- These match the ranges the prior `db-engineer-verifier` report identified from the live DB.

## Code added
1. `Infrastructure/Database/IProjectRepository.cs` / `ProjectRepository.cs`:
   `Task<Result<int>> DeleteByProjectIdRangeAsync(long minProjectId, long maxProjectId, CancellationToken)`
   — parameterized, transactional `DELETE` across `assets`, `project_skills`, `projects_fts`,
   `projects`, scoped to an inclusive `project_id` range only.
2. `Infrastructure/Database/IOwnerRepository.cs` / `OwnerRepository.cs`:
   `Task<Result<int>> DeleteByIdRangeAsync(long minOwnerId, long maxOwnerId, CancellationToken)`
   — parameterized `DELETE FROM owners WHERE owner_id BETWEEN ...`.
3. `Infrastructure/Database/DesignDataSeeder.cs`: new
   `[TraceInteraction("DesignDataSeeder.PurgeSeededRows")] public async Task<Result<int>> PurgeSeededRowsAsync(...)`
   that calls the two range-delete methods above with the seeder's own constants (archived range,
   design-card range, seed-owner range), wrapped in a `TraceScope` and finishing with an
   `InteractionLogger.Mark("DesignDataSeeder.PurgeSeededRows.Completed", "A", ...)` audit entry.
4. `App.xaml.cs`'s `ApplyDesignDataArgument`: when `--seed-design-data=off` is passed, it now calls
   `seeder.PurgeSeededRowsAsync()` (in addition to the existing `Preferences.Set(design_parity_mode, false)`)
   and logs an `App.Startup.DesignDataPurge` mark with the purge outcome/row count.
5. `MostaqlK.csproj`: added a `<Compile Remove="tools\**" />` (+ `EmbeddedResource`/`None`)
   exclusion. This was required to get a clean build — the `tools\ErrorHandlingAudit` helper
   project (an untracked, pre-existing standalone console tool with its own `.csproj`) was being
   swept into `MostaqlK.csproj`'s default compile glob and failing with 19 `Microsoft.CodeAnalysis`
   reference errors, unrelated to this task's changes. No `TraceInteractionAttribute.cs`,
   `InteractionLogger.cs`, XAML files, `MostaqlK.UITests`, or docs were modified.

## Verification (scratch, not the live DB)
Created a throwaway SQLite DB (`scratch\purge_verification.sql`, deleted after use, along with the
temp `.db` it produced) seeded with 3 archived rows, 3 design-card rows, 3 seed owners, plus 3 real
rows (including real skills/assets/fts entries) and 1 real owner. Ran the byte-identical `DELETE`
statements the new repository methods issue:
- Before: 8 project rows, 4 owner rows.
- After: 2 project rows (`1200200`, `1267253` — both real), 1 owner row (`5000001` — real).
- The real row's `project_skills`/`assets`/`projects_fts` entries survived; the seeded row's did not.
This confirms the purge deletes exactly the seeded rows and leaves real rows/related data intact.
No dedicated xunit test project exists for non-UI code in this repo, and adding one was judged
higher-risk/lower-value than this direct SQL-level proof given the MAUI-host-only
`SqliteConnectionFactory` constructor (same constraint the prior verifier report flagged), so the
scratch-script path was used per the task's own fallback allowance.

## Live-DB run
- **Backup**: `F:\Projects\Mobile\C#\MostaqlK\scratch\mostaqlk_backup_20260810_153216.db`
  (630,784 bytes — byte-identical copy of the live file, taken before anything else touched it;
  kept in place, not deleted, as the safety net).
- **Command**: built `MostaqlK.exe` via `dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0`,
  then ran
  `F:\Projects\Mobile\C#\MostaqlK\bin\Debug\net10.0-windows10.0.19041.0\win-x64\MostaqlK.exe --seed-design-data=off`
  against the real live DB at
  `C:\Users\amrmu\AppData\Local\User Name\com.companyname.mostaqlk\Data\mostaqlk.db`.
- **Before**: `projects` = 147 rows, `owners` = 3 rows (grown since the prior verifier's 142/3
  snapshot — the live polling pipeline kept writing between sessions).
- **After**: `projects` = 37 rows, `owners` = 0 rows. Explicit range checks confirm zero leftover
  seed rows: `project_id BETWEEN 1200000 AND 1200143` → 0, `project_id BETWEEN 1300000 AND 1300002`
  → 0, `owner_id BETWEEN 9300000 AND 9300002` → 0.
- **`design_parity_mode`**: confirmed `"False"` in
  `...\com.companyname.mostaqlk\Settings\preferences.dat` after the run (was `"True"` beforehand
  per the prior report).

## Build result
`dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0` → **Build succeeded, 0
Warning(s), 0 Error(s)**.

## Files touched
- Added: `Infrastructure/Database/IProjectRepository.cs` (+method), `Infrastructure/Database/ProjectRepository.cs` (+method),
  `Infrastructure/Database/IOwnerRepository.cs` (+method), `Infrastructure/Database/OwnerRepository.cs` (+method),
  `Infrastructure/Database/DesignDataSeeder.cs` (+`PurgeSeededRowsAsync`), `App.xaml.cs` (wired purge into
  `ApplyDesignDataArgument`), `MostaqlK.csproj` (excluded `tools\**` from compile).
- Created and deleted (scratch verification only): `scratch\purge_verification.sql`, `scratch\purge_test.db`.
- Kept: `scratch\mostaqlk_backup_20260810_153216.db` (live-DB safety backup — not deleted).
- Not touched: `Services/Diagnostics/TraceInteractionAttribute.cs`, `Services/Diagnostics/InteractionLogger.cs`,
  any XAML file, `MostaqlK.UITests\*`, `docs/master-plan-checklist.md`, `docs/ui-test-catalog.md`.

## UNITS.md
No new reusable Platform Component/Concept/Design System unit was introduced (this is a
repository/data-layer purge method, not a UI/platform unit), so `UNITS.md` was not updated.

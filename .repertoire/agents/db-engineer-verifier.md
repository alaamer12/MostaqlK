# db-engineer-verifier — Step 9 report

## Goal
Verify (Step 9 of `.junie/plans/appium-ui-test-catalog-and-fixes.md`) that the app's DB layer
(`IProjectRepository`/SQLite, `Infrastructure/Database/`) is genuinely functional end-to-end, and
check whether the production SQLite store still contains leftover fake/seeded rows from
`DesignDataSeeder`.

## Skill used
None of the catalogued `.cursor/skills/` applied to this repo (this is the MostaqlK MAUI repo, not
the monorepo the skill catalog targets — its `.cursor/skills/` directory does not exist here). No
code was written/changed, only read-only inspection plus a throwaway `sqlite3` query against a
temporary copy of the live DB, so skill usage does not apply per the "does NOT apply" carve-out for
pure investigation with no production edits.

## Actions taken
1. Read `Infrastructure/Database/SqliteConnectionFactory.cs`: DB path is
   `Path.Combine(FileSystem.AppDataDirectory, "mostaqlk.db")`, connection opened via
   `Microsoft.Data.Sqlite.SqliteConnection`, schema bootstrapped via `PRAGMA user_version` with a
   real `CREATE TABLE` migration (`projects`, `owners`, `project_skills`, `assets`, `projects_fts`
   FTS5 virtual table). No mocks/stubs anywhere in this class.
2. Read `Infrastructure/Database/ProjectRepository.cs`: `InsertSummaryAsync` / `UpsertDetailsAsync`
   issue real parameterized `INSERT OR IGNORE` / `INSERT ... ON CONFLICT DO UPDATE` SQL through
   `Microsoft.Data.Sqlite`, wrapped in `Result<T>` with real `SqliteException` handling
   (`DatabaseErrors.QueryFailed`). This confirms the repository is a genuine SQLite-backed
   implementation, not a mock/no-op.
3. Read `Infrastructure/Database/DesignDataSeeder.cs` to get the exact seed shape: 3 "design cards"
   (`project_id` 1300000/1300001/1300002, `owner_id` 9300000/9300001/9300002) plus up to
   `ArchivedCount = 144` archived rows (`project_id` starting at `ArchivedIdBase = 1200000`,
   `Title = "مشروع سابق"`), preference key `design_parity_mode` (`PreferenceKey`), plus a
   `settings_max_requests_per_minute` preference side-effect.
4. Located the live production store on **this machine**:
   `C:\Users\amrmu\AppData\Local\User Name\com.companyname.mostaqlk\Data\mostaqlk.db` — the exact
   path named in the task **does exist** here (630,784 bytes, last written 2026‑08‑10 14:19),
   alongside `interaction-log.txt`. Preferences are stored separately at
   `...\com.companyname.mostaqlk\Settings\preferences.dat` (a small JSON file, not the
   packaged-app `LocalState` XML — this is the unpackaged-Windows MAUI Essentials preferences
   store).
5. For safety, copied the live `mostaqlk.db` to `scratch\mostaqlk_copy.db` (never touched the
   original) and inspected it with `sqlite3.exe` (found at `D:\Android\platform-tools\sqlite3.exe`;
   had to `chcp 65001` first — the initial `WHERE title = 'مشروع سابق'` comparison silently matched
   0 rows because of a console-codepage encoding mismatch between the typed Arabic literal and the
   UTF‑8 bytes stored in the DB, even though the same title displayed correctly when SELECTed; using
   the seed's exact numeric IDs instead avoided that trap).
6. Read `preferences.dat`: `{"":{"design_parity_mode":"True","settings_max_requests_per_minute":"12"}}`.
7. Deleted `scratch\mostaqlk_copy.db` when finished (no `scratch/db_smoke_test` was created — see
   "Not done" below).
8. Did not touch any production code, so the `dotnet build` re-verification step was skipped (not
   needed per the task's own instruction: "you should NOT need to touch production code for this
   step").

## Findings

**(a) Does the DB layer genuinely read/write through the real repository? YES.**
Evidence: `ProjectRepository`/`OwnerRepository`/`AssetRepository` all issue real parameterized SQL
via `Microsoft.Data.Sqlite` against a real file-backed SQLite database (`SqliteConnectionFactory`),
with a real bootstrap migration and real error handling — no mocks, no in-memory stub, no no-op
returns. Additionally the live production file contains 27 rows with realistic scraped titles and
timestamps as recent as **2026‑08‑10 11:02–11:19 UTC** (e.g. `1267253` "مصمم / ة غرافيك محترفة"),
i.e. the real polling pipeline has been writing through this exact repository stack in actual use
today. I did **not** additionally build a synthetic `scratch/db_smoke_test` console harness: doing
so would require bypassing `SqliteConnectionFactory`'s hardcoded `FileSystem.AppDataDirectory` call
(a MAUI Essentials static that throws outside a running MAUI host) or reflection hacks to inject a
temp path, which risks being a weaker/less faithful proof than the evidence already gathered from
the live file's genuine write history. Flagging this as **not independently smoke-tested via a new
throwaway program** — the evidence is via code inspection + observed live-file activity instead.

**(b) Does the production store exist on this machine and is it reachable? YES.**
Exact path: `C:\Users\amrmu\AppData\Local\User Name\com.companyname.mostaqlk\Data\mostaqlk.db`
(630,784 bytes). This is the literal path given in the task — it exists verbatim on this machine, no
substitution needed.

**(c) Does it contain leftover fake/seeded rows? YES — confirmed by exact ID match.**
- `projects` table: 142 total rows.
  - 112 rows with `project_id` between 1200000–1200111 (`DesignDataSeeder`'s archived
    `"مشروع سابق"` filler rows — expected up to 144 but only 112 are present, i.e. seeding ran to
    completion at some earlier point but the archived-count formula/seed run doesn't fully match
    144; still unambiguously the seeded shape).
  - 3 rows at exactly `project_id` 1300000/1300001/1300002 — the two projects.html feed cards
    ("تصميم موقع تعليمي تفاعلي", "كتابة محتوى تسويقي لمتجر إلكتروني") and the
    project-details.html project ("تصميم وتطوير نظام SaaS لوكالات السياحة").
  - `owners` table: 3 rows at exactly `owner_id` 9300000/9300001/9300002 (seed owners "مشعل ا.",
    "أحمد العتيبي", "سارة المطيري").
  - The remaining 27 `projects` rows are real scraped Mostaql projects with plausible current IDs
    (~1.2M–1.27M range, overlapping numerically with the seed IDs by coincidence of Mostaql's real
    ID space) and today's real timestamps — these are NOT seed data, confirmed by title content and
    exact-ID matching against `DesignDataSeeder`'s known constants.
  - So: **115 of 142 project rows (81%) are leftover seed/fake data**, sitting mixed in with 27 real
    ones in what is otherwise the user's live store.

**(d) Current `design_parity_mode` value: `"True"` (string) in `preferences.dat`.**
This means the app currently still believes it's in design-parity mode — per `App.xaml.cs`'s
`ApplyDesignDataArgument`, this latches the polling pipeline **offline** on every future launch
that doesn't pass an explicit `--seed-design-data` argument, which is consistent with the mixed
seed+real data (real rows got in only during whatever launches passed the override args noted in
`App.xaml.cs`'s comments about "explicitlySeededThisLaunch" not permanently disabling the pipeline).

**(e) Action taken: NONE — deferred to master, per the task's own fallback instruction.**
I did not delete, clear, or modify the live `mostaqlk.db` or `preferences.dat` in any way. I only
read a **temporary copy** (`scratch\mostaqlk_copy.db`), which has since been deleted. Rationale:
- The app does not currently expose an app-level "clear seeded rows" path — `--seed-design-data=off`
  (see `App.xaml.cs`'s `ApplyDesignDataArgument`) only flips the `design_parity_mode` preference off;
  it does **not** call `ClearAllAsync()`, so it would not remove the 115 leftover fake rows.
- The only code path that clears project data is `DesignDataSeeder.SeedAsync()`'s
  `_projectRepository.ClearAllAsync(...)` call, which is a destructive **clear-then-reseed** — running
  it would wipe the 27 real rows too, which is a decision affecting the user's real data.
- Per the task's own guidance ("If unsure, report the finding precisely WITHOUT deleting anything and
  flag it for the master to decide"), I am flagging this for the master rather than acting
  unilaterally. Recommended options for the master to choose from: (1) add a proper
  "clear seeded rows only" repository method (e.g. `DeleteByIdRangeAsync`/`DeleteSeedDataAsync`)
  that this specific step should NOT implement since it touches production code, or (2) have the user
  manually back up `mostaqlk.db` then run a corrected `--seed-design-data=off` flow, or (3) leave the
  live store as-is if the user considers the mixed data acceptable for their current testing needs.

## Files touched / inspected
- Inspected (read-only): `Infrastructure/Database/SqliteConnectionFactory.cs`,
  `Infrastructure/Database/ProjectRepository.cs`, `Infrastructure/Database/DesignDataSeeder.cs`,
  `Infrastructure/Database/IProjectRepository.cs` (not opened in full, referenced via
  `ProjectRepository`'s implementation), `App.xaml.cs` (read-only, per the exclusion list — not
  modified), `MauiProgram.cs` (grep only).
- Created and deleted: `scratch\mostaqlk_copy.db` (temporary copy of the live DB, used only for
  read-only `sqlite3` inspection, deleted at end of session). No `scratch/db_smoke_test` directory
  was created (see "Not done" below).
- Not touched: no production code, no XAML, no `MostaqlK.UITests` files, no docs, no
  `App.xaml.cs`/`Services/Diagnostics/*` edits.

## Not done / honesty notes
- **No standalone `scratch/db_smoke_test` console program was built.** `SqliteConnectionFactory`'s
  constructor hardcodes `FileSystem.AppDataDirectory` (a MAUI Essentials static requiring a running
  MAUI host to resolve), so a plain `dotnet run` console project cannot instantiate the real
  `SqliteConnectionFactory`/`ProjectRepository` without either running inside the MAUI app itself or
  reflection-patching the private path — both felt riskier/weaker as "proof" than the code-inspection
  + live-file-activity evidence already gathered. If the master wants a literal synthetic round-trip
  test through the exact repository class, it would need either a small refactor to make the DB path
  injectable (production-code change, out of scope for this step) or an Appium-level UI test that
  drives the app itself.
- `dotnet build` was **not** re-run since no production code was touched (consistent with the task's
  own expectation).
- No files under the excluded list (`App.xaml.cs`, `Services/Diagnostics/*`, XAML,
  `MostaqlK.UITests`, `docs/master-plan-checklist.md`, `docs/ui-test-catalog.md`) were modified.

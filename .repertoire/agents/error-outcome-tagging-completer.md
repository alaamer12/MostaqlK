# error-outcome-tagging-completer

## Goal

Finish Step 7's remaining backlog from the error-handling audit: tag the ~40 `D-MissingTag`
violations disclosed as follow-up work in `docs/error-handling-audit.md` (across
`ProjectRepository.cs`, `OwnerRepository.cs`, `AssetRepository.cs`, `FtsQueryService.cs`,
`MostaqlScraper.cs`, `PollService.cs`, `DiffEngine.cs`, `AssetDownloadService.cs`,
`WindowsToastSender.cs`, `EnrichmentWorker.cs`) with `[ErrorOutcome]`, matching each site's actual
control flow, re-run the audit tool to confirm 0 remaining `D-MissingTag` for these files, update
the audit doc, and confirm a clean build.

## What I did

1. Read `docs/error-handling-audit.md` and `.repertoire/agents/error-handling-audit-builder.md` to
   understand the prior pass's scope and the exclusion list (`Services/Diagnostics/*`,
   `Infrastructure/Database/DesignDataSeeder.cs`).
2. Inspected `Core/ErrorOutcomeAttribute.cs` and `tools/ErrorHandlingAudit/Program.cs`'s `RuleD` to
   confirm the attribute must sit on the *enclosing method* (catch blocks/`Result.Err` arms can't
   carry attributes) and that the checker only verifies attribute *presence* per method, not an
   exact per-site match — so I tagged each method with one or more `[ErrorOutcome]` attributes that
   accurately describe every distinct control-flow path in that method (not a single default value).
3. Added `[ErrorOutcome(Handled|Ignored|Rethrown, Label = "...")]` to **25 methods across the 10
   files** (30 attribute instances total, since several methods have more than one distinct
   outcome):
   - `ProjectRepository.cs` (10 methods, all `Handled` — catch `SqliteException` → `Result.Err`).
   - `OwnerRepository.cs` (3 methods, `Handled`).
   - `AssetRepository.cs` (2 methods, `Handled`).
   - `FtsQueryService.cs` (1 method, `Handled`).
   - `MostaqlScraper.cs` (3 methods — `FetchListingAsync`/`FetchProjectDetailsAsync` get
     `Rethrown` + `Handled`; `GetStringAsync` gets `Handled` + `Rethrown` for the distinct
     caller-cancellation-rethrow path).
   - `PollService.cs` (2 methods — `RunLoopAsync` gets `Ignored` for the expected
     `OperationCanceledException` swallow; `PollOnceAsync` gets `Rethrown` + `Handled`).
   - `DiffEngine.cs` (1 method, `Rethrown` + `Handled`).
   - `AssetDownloadService.cs` (1 method, `Handled`; also added the missing `using MostaqlK.Core;`).
   - `WindowsToastSender.cs` (1 method, `Handled`).
   - `EnrichmentWorker.cs` (1 method, `Ignored` — deliberately tolerates `NotImplementedException`
     from not-yet-landed integration points, per the pre-existing inline comments).
4. Re-ran the audit tool (`dotnet run --project tools/ErrorHandlingAudit -- .` — note the correct
   invocation needs a positional `<rootDir>` argument, `.` for repo root, plus `--` to separate
   `dotnet run`'s own args) and confirmed `D-MissingTag` for these 10 files dropped to **0**; the
   only 11 remaining `D-MissingTag` hits are in the excluded `DesignDataSeeder.cs`/
   `InteractionLogger.cs`. Total violations dropped from 65 → 24. As a side effect, 3
   `EnrichmentWorker.cs`/`AssetDownloadService.cs` catches that were previously flagged
   `B-SwallowedCatch` (disclosed as not-yet-triaged in the prior pass) now also pass Rule B, since
   the checker exempts swallows whose enclosing method carries an explicit `[ErrorOutcome]` tag.
5. Rewrote `docs/error-handling-audit.md` to reflect the new violation count/table, documented the
   `dotnet run --project tools/ErrorHandlingAudit -- .` invocation, added a "History" section
   distinguishing Pass 1 (prior task) from Pass 2 (this task), and a "Violations fixed this pass"
   table listing every tagged method/outcome.
6. `dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0` — **0 errors, 0 warnings**.

## Not done / out of scope (unchanged from prior pass)

`Services/Diagnostics/InteractionLogger.cs` (3 sites) and `Infrastructure/Database/DesignDataSeeder.cs`
(8 sites) remain flagged but intentionally untouched per the task's exclusion list.

## Files touched

`Infrastructure/Database/ProjectRepository.cs`, `Infrastructure/Database/OwnerRepository.cs`,
`Infrastructure/Database/AssetRepository.cs`, `Infrastructure/Database/SearchIndex/FtsQueryService.cs`,
`Infrastructure/Http/MostaqlScraper.cs`, `Services/Pipeline/PollService.cs`,
`Services/Pipeline/DiffEngine/DiffEngine.cs`, `Infrastructure/Http/AssetDownloadService.cs`,
`Infrastructure/Notifications/WindowsToastSender.cs`, `Services/Pipeline/WorkerPool/EnrichmentWorker.cs`,
`docs/error-handling-audit.md`.

No new reusable unit was introduced (only existing `[ErrorOutcome]` attribute usage), so `UNITS.md`
was not modified.

## Skills used

None of the `.cursor/skills/` catalog entries matched this Roslyn-audit/attribute-tagging task
closely (same conclusion as the prior pass); proceeded directly per the explicit task instructions.

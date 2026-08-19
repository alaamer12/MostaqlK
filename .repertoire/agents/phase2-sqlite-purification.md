# Agent Execution Report: Phase 2 SQLite Purification & Schema Stabilization Retry

**Agent Name**: `phase2-sqlite-purification`  
**Task**: Step 2 (Phase 2: SQLite Schema & Storage Layer Purification - Primitives Restoration)  
**Status**: Completed  

---

## 1. Goal
Restore paired primitive separation (`publish_time_number`, `publish_time_text`, `proposal_count`, `proposal_count_text`) across the SQLite schema, models, repository, and scrapers to preserve raw scraper metadata for diagnostic fidelity, test assertions, and schema stability, while strictly preserving Single Ground principles:
1. `PublishedTimeUpdateService` remains removed and unhooked (no periodic DB rewriting).
2. `GetAllDetailsAsync` retains batched loading of related entities (`project_skills`, `assets`) to eliminate N+1 queries.
3. Scraper parsing delegates to centralized domain formatters in `Core/Formatting/`.
4. Presentation/ViewModels derive live relative time dynamically via `ArabicRelativeTime.Since(DiscoveredAt)` and canonical pluralization via `ArabicProposalParser.Format(ProposalCount)` rather than consuming static DB strings.

---

## 2. Actions Taken & Files Touched

1. **`Infrastructure/Database/SqliteConnectionFactory.cs`**:
   - Restored `publish_time_number`, `publish_time_text`, `proposal_count`, and `proposal_count_text` columns to the V1 bootstrap schema definition in `InitialSchemaSql`.

2. **`Models/ProjectSummary.cs` & `Models/ProjectDetails.cs`**:
   - Reinstated `PublishTimeNumber`, `PublishTimeText`, `ProposalCount`, and `ProposalCountText` properties on both models.

3. **`Infrastructure/Database/ProjectRepository.cs`**:
   - Updated `InsertSummaryAsync` and `UpsertDetailsAsync` SQL statements and parameter bindings to persist raw scraper primitives.
   - Updated `GetRecentAsync`, `GetDetailsAsync`, `GetAllDetailsAsync`, and `ReadSummary` to map all restored primitive columns from query readers.
   - Maintained the batched dictionary loading in `GetAllDetailsAsync` for `project_skills` and `assets` (0 N+1 queries).

4. **`Infrastructure/Database/SearchIndex/FtsQueryService.cs`**:
   - Updated FTS queries and summary projection to populate the restored primitive fields.

5. **`Infrastructure/Database/DesignDataSeeder.cs`**:
   - Initialized `PublishTimeNumber`, `PublishTimeText`, `ProposalCount`, and `ProposalCountText` on seeded model records.

6. **`Infrastructure/Http/Parsers/ListingParser.cs` & `Infrastructure/Http/Parsers/DetailParser.cs`**:
   - Wired relative time extraction to `ArabicRelativeTime.ParseRelativeNumber(text)` while populating both the numeric and raw string properties.
   - Wired proposal parsing to `ArabicProposalParser.Parse(text)` while populating both the numeric count and original text.

7. **`tools/ParserTests/Program.cs`**:
   - Added assertions to verify both numeric and raw string extraction on parsed listings and detail fixtures.

---

## 3. Verification & Results

- **Headless Parser Test Suite**:
  - Command: `dotnet run --project tools\ParserTests`
  - Output: 135/135 tests passed, 0 failed.
- **Full Windows Desktop Compilation**:
  - Command: `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -c Debug`
  - Output: Build succeeded with 0 warnings and 0 errors in ~78s.

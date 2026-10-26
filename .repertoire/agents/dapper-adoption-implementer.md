# dapper-adoption-implementer

## Goal

Adopt Dapper in the SQLite repositories (`ProjectRepository`, `OwnerRepository`, `AssetRepository`, `FtsQueryService`) as a pure implementation-detail refactor: replace manual `SqliteDataReader` ordinal-mapping loops with Dapper row DTOs + explicit DTO→domain mappers, without changing any SQL predicate/join/order/column, any returned value, default, or error path. Add the `Dapper` package to `MostaqlK.csproj`.

## Skill mandate note

`.cursor/skills/` does **not exist** in this checkout (`ls F:\Projects\Mobile\C#\MostaqlK\.cursor` → empty/missing), so no listed project skill could be read or invoked. Work followed the repo AGENTS.md conventions (scratch-script policy, CMD-preferred shell, one command at a time) and the task's own gates instead.

## Files touched

| File | Change |
|---|---|
| `MostaqlK.csproj` | Added `<PackageReference Include="Dapper" Version="2.1.89" />` (latest stable on nuget.org, verified via the flat-container index; inserted alphabetically between `CommunityToolkit.Mvvm` and `HtmlAgilityPack`, tab indentation preserved). |
| `Infrastructure/Database/RowMaps.cs` **(new)** | `internal static class RowMaps` with: (a) static constructor configuring underscore matching + a `[ModuleInitializer]` that force-runs it before any query builds a cached deserializer; (b) DTOs `ProjectKeyRow`, `ProjectSummaryRow`, `ProjectDetailsRow`, `OwnerRow`, `AssetRow`, `SkillRow`, `FtsProjectSummaryRow`, `CountersRow` — snake_case properties matching column names, DB-native types (`long` internal ids, `string?` ISO-8601 TEXT dates, `string` enrichment_status, `long?` 0/1 flags, `int?`/`double?` nullables); (c) mapper extension methods `ToSummary`, `ToDetails`, `ToOwner`, `ToAsset`, `ToFtsSummary` replicating the old reader code line-for-line. |
| `Infrastructure/Database/ProjectRepository.cs` | 8 query methods converted to `QueryAsync<T>` / `QueryFirstOrDefaultAsync<T>` / `ExecuteScalarAsync<object>` via `CommandDefinition(..., cancellationToken: ct)`; private `ReadSummary`/`ReadAsset` helpers removed (replaced by `RowMaps` mappers). All INSERT/UPDATE/DELETE/transaction paths untouched raw ADO. |
| `Infrastructure/Database/OwnerRepository.cs` | `GetByIdAsync` + `GetAllAsync` converted; `UpsertAsync`/`DeleteByIdRangeAsync` left raw. |
| `Infrastructure/Database/AssetRepository.cs` | `GetByProjectIdAsync` converted; `InsertAsync` left raw. |
| `Infrastructure/Database/SearchIndex/FtsQueryService.cs` | `SearchAsync` converted (`QueryAsync<RowMaps.FtsProjectSummaryRow>`). |

## Every mapping rule (before → after)

All "after" expressions live in `RowMaps.cs` mappers; all queries keep identical SQL text except the three explicitly flagged alias additions.

1. **Internal id → site id (projects)**: `_registry.ToSiteProjectId(rowSource, reader.GetInt64(0))` → `registry.ToSiteProjectId(rowSource, row.project_id)` (`ToSummary`, `ToDetails`).
2. **Composite key rows**: `KeyFromRow(reader.GetString(0), reader.GetInt64(1))` → `KeyFromRow(row.source_id, row.project_id)` (`GetAllKnownProjectIdsAsync`, `GetBacklogIdsAsync`, `GetNewestProjectIdAsync`).
3. **Owner id band subtraction**: `OwnerId = _registry.ToSiteProjectId(rowSource, reader.GetInt64(15))` → `registry.ToSiteProjectId(rowSource, row.owner_id.Value)` guarded by `row.owner_id is null ? new Owner() : ...` exactly mirroring `reader.IsDBNull(15)` (`ToDetails`, `ToOwner`).
4. **`EnrichmentStatus`**: `Enum.Parse<EnrichmentStatus>(reader.GetString(n))` → `Enum.Parse<EnrichmentStatus>(row.enrichment_status)`, still evaluated **inside the same `try`** (mapper runs in the method body), so a FormatException on an unknown string escapes to the caller exactly as today (no catch in these methods intercepts it — unchanged strictness).
5. **Null defaults**: `Description`/`ClientName`/`PublishTimeText`/`ProposalCountText`/`SkillsText`/`Owner.Name` null→`string.Empty` via `?? string.Empty`; `PublishTimeNumber`/`ProposalCount` null→`0` via `?? 0`; `Budget`/`ProjectStatus`/`ProfileUrl`/`AvatarUrl`/`RegisteredAt`/`RawUrl`/`LocalPath`/`Extension`/`SizeText` stay `string?` passthrough; `DeliveryDays`/`CompletedProjectsCount`/`OpenProjectsCount`/`InProgressProjectsCount`/`OngoingCommunicationsCount` `int?` passthrough; `Rating`/`HiringRatePercent` `double?`; `SizeBytes` `long?`.
6. **`DiscoveredAt`**: details projection keeps `row.discovered_at is null ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(row.discovered_at)` (mirrors `IsDBNull(14)` check); summary/FTS projections have no null check today, so they call plain `DateTimeOffset.Parse(row.discovered_at)` — same culture-free `DateTimeOffset.Parse(string)` call as before. `EnrichedAt`: null→null else `DateTimeOffset.Parse`.
7. **`is_unread`**: `!reader.IsDBNull(n) && reader.GetInt64(n) != 0` → `row.is_unread is not null && row.is_unread != 0` (`long?` DTO field, no bool conversion by Dapper). Same for `requires_auth`.
8. **`registered_at`**: stored/passed as raw TEXT (Arabic month string), never parsed — mapper passthrough, identical.
9. **`GetNewestProjectIdAsync` no-row shape**: old code used `reader.Read()==false → Ok(null)`; new uses `QueryFirstOrDefaultAsync<T>` returning null → `Ok(null)`. (Old code did *not* use ExecuteScalar here; replicated as-is.)
10. **`CountTrackedAsync`**: `(reader.GetInt32(0), reader.GetInt32(1))` → `(row.tracked, row.unread)` with `int` DTO fields; no-row → `(0,0)` preserved. **SQL change (alias only):** `SELECT COUNT(*), COALESCE(SUM(is_unread), 0)` → `... COUNT(*) AS tracked, COALESCE(SUM(is_unread), 0) AS unread` (needed for Dapper name binding; result values/order unchanged).
11. **`CountAddedTodayAsync`**: `command.ExecuteScalarAsync` → Dapper `ExecuteScalarAsync<object>`; the `result is null || result is DBNull ? 0 : Convert.ToInt32(result)` line kept verbatim.
12. **FTS summary quirk preserved**: `ProjectId = reader.GetInt64(0)` stored the **raw internal id** and `Source` was never assigned (defaults `SourceId.Default`) — `ToFtsSummary` replicates this exactly, including no `enriched_at` field in that projection.
13. **Skills**: `new ProjectSkill { Name = reader.GetString(0), Url = reader.IsDBNull(1) ? null : ... }` → `{ Name = skillRow.name, Url = skillRow.url }`; batch variant keyed on `skillRow.project_id` into the same `projectMap` (internal ids).
14. **Assets**: old static `ReadAsset` moved verbatim to `AssetRow.ToAsset()` (`Url = row.url ?? string.Empty`, `ProjectId` stays the internal row id).
15. **Parameters**: every method keeps identical parameter values, moved from `AddWithValue` to Dapper param objects (`new { source_id = source.Value, limit }`, `new { project_id = internalId }`, `new { query = enhancedQuery }`, etc.). `cancellationToken` is passed to every Dapper call through `new CommandDefinition(sql, params, cancellationToken: cancellationToken)` (string overloads have no CT parameter in 2.1.89).
16. **Error paths**: every method's `try { ... } catch (SqliteException ex) { return ...Err(DatabaseErrors.QueryFailed/CommandFailed(nameof(X), ex)); }` kept byte-identical (Dapper propagates provider `SqliteException`s, so the catch still fires). Write-on-`UpsertDetailsAsync`'s secondary generic `catch (Exception) → InteractionLogger.Fault + rethrow` untouched.
17. **Alias additions (the only 3 SQL-text edits, all `AS` on previously unnamed expressions)**: GetRecentAsync COALESCE → `AS skills_text`; FtsQueryService COALESCE → `AS skills_text`; CountTrackedAsync → `AS tracked` / `AS unread`. No predicate, join, column, or order change anywhere.

## Deviation surfaced (not decided silently)

- Task said set `Dapper.SqlMapper.Settings.MatchNamesWithUnderscores = true`. **Dapper 2.1.89 has no such member on `SqlMapper.Settings`** (build error CS0117). Used the equivalent supported flag `Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true` in the `RowMaps` static constructor (documented inline + in class remarks). With snake_case DTO properties the flag is belt-and-braces either way — exact-name matching already binds.
- Theoretical exception-shape difference on NULL-in-NOT-NULL-column values: old `GetString(n)` on NULL threw `InvalidCastException` before Dapper existed; a DTO string property would take the null and the mapper's `DateTimeOffset.Parse`/`Enum.Parse` would throw `ArgumentNullException`/`ArgumentException` instead. Both escape the same (only `SqliteException` is caught), but the exception *type* would differ. Unreachable with real data (schema `NOT NULL` on `title`, `url`, `enrichment_status`, `discovered_at`, `owners.name`, `project_skills.name`); surfaced, not "fixed".
- `FtsQueryService` raw-internal-id / missing-`Source` shape is very likely a pre-existing bug, but preserving it was contractually mandatory. Flagged for the parent agent; **not** changed.

## Commands & real output

1. `dotnet add MostaqlK.csproj package Dapper --version 2.1.89` → `Package 'Dapper' is compatible ... PackageReference for package 'Dapper' version '2.1.89' added`. (Version confirmed as latest stable from `https://api.nuget.org/v3-flatcontainer/dapper/index.json` — 2.1.89, list ends there; 1.50.x/2.0.x older.)
2. First build failed with exactly one error: `RowMaps.cs(39,28): error CS0117: 'SqlMapper.Settings' does not contain a definition for 'MatchNamesWithUnderscores'` → switched to `DefaultTypeMap` (see deviation above).
3. **Gate 1** `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0`:
   `Build succeeded. / 0 Warning(s) / 0 Error(s)`
4. **Gate 2** `dotnet run --project tools/DbMigrationTests`: 40 PASS lines (v1→v2 upgrade, re-run no-op, fresh bootstrap, composite identity) ending `ALL DB MIGRATION CHECKS PASSED`.
5. **Gate 3** `dotnet run --project tools/ParserTests`: `TOTAL: 135 passed, 0 failed / All checks passed.`
6. **Gate 5** (`sqlite3` at `D:\Android\platform-tools\sqlite3`, read-only SELECTs on `%LOCALAPPDATA%\MostaqlK\Data\mostaqlk.db`, app confirmed not running via tasklist):
   - BEFORE: `757` / `565` / `101` (projects/owners/assets)
   - AFTER:  `757` / `565` / `101`
   - Joined details row (before == after): `750770|mostaql|text|integer|integer|Enriched|2026-09-04T11:11:19.9737251+00:00|2026-09-04T11:11:34.4567978+00:00|2133472428117522|19 يونيو 2018|null` — confirms TEXT "O"-format dates, `Enriched` status text, INTEGER 0/1 types the DTOs mirror.
7. **Extra runtime proof (beyond gates)**: a throwaway console app in `%TEMP%` (outside the repo, deleted afterwards; queried a *copy* of the DB opened `Mode=ReadOnly`) replayed the exact converted SQL + DTO shapes through Dapper 2.1.89: `SMOKE: ALL PASSED` (18 checks — 20 recent rows fully bound, details join with owner bound, 565 owners, 101 assets, 5924 skills, key/backlog projections, no-row→null paths, counters 757/755, scalar Int64, FTS 486 hits with titles).

## Verification performed

- All 3 gates + before/after DB evidence above.
- `grep` confirms zero remaining `ExecuteReaderAsync`/`ReadSummary`/`ReadAsset` in the four converted files.
- `git status` shows only the five in-scope files modified + `RowMaps.cs` added; nothing committed; DB never written by any task command.

## Assumptions

- "Latest stable" = 2.1.89 per nuget.org index at run time.
- Column names reported by Microsoft.Data.Sqlite for qualified projections (`p.project_id`, `o.name`, …) are the bare names — confirmed empirically by the smoke run.
- Non-nullable DTO strings initialized to `string.Empty` only to satisfy nullable analysis; see exception-shape note.

## Open questions for the parent agent

1. `FtsQueryService.SearchAsync` feeds `ProjectSummary.ProjectId` the raw internal row id and leaves `Source` defaulted — search results may resolve to the wrong project on non-zero-band sources. Pre-existing; wants its own ticket if confirmed as a bug.
2. Should later phases convert the write commands (`ExecuteAsync` with `DynamicParameters` for the `DBNull.Value` nullable cases)? Deliberately left raw per task item 5.
3. Whether to add UNITS.md entry — not done: this is a pure data-layer change introducing no UI unit (UNITS.md catalogs UI builder blocks), and the task pre-declared "no new UI units expected".

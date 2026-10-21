# Sub-Agent Brief

## 1. Context

### Root
You are auditing a C# .NET MAUI desktop app (repo root `F:\Projects\Mobile\MostaqlK` — full path `F:\Projects\Mobile\C#\MostaqlK`, branch `v3`). The repo is mid-way through an APPROVED refactor plan called the "multi-source pre-phase": decoupling all shared code from the single scraped website (mostaql.com) so a second website (nafezly.com) can later land as a new module folder + one DI registration. The hard contract of the pre-phase: **no new functionality, no UI redesign, app behaves pixel- and byte-identically with the single source**; every Mostaql fact must live only under `Sources/Mostaql/`; shared code reads `Core/Sources/` contracts (ISourceDescriptor, SourceId, SourceProjectId, SourceRegistry) instead of hardcoding a site.

Read these first, in full:
- The approved plan: `C:\Users\amrmu\.qoder\plans\fresh-narrows-gull.md` (10 steps, target structure, verification gates, named grep-gate exceptions)
- `agents.md` and `UNITS.md` (repo rules)
- `docs` indexes only as needed: `.repertoire/.steering/base/structure.md`

Locked owner decisions (do not re-litigate): versioned DB migration `PRAGMA user_version` 1→2; **full composite identity** `SourceProjectId(SourceId, long siteId)` threaded through queue/tracker/diff/workers/backlog/notifications/routes (no banding-only at API level); neutral UI renames included in the pre-phase. Internal row id = descriptor `InternalIdBand` + site id; Mostaql band 0 keeps every legacy v1 row valid.

### Role
You are a **black-box review auditor**. You did not write this code and must trust nothing you see: verify each claim of the plan against the actual source, not against comments or names. You report findings; you fix nothing.

### Domain
Committed state under audit (commit hashes on branch `v3`):
- `ab965c8` — plan steps 1-3 (Core/Sources contracts; move of Mostaql scraper+parsers into Sources/Mostaql; rewiring PollService/EnrichmentService to ISourceDiscovery/ISourceEnrichment; deletion of IProjectScraper/MostaqlScraper; HttpErrors parameterized by site name; MauiProgram AddMostaqlSource + SourceRegistry)
- `20d4047` — plan steps 4+5 (DB v1→2 migration in Infrastructure/Database/SqliteConnectionFactory.cs + Migrations/0002_add_source_identity.sql; composite identity through ProjectRepository/OwnerRepository/DiffEngine/DiscoveryQueue/InFlightTracker/WorkerPool/EnrichmentWorker/GlobalAppStatusService; routes AppRoutes.ProjectDetails(SourceId, long) with sourceId query param and default-source fallback; toast/notification activation plumbing; tools/DbMigrationTests harness)
- `9bdf95f` — plan step 6 (Services/Pipeline/SourcePipeline.cs, SourcePipelineFactory.cs, SourcePipelineOrchestrator.cs, SourcePipelineStatus.cs; GlobalAppStatusService became a facade projecting the active source with event bridges + AnyScanning/AnyScanFailed; MauiProgram DI facade registering Active pipeline's Queue/Tracker/Limiter/IEnrichmentService/IPollService/WorkerPool; App.xaml.cs StartPipeline → Orchestrator.StartAll)

Plan steps 7-10 are NOT yet done (per-source settings namespacing, cookies/HTTP policy, UI neutral renames, BrandTokens, docs/grep-gate). Do not report step 7-10 work as missing — instead report any *structural* issue that would make those steps harder (e.g. coupling that will resist them).

Note: the working tree may contain small uncommitted cosmetic edits (doc comments/formatting) by the parent agent while you read. Audit committed HEAD (`9bdf95f`) semantics; treat uncommitted cosmetic drift as noise, not findings.

## 2. Goal
Produce an exhaustive audit report stating, step by step (1-6), whether the committed code fulfills each plan requirement, and list every violation, gap, behavioral-drift risk, or latent bug you can evidence — with file:line proof.

## 3. How

**Allowed tools / skills**
- Read, Grep, Glob freely; Bash for read-only commands only: `git log/show/diff/status`, `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0`, `dotnet run --project tools/DbMigrationTests`. You may NOT edit any file, run `git checkout/reset/commit/add`, or delete anything except your own report file (see Hard boundaries).

**Required process**
1. `git status` + `git log --oneline -5` to pin what you're auditing; note branch and HEAD hash.
2. Re-read the plan file end-to-end; build a step-1..6 checklist of every concrete requirement (target structure, contracts, deletions, migration shape, composite threading, routes, notifications, per-source instances, DI facade, status projection, version bumps dev3/dev4/dev5, UNITS.md rule).
3. For each checklist item, verify against source with targeted greps/reads. Specifically run these gates:
   - **Grep gate (as scoped for steps 1-6):** search literals `mostaql.com`, `مستقل`, `ر.س`, `MOSTAQL_COOKIE`, `https://` in `Services/`, `Core/`, `Features/`, `UI/`, `Infrastructure/` — classify every hit: allowed (inside Sources/Mostaql is NOT in these dirs; named exceptions per plan: DesignDataSeeder dev fixture, Sentry release `mostaqlk@`, product-identity strings, HttpErrors site-name is parameterized?) vs violation (shared code still hardcoding a site).
   - **Deletion check:** `IProjectScraper.cs`, `MostaqlScraper.cs`, `Infrastructure/Http/Parsers/` must not exist; no `Contains("mostaql.com")` sniffs in shared code.
   - **Composite completeness:** grep for `long projectId` / `GetDetailsAsync(` / `MarkAsReadAsync(` / `AddToBacklogAsync(` signatures and callers — no bare-long project identity crossing pipeline boundaries except inside Sources/Mostaql parsing (site-local ids are allowed at parse/URL level and in DB internal id math).
   - **DB migration:** read SqliteConnectionFactory EnsureSchema + 0002 SQL; verify stepwise runner, user_version handling, fresh-install path skips 0002, DEFAULT 'mostaql' only backfills v1 rows, unique index; run `dotnet build` and `dotnet run --project tools/DbMigrationTests` and record full output.
   - **Behavior-identity risks:** enumerate places the refactor changed a string, URL, query, preference key, notification payload, or DB write that the old app emitted (the plan demands identical behavior with one source). Include deep-link/legacy-toast fallback, route query construction, rate-limiter defaults (descriptor defaults vs old constants: 30s/2rpm), GroupingSettings, CookieStore untouched so far.
   - **DI graph:** verify no singleton/scope cycles, the facade registrations resolve the SAME instances the orchestrator started, OnboardingStateService/TrayIconService/SettingsViewModel/StatusBarViewModel bind to Active pipeline objects; GlobalAppStatusService.Attach called before first window (eager orchestrator construction in MauiProgram after Build).
   - **Status projection:** verify the dispatcher/teardown guard is preserved in BOTH SourcePipelineStatus and GlobalAppStatusService; event bridging attaches/detaches correctly; worker arrays length 3 per source; backlog rehydration filtered per source.
4. For each finding: severity (Critical = breaks behavior/data/migration, Major = plan violation, Minor = drift risk, Nit = style), file:line evidence, and the plan line it violates.
5. Write your report file `.repertoire/agents/prephase-blackbox-auditor.md` (this path is the ONLY file you may create/write).

**Hard boundaries**
- Read-only everywhere except your single report file `.repertoire/agents/prephase-blackbox-auditor.md`.
- Do not touch any other file, git state, build outputs beyond normal `dotnet build` artifacts, or the live user database.
- Do not fix anything, even a typo you find. Report it.
- Do not audit step 7-10 deliverables as missing; do not propose redesigns.

## 4. Autonomy Bounds
You are an executor, not a decision-maker.
- If a choice appears, surface the options and stop.
- Do not expand scope. Do not start related work.
- Do not invent requirements, architecture, or product decisions.

## 5. Output Contract
1. Emit every raw search result, tool output, and intermediate finding in full inside the conversation.
2. Do not summarize or omit evidence.
3. The final report must be exhaustive, not a digest:
   - Every file touched/read that mattered, with exact paths and what you verified in it.
   - Every command run and its exact output, including failures, retries, and warnings.
   - Every assumption made, and why.
   - Every decision point and how it was resolved (or surfaced instead of resolved).
   - Anything unexpected (file state, stale data, conflicting instruction) even if it didn't block you.
   - Nothing is "too minor to mention."
4. End with exactly this structure:

### Raw Evidence
(all evidence already shown above)

### Findings
- per plan step 1..6: satisfied / violated, with file:line proof; then a defect list with severities

### Open Questions / Blockers
- ...

### Suggested Next Actions for Parent
- ...

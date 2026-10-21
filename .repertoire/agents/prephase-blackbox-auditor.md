# prephase-blackbox-auditor — Multi-source pre-phase steps 1–6 black-box audit

## Goal (as assigned)

Verify committed state on branch `v3` at HEAD `9bdf95f` (steps 1–3 `ab965c8`, steps 4+5 `20d4047`, step 6 `9bdf95f`)
against the approved plan `C:\Users\amrmu\.qoder\plans\fresh-narrows-gull.md`, step by step, with file:line evidence.
Steps 7–10 deliverables are out of scope except where structural coupling would resist them. Read-only audit; fixes forbidden.

## Environment notes / anomalies

- `git status` at session start: `On branch v3 ... nothing added to commit but untracked files present: scratch/`.
  Mid-session, a SECOND `git status --porcelain` showed tracked modifications appearing underneath me:
  ` M Core/Sources/BrandTokenSet.cs`, ` M Sources/Mostaql/MostaqlSource.cs`, `RM UI/PlatformComponents/MostaqlLinkButton/* -> UI/PlatformComponents/SourceLinkButton/*`,
  ` M tools/DbMigrationTests/Program.cs`, `?? UI/DesignSystem/BrandTokens/`. This is the parent agent concurrently implementing plan steps 8/9
  in the same working tree. Per global AGENTS.md I did not "restore" anything; I audited committed HEAD semantics
  (`git show HEAD:<file>` used to confirm e.g. `BrandTokenSet` is 4-field at HEAD while the working tree shows a 6-field WIP).
- Because of that concurrent edit, the mandated `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` compiles a
  MIXED tree (ProjectCardWindowsLayout.xaml already switched to `SourceLinkButton` while ProjectDetails{Windows,Mobile}Layout.xaml
  still reference `MostaqlLinkButton`, whose folder is rename-staged). The build failure output below is attributable to the
  in-flight step-8 work, NOT to HEAD `9bdf95f`. I could not reproduce the HEAD-only build without mutating git state (forbidden).
- One temporary file `scratch/old_App.xaml.cs.txt` was created by me during early diffing (before the no-file-creation constraint
  bit) and deleted immediately after use. No other file besides this report was created or modified.

## Commands run (full outputs)

### git log / status

```
$ git status
On branch v3
Your branch is ahead of 'origin/v3' by 8 commits.
nothing added to commit but untracked files present (use "git add" to track)
	scratch/

$ git log --oneline -12
9bdf95f feat: per-source pipeline instances with active-projection status facade
20d4047 feat: composite source identity through storage and pipeline boundaries
e376c2e fix: pin IDE0051/IDE0052 to suggestion - auto-removal breaks platform-partial members in MAUI multi-targeting
e305625 style(csharp): apply dotnet format whitespace and accessibility-modifier pass (tool-only files; agent-in-progress files excluded)
e999a29 style(docs): apply markdownlint autofixes and structural heading/fence corrections across all markdown
3f1c884 chore: add dotnet-outdated, dotnet format + editorconfig lint, markdownlint tooling
5a3ba86 build: set up DocFX documentation site generation for the MAUI app
ab965c8 refactor: introduce source contracts and move Mostaql into its own module
3a47dae docs: add plan
e00de46 docs: add v3 source-morph UI design proposal and mockups
bf881b4 docs: postmoterm of the fix
5a78058 feat: change how versioning works, to ship the db into next update
```

Commit file stats were captured for `ab965c8`, `20d4047`, `9bdf95f` (shown in tool logs; all files enumerated there were read or diffed).

### dotnet build (FAILED — attributable to concurrent step-8/9 working-tree state, not HEAD)

```
$ dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0
Determining projects to restore...
  All projects are up-to-date for restore.
  XAML source generation is enabled (MauiXamlInflator=SourceGen). ...
error MAUIG1001: ... ProjectCardWindowsLayout.xaml(1,2): The given key 'Microsoft.Maui.Controls.Xaml.ElementNode' was not present in the dictionary.
error MAUIG1001: ... ProjectDetailsMobileLayout.xaml(1,2): (same)
error MAUIG1001: ... ProjectDetailsWindowsLayout.xaml(1,2): (same)
error CS8795: Partial method 'ProjectCardWindowsLayout.InitializeComponent()' must have an implementation part ...
error CS8795: Partial method 'ProjectDetailsMobileLayout.InitializeComponent()' ...
error CS8795: Partial method 'ProjectDetailsWindowsLayout.InitializeComponent()' ...
    0 Warning(s)
    6 Error(s)
Time Elapsed 00:00:10.59
```

Attribution evidence: `grep MostaqlLinkButton|SourceLinkButton Features/Projects/Views/Layouts/*.xaml` shows
`ProjectCardWindowsLayout.xaml:189 <components:SourceLinkButton` (already renamed, working-tree modified per git status)
while `ProjectDetailsMobileLayout.xaml:102` / `ProjectDetailsWindowsLayout.xaml:164` still use `<components:MostaqlLinkButton`
whose folder is staged-renamed to `SourceLinkButton/`. Mixed in-flight state. HEAD `9bdf95f` itself contained a consistent
`MostaqlLinkButton` everywhere (commit stats touch no layout files).

### dotnet run --project tools/DbMigrationTests (PASSED)

```
== v1 -> v2 upgrade ==
  PASS  user_version is 2 after upgrade (got 2)
  PASS  projects has source_id + site_project_id
  PASS  owners has source_id
  PASS  discovery_backlog has source_id
  PASS  unique index ux_projects_source_site exists
  PASS  all 3 v1 rows backfilled to source mostaql with site_project_id = project_id
  PASS  project rowids (internal ids) unchanged - child/FTS references stay valid
  PASS  projects row count unchanged (3 -> 3)
  PASS  owners row count unchanged (1 -> 1)
  PASS  project_skills row count unchanged (3 -> 3)
  PASS  assets row count unchanged (1 -> 1)
  PASS  discovery_backlog row count unchanged (1 -> 1)
  PASS  projects_fts row count unchanged (3 -> 3)
  PASS  owner row backfilled to mostaql
  PASS  backlog row backfilled to mostaql
== re-run is a no-op ==
  PASS  user_version stays 2
  PASS  projects row count unchanged on re-run (3 -> 3)
  PASS  owners row count unchanged on re-run (1 -> 1)
  PASS  project_skills row count unchanged on re-run (3 -> 3)
  PASS  assets row count unchanged on re-run (1 -> 1)
  PASS  discovery_backlog row count unchanged on re-run (1 -> 1)
  PASS  projects_fts row count unchanged on re-run (3 -> 3)
== fresh install bootstraps at v2 ==
  PASS  fresh db reaches user_version 2 without running the 0002 step
  PASS  fresh projects table already carries the v2 columns
  PASS  insert without source_id rejected on fresh schema
== composite identity across sources ==
  PASS  mostaql band 0 keeps site id as internal id (v1 rows untouched)
  PASS  colliding site ids map to distinct internal ids (101 vs 1000101)
  PASS  round-trip mostaql
  PASS  round-trip nafezly
  PASS  same site id stored once per source without collision
  PASS  committed set holds both composites distinctly
  PASS  duplicate (mostaql, 101) rejected by ux_projects_source_site
ALL DB MIGRATION CHECKS PASSED
```

(Retries: none needed; run succeeded first time. Note it compiled against the working-tree `BrandTokenSet.cs`/`Program.cs`
which were consistent 6-field versions at run time — the harness itself passed.)

### grep gates (full hit lists)

`mostaql.com` / `MOSTAQL_COOKIE` under Services/, Core/, Features/, UI/, Infrastructure/ — 18 raw hits, in
CookieJar.cs (env var + comments, L22/43/69/72/76), AssetDownloadService.cs (L46/86/133/135), DesignDataSeeder.cs
(L208/322/327), ISourceDescriptor.cs (L16/63 comments), SettingsViewModel.cs L91 (comment),
ProjectCardWindowsLayout.xaml L186 (comment), AboutPage{Mobile,Windows}Layout.xaml.cs L29/L49 (`https://mostaql.com`),
ProjectDetailsViewModel.cs L238, ProjectCardViewModel.cs L54 (comment)/L90.
Classification in Findings.

`مستقل` / `ر.س` — 24 raw hits (full list in tool log): toast action text (WinRtVariation L70/108, WinAppSdkVariation L143/162,
ToastActivator L19 comment, NotificationUrlLauncher L7 comment), Core/Sources/ISourceDescriptor.cs L13/L28 (doc examples),
Core/Formatting/BudgetFormatter.cs L9 (comment)/L16 (`public const string CurrencySuffix = "ر.س";`),
MostaqlLinkButton.xaml L18 (`عرض في مستقل`), SettingsPanelWindowsLayout.xaml L174 (query-params helper copy naming مستقل) and
L250 (`كوكي جلسة مستقل (Mostaql Session Cookie)`), ProjectCardViewModel L54/114 (comments), ProjectDetailsViewModel L188 (comment),
PressableEffect.Windows.cs L23/40 (comments), OnboardingPage.xaml L25 (`مستقلك`), OnboardingViewModel L55 (`نرصد مشاريع مستقل`),
ExitConfirmationBox.cs L29 (`مستقلك`). Classification in Findings.

`https://` — 8 raw hits: DesignDataSeeder L208/322/327, AboutPage layouts L49/L29, ProjectDetailsViewModel L238,
ProjectCardViewModel L90. (Sentry DSN lives in MauiProgram.cs, outside the five gated trees.)

Deletion check: `ls` + `git ls-files` confirm `Infrastructure/Http/IProjectScraper.cs`, `Infrastructure/Http/MostaqlScraper.cs`,
`Infrastructure/Http/Parsers/` do not exist; code-wide grep for `IProjectScraper|MostaqlScraper|Infrastructure.Http.Parsers`
finds only two stale doc comments (Models/ProjectDetails.cs L51, Services/CookieStore.cs L11).
`Contains("mostaql.com")` in shared code exists in exactly one live site: Infrastructure/Http/AssetDownloadService.cs:133
(step 7 assigns it to `descriptor.IsSameSiteUrl`; `ISourceDescriptor.IsSameSiteUrl` currently has zero app consumers).

Composite completeness: grep for `long projectId` params in Features/Services shows no bare-long project identity crossing a
pipeline/storage boundary; remaining `long` uses are site-local parse values (IDetailStrategy.ParseDetail doc says so,
Core/Sources/ISourceStrategies.cs:21-22), internal-id math (SourcePipelineStatus.Token / TitleOf(long)), and
`ProjectDetailsPage.ProjectId` string query property parsed into `SourceProjectId` (ProjectDetailsPage.xaml.cs:61-63).

## Files read/examined in full or in relevant parts

Plan file (fresh-narrows-gull.md); Core/Sources/* (all 10 files); Sources/Mostaql/* (all files); Services/Pipeline/:
PollService, IPollService, EnrichmentService, IEnrichmentService (diff), DiscoveryQueue, InFlightTracker, TokenBucketRateLimiter,
HttpErrors.cs, DiffEngine/{DiffEngine,DiffResult,IKnownStateProvider,InFlightSetProvider,SqliteCommittedProvider},
WorkerPool/{WorkerPool,EnrichmentWorker,EnrichErrors}, SourcePipeline, SourcePipelineFactory, SourcePipelineOrchestrator,
SourcePipelineStatus; Services/{GlobalAppStatusService, CookieStore(grep), NotificationDispatcher(grep)}; Models/{ProjectSummary,Owner,ProjectDetails};
Infrastructure/Database/{SqliteConnectionFactory (+pre-20d4047 version), Migrations/0002_add_source_identity.sql, Migrations/README.md,
IProjectRepository, ProjectRepository, IOwnerRepository, OwnerRepository, DesignDataSeeder (diff)};
Infrastructure/Http/{HttpErrors, CookieJar, AssetDownloadService}; Infrastructure/Notifications (20d4047 diffs);
Core/Navigation/AppRoutes.cs; App.xaml.cs (+old version diff); AppShell.xaml.cs + StartupNavigation (diff);
MauiProgram.cs (+pre-refactor version full); Features VMs diffs (Feed/Details/Card/Settings/Notifications);
ProjectDetailsPage.xaml.cs; Tools: DbMigrationTests/{Program.cs,csproj,AppPathsShim.cs}; UNITS.md (targeted greps + section read);
MostaqlK.csproj version diffs for all three commits; deleted `ab965c8^:Infrastructure/Http/MostaqlScraper.cs` for behavior comparison;
old `GlobalAppStatusService` (9bdf95f^) for literal comparison.

Skill used: `using-agent-skills` catalog was already loaded via repo AGENTS.md; per-brief process followed the audit workflow
(this is a read-only audit; `code-review-and-quality`-style evidence discipline).

## Decisions made during the audit

- Treated the "band math instead of table rebuild" schema shape as governed by the LOCKED owner decision ("Internal row id =
  descriptor InternalIdBand + site id; Mostaql band 0 keeps every legacy v1 row valid") rather than by the plan step-4 prose
  (`id INTEGER PRIMARY KEY` rebuild). Judged implementation vs that locked decision + plan Verification §3 wording
  (`site_project_id=project_id` matches the implementation's column naming). Reported the prose divergence for transparency.
- Classified every grep hit whose fix is explicitly assigned to plan steps 7–9 (AssetDownloadService sniff/UA/user text → step 7;
  VM fallback URL builders, toast button text, MostaqlLinkButton label, BudgetFormatter suffix → step 8) as allowed-deferred,
  not as missing deliverables, and separately as *coupling to watch* per the brief.
- Build failure attributed to concurrent parent edits with file-level evidence (mixed SourceLinkButton state); HEAD-build
  verdict recorded as unverifiable-in-place rather than as passing.
- Appium gate (plan verification §2) and manual smoke (§5) are outside my allowed tooling; not run, recorded as open item.

---

# FINAL REPORT

## Step-by-step verdicts

### Step 1 — Contracts (`Core/Sources/`): SATISFIED, 2 nits + 1 placement divergence

| Requirement | Status | Evidence |
|---|---|---|
| SourceId readonly record struct, const Mostaql | satisfied | Core/Sources/SourceId.cs:8, :38; also `Default` (:44) and TryFrom normalization (:25-35) |
| SourceProjectId(SourceId, long) | satisfied | Core/Sources/SourceProjectId.cs:9 |
| ISourceDescriptor (id, DisplayNameAr, Domain, RailGlyph, Brands, cadence/rate defaults, CurrencySuffix, OwnerFallbackMeta, CookieSecretKey, SettingsContainerId, BuildProjectUrl/BuildAbsoluteUrl, IsSameSiteUrl) | satisfied | Core/Sources/ISourceDescriptor.cs:11-64 (all named members present + InternalIdBand :52) |
| SourceCapabilities | satisfied | Core/Sources/SourceCapabilities.cs:8-11 |
| ISourceModule | satisfied | Core/Sources/ISourceModule.cs:11-16 |
| ISourceDiscovery / ISourceEnrichment | satisfied | Core/Sources/ISourceDiscovery.cs:10-30 (both in one file — naming divergence vs tree's separate bullets; nit) |
| IListingStrategy / IDetailStrategy | satisfied | Core/Sources/ISourceStrategies.cs:9-23 (consolidated file `ISourceStrategies.cs`, tree said `IListingStrategy.cs / IDetailStrategy.cs`; nit) |
| SourceRegistry (dict + Enabled list) | satisfied | Core/Sources/SourceRegistry.cs:8-49; `All` is registration-order list (:24), no separate "Enabled" concept — acceptable at single-source; band math helpers added in 20d4047 (:40-48) |
| ISettingsRepository | satisfied (contract only) | Core/Sources/ISettingsRepository.cs:9-19 + SettingsKeys (:26-42). Implementation lands with step 7 per plan. |
| BrandTokenSet placement | divergence | Target structure puts `BrandTokenSet record + IBrandTokenProvider` under `UI/DesignSystem/BrandTokens/` (plan :43); record created at HEAD in Core/Sources/BrandTokenSet.cs instead (rationale in its own doc: "kept platform-neutral (Core must not produce MAUI Colors)"). Harmless today; the step-9 tree line will still need the provider (parent is creating `UI/DesignSystem/BrandTokens/` now, uncommitted). Severity: Nit (placement), content present. |

Contract-consumption note: descriptor members `RailGlyph`, `Brands`, `CurrencySuffix`, `OwnerFallbackMeta`, `Capabilities`,
`CookieSecretKey`, `SettingsContainerId`, `BuildAbsoluteUrl`, `IsSameSiteUrl` have **zero app consumers at HEAD**
(grep across repo — only Sources/Mostaql impls + tools/DbMigrationTests stub). This is expected (consumers arrive with
steps 7–9) but means "shared code reads contracts instead of hardcoding" is only partially exercised at this commit:
`BuildListingUrl`, `BuildProjectUrl`, `DisplayNameAr`, `Id`, `DefaultPollIntervalSeconds`, `DefaultRequestsPerMinute`
are the only members actually read by shared code (SourcePipelineFactory.cs:49,:57; MostaqlDiscovery/MostaqlEnrichment/Fetcher).

### Step 2 — Move-only commit: PROCESS DEVIATION (steps 1+2+3 executed as one commit `ab965c8`)

- Plan mandated a separate move-only commit with a *temporary `IProjectScraper` adapter* so Poll/Enrichment stayed untouched
  (plan :54-55). No such commit exists in history (`git log`: ab965c8 covers steps 1-3 combined, adapter never created, seam deleted in the same commit).
  End state matches step 3, so this is a granularity/process deviation only. Severity: Nit.
- Files moved: `git show --stat ab965c8` lists the six parser files as renames into `Sources/Mostaql/Parsing/`
  (DetailParser, InferenceEngine, ListingParser, ParseErrors, ParseException, StructuralExtractor) with namespace-only diffs (2 +- lines each).

### Step 3 — Contracts wired; seam deleted: SATISFIED, 3 nits

| Requirement | Status | Evidence |
|---|---|---|
| MostaqlModule.AddMostaqlSource registers descriptor + discovery/enrichment + strategies | satisfied | Sources/Mostaql/MostaqlModule.cs:14-34; descriptor singleton + SourceRegistry at MauiProgram.cs:126-128 |
| PollService rewired to ISourceDiscovery | satisfied | Services/Pipeline/PollService.cs:12,49,163 |
| EnrichmentService rewired to ISourceEnrichment | satisfied | Services/Pipeline/EnrichmentService.cs:11,17,27 |
| IProjectScraper.cs + MostaqlScraper.cs deleted | satisfied | git ls-files / ls of Infrastructure/Http — only AssetDownloadService, CookieJar, HttpErrors remain |
| URL building/absolutization behind descriptor | mostly satisfied | Listing/detail URL building via `MostaqlSource.BuildListingUrl/BuildProjectUrl` (MostaqlSourceServices.cs:35,82). NOT done: `DetailParser.cs:610,627` still inline `"https://mostaql.com" + ...` absolutization (plan :58 named these lines); `descriptor.BuildAbsoluteUrl` exists (:66) with zero consumers. Allowed (inside module) but plan line unexecuted. Severity: Minor. Also a latent inconsistency: parser absolutization adds a `/` when missing (`(url.StartsWith('/') ? "" : "/")`) while `BuildAbsoluteUrl` returns non-slash input unchanged — the two implementations differ. |
| HttpErrors messages take site name from descriptor, codes unchanged | satisfied | Infrastructure/Http/HttpErrors.cs:12-46 (siteName parameter; HTTP-001..006 codes unchanged per diff ab965c8); substitution with "مستقل" reproduces the old Arabic strings verbatim (verified char-by-char against `git show ab965c8^` version). NotFound (HTTP-004) has no site name — same as before. |
| MauiProgram:123 becomes AddMostaqlSource + SourceRegistry | satisfied | MauiProgram.cs:126-128 |
| Behavior identity vs old MostaqlScraper | satisfied with 1 nit | Fetched URL shapes identical (`https://mostaql.com/projects` + normalized `?query`, `https://mostaql.com/project/{id}`); 15 s timeout identical (MostaqlFetcher.cs:16); cookie attach identical (MostaqlFetcher.cs:35-39 vs old static CookieHeader). Nit: parse-failure path now passes the *query-appended* URL to `HttpErrors.ParseFailed` (MostaqlSourceServices.cs:57) where old code passed the bare `ListingUrl` (MostaqlScraper) — InternalMessage text drift only (user-visible ExternalMessage unchanged). |
| — | nit | Stale doc references to deleted seam: Services/CookieStore.cs:11 ("MostaqlScraper"), Models/ProjectDetails.cs:51 ("Infrastructure.Http.Parsers.DetailParser"). |
| — | nit | `ISourceModule` is registered (MauiProgram via MostaqlModule.cs:30) but never resolved/consumed anywhere; `AddMostaqlSource` also calls `new MostaqlModule().Register(services)` directly (MostaqlSourceServices…MostaqlModule.cs:31), bypassing the planned module hook and creating two parallel registration paths. Dead-but-confusing surface for step 7+. |

### Step 4 — DB migration + composite storage: SATISFIED with 2 Major constraint gaps and Minor notes

Runner (`Infrastructure/Database/SqliteConnectionFactory.cs`):
- Stepwise `user_version` runner replaces throw-on-mismatch: :62-106 (0→bootstrap, 1→0002 step, >2 throws :73-76), each step in a
  transaction (:126-134), version set after (:91). Fresh installs land at v2 via extended `InitialSchemaSql` (:190-266, `site_project_id`
  NOT NULL :194 + `ux_projects_source_site` :213) and skip 0002 (comment :80-81, harness-proven).
- `DEFAULT 'mostaql'` on the added columns (:177,:181,:182) only backfills pre-existing v1 rows (fresh schema has NO default →
  source-blind inserts rejected — harness check "insert without source_id rejected on fresh schema" PASS).
- Migrations/0002_add_source_identity.sql :9-14 is byte-consistent with the executable `Migration0002Sql`.
- Idempotent re-run: version==2 → no steps executed; harness "re-run is a no-op" PASS.
- Pre-migration DBs truly have `user_version=1`: verified old factory (20d4047^ `EnsureSchema`) sets it right after bootstrap.
  Residual edge (Minor): any hypothetical DB with tables present but `user_version=0` (mid-crash in a v1 build between
  RunInitialMigration and SetUserVersion — both ran in one EnsureSchema but the CREATE step committed separately from the PRAGMA)
  would be misclassified as fresh: CREATE IF NOT EXISTS no-ops, version stamped 2, and the v1-shaped tables then fail every
  repository query (`site_project_id` missing). Narrow, but the runner has no "version 0 with tables" guard.

Repositories/identity:
- All IProjectRepository methods composite-typed (Infrastructure/Database/IProjectRepository.cs:14-93; 17 methods — plan said 16,
  one extra from a pre-existing split; every id-bearing method takes `SourceId`/`SourceProjectId`). OwnerRepository likewise
  (IOwnerRepository.cs:13-27).
- `SqliteCommittedProvider`/`IKnownStateProvider` return `IReadOnlySet<SourceProjectId>` (DiffEngine/IKnownStateProvider.cs:12,
  SqliteCommittedProvider.cs:19, InFlightSetProvider.cs:18) — cross-source suppression bug class fixed (harness "committed set holds both composites distinctly" PASS).
- `ProjectSummary`/`ProjectDetails`/`Owner` gained `SourceId Source` (Models/ProjectSummary.cs:12, ProjectDetails.cs:13, Owner.cs:11).

**Violations vs plan step-4 wording (both Major-class plan deviations, mitigated by the locked banding decision):**
1. `owners`: plan (:63) requires "add `source_id`, unique `(source_id, owner_id)`". The `source_id` column is added
   (SqliteConnectionFactory.cs:181) but **no unique index on owners exists in either Migration0002Sql (:176-183) or InitialSchemaSql
   (:215-229)**. Identity relies solely on the banded `owner_id` PK (:108-110,20). If two future sources' `InternalIdBand` ranges ever
   overlap (bands are plain `long` with no disjointness validation anywhere — `SourceRegistry.Band` :48), FNV-hashed owner ids would
   silently cross-overwrite between sources (ON CONFLICT(owner_id) DO UPDATE, OwnerRepository.cs:41).
2. `discovery_backlog`: plan (:64) requires "rows keyed `(source_id, project_id)`". Implementation keeps
   `PRIMARY KEY (project_id)` alone (SqliteConnectionFactory.cs:253; migration only ADDs `source_id` :182); no composite key/index.
   Same banding-collision dependency; `RemoveFromBacklogAsync` (ProjectRepository.cs:539) and `AddToBacklogAsync` (:519) key on internal id only.

Other notes:
- Shape divergence from plan prose (:62: "rebuild as `id INTEGER PRIMARY KEY` … + `project_id INTEGER NOT NULL` (site id)"):
  implementation keeps `project_id` = internal id and stores site id in `site_project_id`. This is **consistent with the plan's own
  Verification §3 wording and with the locked band decision**, and avoids the rebuild entirely; recorded as resolved-in-implementation, not a defect.
- Migrated DBs get nullable `site_project_id` (ALTER limitation, :178) vs NOT NULL on fresh (:194) — asymmetry documented in :168-175
  comment; repositories always write it, so benign.
- `Migrations/README.md` (entire file) still describes the pre-0002 regime ("Until then, any unexpected user_version causes
  DatabaseSchemaException") — stale now that 1→2 exists; step 10 doc pass territory. Also both README:19 and
  SqliteConnectionFactory.cs:188 reference `Migrations/0001_initial_schema.sql`, **which does not exist in git**
  (`git ls-files Infrastructure/Database/Migrations` → only 0002 + README).
- Harness gap vs plan verification §3: no `GetRecentAsync returns identical rows/order` check and no live-DiffEngine suppression
  test (the harness asserts the committed-set membership at SQL level only, tools/DbMigrationTests/Program.cs:181-194). Minor.

### Step 5 — Composite through pipeline + boundaries: SATISFIED, minor drift risks

- `DiscoveryQueue` → `Channel<SourceProjectId>` (DiscoveryQueue.cs:15-28). `InFlightTracker` → `ConcurrentDictionary<SourceProjectId,byte>`
  (InFlightTracker.cs:13). `WorkerPool`/`EnrichmentWorker`/`IEnrichmentService.EnrichAsync` take `SourceProjectId`
  (IEnrichmentService.cs:15, EnrichmentWorker.cs:119, EnrichErrors.cs:12,24).
- Backlog rehydration source-scoped (WorkerPool.cs:54-64: `if (projectId.Source != _status.Source) continue;`).
- `IPollService` shape unchanged (IPollService.cs:9-45) — PollService stamps source via `ISourceDiscovery` results
  (PollService.cs:193-196 keys `SourceProjectId(summary.Source, summary.ProjectId)`; summary.Source stamped inside module,
  MostaqlSourceServices.cs:47-50).
- Routes: `AppRoutes.ProjectDetails(SourceId, long)` (AppRoutes.cs:56-59) → for the **default source emits the exact old string**
  `ProjectDetailsPage?projectId={id}` (sourceId omitted), non-default emits `?sourceId=<escaped>&projectId=`. Plan prose said
  `?sourceId=&projectId=` always; the implemented conditional is *more* behavior-identical (old deep links byte-identical) and is
  documented at AppRoutes.cs:51-55 + WinAppSdkVariation diff comment. Resolved as deliberate, acceptable.
- `ProjectDetailsPage` `[QueryProperty("projectId"), QueryProperty("sourceId")]` with absent-sourceId → `SourceId.Default`
  (ProjectDetailsPage.xaml.cs:9-10,42-44). `StartupNavigation` gained optional `SourceId` + `--source-id=` arg
  (AppShell.xaml.cs diff :44,67-70) and AppShell resolves default fallback (:26-29).
- Notifications: toast arguments/activation carry SourceId with default fallback in all three paths
  (ToastActivator.Windows.cs diff; WinAppSdkVariation.Windows.cs:134-140 `AddArgument("sourceId", …)` only for non-default →
  mostaql payloads byte-identical; WinRtVariation.Windows.cs diff `sourceParam` empty for default → launch string byte-identical).
  `NotificationCenterViewModel`/`NotificationItemViewModel` expose `Source` and navigate with the composite
  (NotificationCenterViewModel.cs:82; NotificationItemViewModel.cs Source property).
- Behavioral-drift inventory (strings/queries/payloads changed vs pre-refactor):
  - Enrichment internal log/error messages now read `Project mostaql/101 …` instead of `Project 101 …`
    (EnrichErrors.cs:14,26; old form in `git show 20d4047` diff). ExternalMessage + codes unchanged. Severity: Minor (logs only).
  - `PollService.MissingSummary` telemetry payload now serializes composite ToString `mostaql:101` (PollService.cs:203). Minor (logs only).
  - ParseFailed internal URL includes query params (step 3 nit above). Minor.
  - Rate-limiter defaults: descriptor supplies 30s/2rpm (MostaqlSource.cs:26-28) and factory wires them
    (SourcePipelineFactory.cs:48-57) — BUT shared code still duplicates the same constants: `TokenBucketRateLimiter.DefaultRequestsPerMinute = 2`
    (TokenBucketRateLimiter.cs:27, used as SettingsViewModel default at SettingsViewModel.cs:41), `PollService.PollIntervalSeconds = 30`
    initializer (PollService.cs:30 — overwritten to descriptor value by the factory, so inert), `SourcePipelineStatus.ScanIntervalSeconds = 30`
    (:113). Values are identical so behavior is identical; the duplicated literals are a Minor coupling that can diverge per-source later.
  - Cookie store paths: CookieStore still uses the single flat `SecretRepository.MostaqlCookieKey`
    (Services/CookieStore.cs:44,53,70,85) while `descriptor.CookieSecretKey` ("cookie_mostaql") has no consumer — step 7 assigned; coupling flagged.
  - Grouping/notification budget: NotificationGrouper/Dispatcher untouched except source passthrough — unchanged.
  - Dead error-handling labels: EnrichmentWorker.cs:118,188-193,205-208 still catch `NotImplementedException` with comments
    "Step 4/5 not yet landed" — those steps HAVE landed (20d4047); the catches are now dead code and the `[ErrorOutcome(Ignored)]`
    attribute documents a condition that can no longer occur. Nit.

### Step 6 — Per-source pipelines + status projection: SATISFIED, 1 Major (unwired aggregate) + nits

- `SourcePipeline.cs` (bundles queue/tracker/limiter/enrichment/poll/pool/status per source :16-53);
  `SourcePipelineFactory.cs` (per-source private instances :40-63, descriptor-supplied defaults, raw flat Preferences reads for
  rpm/safe-requests explicitly annotated as step-7 pending :46-50); `SourcePipelineOrchestrator.cs`
  (one pipeline per registry descriptor, skip-without-half-wiring :36-45, Active = default source :47-53, `Attach` called in ctor :53,
  `For(SourceId)` :61, `StartAll` :75-108 — plan said `StartAllAsync`; implemented sync-with-Task.Run, same semantics; Nit).
- App startup: `App.StartPipeline` → `Orchestrator.StartAll(_pipelineCts.Token)` (App.xaml.cs:148-159); persisted-interval clamp
  (10,3600), pause seeding, `--debug-via-json` force, and the no-UI-SyncContext `Task.Run` rule are **line-for-line equivalent** to the old
  App.StartPipeline (compared against `git show ab965c8^:App.xaml.cs` / 9bdf95f diff). Per-source filter for the saved interval
  (`if (pipeline.Source == SourceId.Default)`, Orchestrator:86-92) preserves single-source behavior.
- DI facade: MauiProgram.cs:151-158 registers factory+orchestrator and projects the **Active** pipeline's Queue/Tracker/Limiter/
  IEnrichmentService/IPollService/WorkerPool as the singletons consumers inject; all consumers (SettingsViewModel.cs:125-131,
  StatusBarViewModel.cs:36, TrayIconService.cs:66, OnboardingStateService.cs:15, feed/VMs) resolve through the same orchestrator
  singleton → same instances the orchestrator starts. Removed singletons (DiffEngine, SqliteCommittedProvider, InFlightSetProvider,
  EnrichmentService, PollService, WorkerPool, DiscoveryQueue, InFlightTracker, TokenBucketRateLimiter) have no remaining
  container consumers (grep for GetRequiredService/injection of those types outside the factory: none).
  No singleton resolution cycles traced: Orchestrator→(Factory→repos/AssetDownload/INotificationDispatcher→GlobalStatus→Registry),
  Orchestrator→GlobalAppStatusService(SourceRegistry only, GlobalAppStatusService.cs:28) — no back-edge into pipeline types.
  Eager construction before first window: MauiProgram.cs:219-222 (`_ = app.Services.GetRequiredService<SourcePipelineOrchestrator>()`
  right after Build) — Attach therefore precedes any window binding.
- Status projection: `SourcePipelineStatus` is a faithful extraction of the old `GlobalAppStatusService` telemetry — dispatcher-marshalling +
  teardown guard preserved **in both** classes (SourcePipelineStatus.cs:40-64; GlobalAppStatusService.cs:137-161), worker arrays length 3
  per instance (:85-93), QueueCapacity 50 (:97), ConnectionText Arabic strings identical to `git show 9bdf95f^` (:152), MaxRememberedTitles 512.
  Event bridge attach/detach on projection switch (GlobalAppStatusService.cs:92-105, :297-327); unread badge + ProjectsAddedToday remain
  global-vs-projected per commit intent (:168-192); `ProjectsAddedToday` callers unchanged (SetProjectsAddedToday from
  ProjectFeedViewModel.cs:228 and SettingsViewModel.cs:492 — both forward to the active source; single-source semantics unchanged).
  Radar/dashboard event signatures still use the internal `long` token — band 0 → numerically identical to old raw ids (SourcePipelineStatus.cs:32,194-201).
- **Major deviation**: plan :72 requires "`AnyScanning`/`AnyError` aggregates **for the tray**". Both aggregates exist
  (GlobalAppStatusService.cs:110-131, named `AnyScanning`/`AnyScanFailed`) but **nothing consumes them** — TrayIconService still mirrors
  only `IPollService.StatusChanged` + `DiscoveryQueue.Count` of the active source (TrayIconService.cs:44-66; repo-wide grep for
  `AnyScanning|AnyScanFailed` returns only the two definitions). Single-source behavior is unchanged (active poll status *is* the only
  status), but the stated tray wiring is not delivered. Similarly `SetActiveProjection`/`StatusFor`/`All` are defined with zero callers
  (acceptable — v3 rail hooks reserved for later steps; noted only).
- Structural coupling flagged for later steps (not violations):
  - Orchestrator/Factory read flat global preference keys directly via `Microsoft.Maui.Storage.Preferences`
    (Orchestrator.cs:50,77; Factory.cs:49-50) — `ISettingsRepository` has no implementation; step 7 must replace these call sites
    (they are currently *inside* pipeline classes rather than behind a seam).
  - One global `HttpClient` singleton with the mostaql-bot-filter UA/Accept headers (MauiProgram.cs:112-121) shared by all
    per-source `MostaqlFetcher`s/AssetDownloadService — step 7's `IHttpClientPolicyFactory` has no seam yet; the per-source pipeline
    factory does not create or receive any HTTP policy at all.
  - `MostaqlFetcher` reaches into static `CookieJar.Load()` (MostaqlFetcher.cs:35) — module→static-shared-plumbing coupling that step 7's
    per-source `ICookieJar` must break (call sites: MostaqlFetcher, AssetDownloadService.cs:159, CookieStore).
  - `EnrichmentService` holds a single global `AssetDownloadService` injected into every pipeline (Factory.cs:22,52) — fine for one
    source, will need per-source descriptor policy in step 7.

### Cross-cutting repo rules

- **Tag bumps**: ab965c8 → `1.0.4.dev3`/ApplicationVersion 8; 20d4047 → dev4/9; 9bdf95f → dev5/10; base `<Version>1.0.4</Version>`
  untouched (all three csproj diffs shown above). Satisfied.
- **No in-body `#if PLATFORM`**: new/changed shared files contain only `#if DEBUG` (CookieJar — pre-existing) and no `#if WINDOWS`
  additions in Services/Pipeline or Core/Sources. Satisfied.
- **UNITS.md rule** (plan repo-rules line: "UNITS.md rows updated in the same commit as any unit rename/add"; agents.md §Units):
  No unit *rename* happened in steps 1–6 (MostaqlLinkButton rename is step 8, uncommitted now). However steps 1–6 added new reusable
  shared concepts — `SourceId`/`SourceProjectId` (Value Objects), `ISourceDescriptor`+`SourceRegistry` (Descriptor/Registry),
  `SourcePipeline`/`Factory`/`Orchestrator`/`SourcePipelineStatus` (engine units) — and **none of the three commits touched UNITS.md**
  (`git show --stat` for all three: no UNITS.md). UNITS.md catalogs comparable non-UI service/concept units (e.g. `AppLifecycleService`,
  `TrayIconService`, `AppRoutes` rows — UNITS.md:81,286,361), so by the catalog's own standard these qualify.
  Severity: **Major (repo-rule violation)**; mitigating: no *pixel/behavior* impact and step 10 does mandate doc completion — but the
  same-commit requirement was missed. Additionally UNITS.md:81 still documents `AppRoutes` as `ProjectDetails(id)` (now `(SourceId, long)`) — stale line to fix in step 10.

## Defect list (severity-ordered)

1. **Major** — Step 4: `owners` lacks the plan-required `UNIQUE(source_id, owner_id)` (SqliteConnectionFactory.cs:176-183/215-229,
   Migrations/0002:13); identity rests entirely on unverifiable `InternalIdBand` disjointness (SourceRegistry.cs:48, MostaqlSource.cs:45).
   Plan line :63 violated.
2. **Major** — Step 4: `discovery_backlog` keyed on `project_id` alone (`PRIMARY KEY (project_id)`, :252-257; 0002 only ADDs column :182);
   plan line :64 required `(source_id, project_id)` keying. Same band-collision dependency.
3. **Major** — Step 6: `AnyScanning`/`AnyScanFailed` implemented (GlobalAppStatusService.cs:110-131) but consumed by nothing; the tray
   aggregate the plan (:72) asked for is not wired (TrayIconService.cs unchanged in behavior terms). No single-source behavior drift.
4. **Major** — Repo rule (plan :12): UNITS.md not updated in ab965c8/20d4047/9bdf95f for the new shared units (SourceId/SourceProjectId/
   ISourceDescriptor/SourceRegistry/SourcePipeline trio/SourcePipelineStatus).
5. **Minor** — Step 3: `DetailParser.cs:610,627` absolutization not moved behind `descriptor.BuildAbsoluteUrl` (plan :58 named it);
   the unused `BuildAbsoluteUrl` even behaves differently (no auto-`/`).
6. **Minor** — Step 5/behavior-identity: internal log/error message strings changed ("Project mostaql/101", composite ToString in
   telemetry payload, parse-failure URL now includes query) — EnrichErrors.cs:14,26; PollService.cs:203; MostaqlSourceServices.cs:57.
   User-visible Arabic + error codes unchanged.
7. **Minor** — Step 4 harness gap vs plan verification §3: no `GetRecentAsync` identical-order check, no live DiffEngine suppression test
   (tools/DbMigrationTests/Program.cs covers schema/idempotency/collision only).
8. **Minor** — Migration edge: DB with existing v1 tables but `user_version=0` would be mis-bootstrapped as fresh and stamped v2 without
   new columns (SqliteConnectionFactory.cs:82-91; no table-shape probe).
9. **Minor** — Coupling that will resist steps 7–9: flat `Preferences` keys read inside pipeline classes (Factory.cs:49-50, Orchestrator.cs:50,77);
   static `CookieJar.Load()` reached from module + AssetDownloadService + CookieStore with `SecretRepository.MostaqlCookieKey`;
   single global `HttpClient` with site-specific headers (MauiProgram.cs:112-121); `AssetDownloadService.cs:111` second hardcoded UA,
   `:133` `Contains("mostaql.com")`, `:85-87` site-named user text; VM fallback URL builders (ProjectCardViewModel.cs:90,
   ProjectDetailsViewModel.cs:238); `BudgetFormatter.cs:16` `CurrencySuffix` constant; toast label `عرض على مستقل`
   (WinRtVariation.cs:70,108; WinAppSdkVariation.cs:143,162). All explicitly assigned to steps 7–9 — listed as step-7/8/9 targets, not
   missing deliverables.
10. **Minor** — Duplicated cadence constants in shared code despite descriptor ownership: TokenBucketRateLimiter.cs:27, PollService.cs:30,
    SourcePipelineStatus.cs:113, SettingsViewModel.cs:41.
11. **Minor** — `MostaqlSource.Brands` at HEAD (`#2386C8,#1A6DA6,#EFF6FB,#ffffff`, MostaqlSource.cs:24) contains no current-app hex pair the
    plan named (#2563EB/#60A5FA are the live link colors in MostaqlLinkButton.xaml:16-22; #1A6DA6/#EFF6FB appear nowhere else in the repo);
    the file's own comment "Seeded to the exact colors the app renders today" is not accurate at HEAD. The parent's in-flight step-9 edit
    (6-field record + `#2563EB`,`#60A5FA`) already addresses this — verify at commit time.
12. **Nit** — ISourceModule registered but never consumed; dual registration path (MostaqlModule.cs:30-31). Consolidated file names vs plan
    tree (ISourceStrategies.cs, ISourceDiscovery.cs holding both interfaces, MostaqlSourceServices.cs holding discovery+enrichment+strategy
    wrappers instead of `MostaqlDiscovery.cs`/`MostaqlEnrichment.cs`); `StartAll` vs planned `StartAllAsync`; steps 1-3 in one commit (no
    move-only commit / temp adapter); dead `NotImplementedException` catches+labels in EnrichmentWorker.cs (steps "not yet landed" but landed);
    stale doc refs `Infrastructure.Http.Parsers.DetailParser` (ProjectDetails.cs:51) / `MostaqlScraper` (CookieStore.cs:11);
    missing `Migrations/0001_initial_schema.sql` referenced by 0002 comment + README; `Migrations/README.md` describes the obsolete
    throw-on-mismatch regime; UNITS.md:81 `AppRoutes` row stale (`ProjectDetails(id)`); `GlobalAppStatusService` forwarding methods
    (UpdateQueueCount/Notify*/TitleOf etc., :261-283) now have no non-pipeline callers — near-dead facade surface; `SettingsPanel*.xaml`
    copy lines 174/250 name مستقل in UI strings that are **not** on the plan's step-8 enumeration (possible plan gap — surface, don't fix).
13. **Nit** — DI: pipelines are constructed eagerly at `MauiProgram` line 222 but `ISourceModule.Descriptor` is never used to enumerate
    modules; registry enumeration relies solely on `IEnumerable<ISourceDescriptor>` (fine, but the planned module-hook pattern is half-implemented).

## Verification gates — outcomes

- Grep gates: run and classified (raw hit lists above; zero unexplained shared-code violations at HEAD; all residual hits are
  explicitly assigned to steps 7–9 or plan-named exceptions).
- Deletion check: PASS (IProjectScraper/MostaqlScraper/Parsers gone; `Contains("mostaql.com")` only at AssetDownloadService.cs:133, step-7 item).
- Composite completeness: PASS (no bare-long identity at pipeline/storage boundaries; allowed exceptions are parse-level and internal-id math).
- DB migration: runner + SQL + fresh-path + DEFAULT analysis PASS; `dotnet run --project tools/DbMigrationTests` **ALL CHECKS PASSED** (full output above).
- Build gate: **INCONCLUSIVE at HEAD** — working tree contaminated by in-flight step-8/9 edits (6 errors, all in the mixed
  SourceLinkButton/MostaqlLinkButton XAML set; evidence above). HEAD itself was not independently buildable without forbidden git mutation.
- Behavior-identity risks: enumerated above (items 6, 8, 9, 10, 11).
- DI graph: facade same-instance requirement satisfied; no cycles; eager Attach before first window satisfied; removal singletons have no consumers left.
- Status projection: guards/bridges/length-3 arrays/per-source filtering/badge semantics verified; one Major unwired-aggregate (item 3).
- Version bumps dev3/dev4/dev5: verified from csproj diffs.
- Appium regression gate (plan §2) and manual smoke (plan §5): not executed — outside allowed tooling for this audit.

## Assumptions made

1. "Committed HEAD semantics" = what `git show HEAD:<file>` returns; where the working tree had drifted (BrandTokenSet, MostaqlSource,
   DbMigrationTests Program), the HEAD version was treated as auditable truth and the drift reported as parent step-8/9 in-flight work.
2. The locked owner decision (internal row id = band + site id, band 0 for Mostaql) overrides plan step-4's `id`-surrogate rebuild prose
   wherever they conflict; the plan's own Verification §3 wording matches the implementation's column naming, supporting this reading.
3. UNITS.md "unit" scope includes shared non-UI service/concept classes (precedent: `AppLifecycleService`, `TrayIconService`, `AppRoutes`,
   `Debouncer` rows). If the owners decide pipeline internals are exempt, defect 4 downgrades to Nit.

## Open questions / blockers

- Was the conditional `sourceId` omission for the default source (AppRoutes.cs:57-58, toast payload gating) an explicitly accepted
  deviation by the plan owner, or should `?sourceId=mostaql&` always be emitted? Behavior-identity argues for the current form.
- Is the owners/backlog unique-index gap (defects 1–2) acceptable as "banding-only constraints" given the locked "full composite identity"
  decision, or should 0002 gain `CREATE UNIQUE INDEX ux_owners_source_owner / ux_backlog_source_project` before nafezly lands?
- HEAD-only build verification: needs a clean tree (parent must commit/finish step 8/9 edits) or an authorized detached worktree.
- Appium baseline run (plan verification §2) was never produced/recorded in these three commits — is a pre-refactor baseline run on file anywhere?

## Suggested next actions for parent

1. Add the two missing unique indexes (owners, discovery_backlog) as a 0002 amendment or a 0003 step *before* nafezly registration,
   or explicitly record banding as the sanctioned substitute.
2. Wire `AnyScanning`/`AnyScanFailed` into `TrayIconService` (or delete the aggregates and note the tray requirement as deferred).
3. Backfill UNITS.md rows for the Core/Sources contracts + SourcePipeline trio in a follow-up commit referencing the missed rule (same-commit rule already broken).
4. Re-run `dotnet build` once the SourceLinkButton/BrandTokenSet step-8/9 edits land, to restore a verified build gate for the dev5+ state.
5. Consider folding the `SettingsPanel` copy lines that name مستقل into the step-8 rename inventory (they are not currently assigned to any step).
6. If the DetailParser absolutization (lines 610/627) is meant to route through `MostaqlSource.BuildAbsoluteUrl`, reconcile the
   missing-slash behavior difference before step 7 touches URLs.

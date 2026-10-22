# B4 — Details + Settings implementer

## Goal (as delegated)
Schema-driven project details rendering from `descriptor.SchemaMap` + capability gating, an executor
field persisted end-to-end (nafezly-only), contributed per-source settings sections rendered from
`SourceContributionRegistry`, the §9 retry affordance, and honest contract flips (§1/§4/§7/§9/§10).
Gates: `tools/ParserTests` and `tools/DbMigrationTests` (app build is parent-owned; not run).

## Actions taken

### 1. Executor end-to-end (decision #1)
- **`Models/ProjectDetails.cs`** — added `public string? ExecutorName { get; set; }` (Owner untouched).
- **`Infrastructure/Database/Migrations/0004_add_executor.sql`** (new) — `ALTER TABLE projects ADD COLUMN executor_name TEXT;`, nullable, additive-only.
- **`Infrastructure/Database/SqliteConnectionFactory.cs`** — `CurrentSchemaVersion` 3→4; added `Migration0004Sql`; stepwise chain now `…==2 → 0003 (set v3) → ==3 → 0004`; `InitialSchemaSql` fresh path gained the `executor_name TEXT` column so fresh installs land at v4 with it.
- **`Infrastructure/Database/RowMaps.cs`** — `ProjectDetailsRow.executor_name` + `ToDetails` maps it.
- **`Infrastructure/Database/ProjectRepository.cs`** — UPSERT INSERT columns/VALUES/`ON CONFLICT DO UPDATE` + `@executor_name` param; `p.executor_name` added to the two details SELECTs (GetDetailsAsync + GetAllDetailsAsync). `InsertSummaryAsync` (discovery) intentionally untouched — executor arrives only on enrichment.
- **`Sources/Nafezly/Parsing/NafezlyDetailParser.cs`** — new `ExtractExecutorName(root)` scoped to the `منفذ المشروع` label's following person-row block (NOT the 60–146 `#project-load-offers` freelancer anchors), reads the `data-profile-role="freelancer"` text anchor (avatar `alt` fallback); wired into the returned `ProjectDetails`. Name only — rating/success-count/profile-url are app-gated (capture-manifest §1.9/§1.10), left null.

### 2. Schema-driven details (decision #2, contract §1/§7)
- **`Features/Projects/ViewModels/DetailSchemaRenderer.cs`** (new, MAUI-free) — `Render(descriptor, details)` reads `descriptor.SchemaMap` (section + order), omits null/empty fields (never "—"), gates `executor_name` on `Capabilities.HasExecutorInfo`, and flags money rows via `MoneyNeedsLtr(CurrencySuffix)` (Arabic-letter test → data-driven, no bool added to the interface). Emits `DetailMetaRow` + `DetailSectionGroup`.
- **`Features/Projects/ViewModels/ProjectDetailsViewModel.cs`** — injects `ActivePipeline`; exposes `MetaSections` (grouped), `ShowExecutorSection` (real `HasExecutorInfo` UI read), `ExecutorName`, `ShowRetryAffordance`, `HasSkills`/`HasAttachments`, and `RetryEnrichmentCommand` (re-enqueues the composite id on `ActivePipeline.Queue`, flips Pending). `LoadAsync` calls `RebuildMetaSections()`; `Details` gained `NotifyPropertyChangedFor` for the new members.
- **`UI/DesignSystem/Converters/BoolToFlowDirectionConverter.cs`** (new) — `IsMoney ⇒ LeftToRight` cell isolation.
- **`ProjectDetailsWindowsLayout.xaml`** + **`ProjectDetailsMobileLayout.xaml`** — replaced the hand-authored project/owner meta grids with a `BindableLayout` over `MetaSections` (per-section header + rows, money cell LTR), added the gated retry button, and gated skills/attachments on `HasSkills`/`HasAttachments`.

### 3. Contributed settings sections (decision #3, contract §4/§10)
- **`Core/Sources/ISourceSettingsSectionViewModel.cs`** (new) — neutral bindable contract with per-row presence flags (`HasRetryToggle`/`HasSessionRow`).
- **`Sources/Nafezly/Contributions/NafezlySettingsSectionContribution.cs`** — `NafezlySettingsSectionViewModel` now implements it (still a plain POCO, so ParserTests keeps linking it).
- **`Features/Settings/ViewModels/SettingsViewModel.cs`** — injects `SourceContributionRegistry` + `IServiceProvider`; `ContributedSections` = `SettingsSectionsFor(ActiveSource)` → `CreateViewModel(services)`; `HasContributedSections`.
- **`SettingsPanelWindowsLayout.xaml`** + **`SettingsPanelMobileLayout.xaml`** — appended a generic contributed-section template (header + AppToggle retry + session row), gated by `HasContributedSections` / per-row `Has*` flags.

## Files touched
New: `Migrations/0004_add_executor.sql`, `DetailSchemaRenderer.cs`, `BoolToFlowDirectionConverter.cs`, `ISourceSettingsSectionViewModel.cs`.
Edited: `ProjectDetails.cs`, `SqliteConnectionFactory.cs`, `RowMaps.cs`, `ProjectRepository.cs`, `NafezlyDetailParser.cs`, `NafezlySettingsSectionContribution.cs`, `ProjectDetailsViewModel.cs`, `SettingsViewModel.cs`, details Win/Mobile layouts, settings Win/Mobile layouts, `tools/ParserTests/{Program.cs,ParserTests.csproj}`, `tools/DbMigrationTests/{Program.cs,DbMigrationTests.csproj}`, contract, UNITS, CHANGELOG, Migrations README.

## Verification
- `dotnet run --project tools/ParserTests` → **297 passed, 0 failed** (baseline 229 + my 17 B4 checks; sibling B3's concurrent facet tests account for the rest — counted by name, no fighting). New: `TestB4ExecutorExtraction` (open="Abdelrahman Moneer", in-progress="عبدالرحمن الزرقانى", open-awarded absent → null, mostaql → null, capability flags) and `TestB4DetailSchemaRenderer` (money LTR flag, executor gating, Order sort, null-omission, mostaql no-executor-even-with-value).
- `dotnet run --project tools/DbMigrationTests` → **ALL PASSED**. Existing `==3` assertions updated to `==4` (required by the version bump). New: `TestV3ToV4ExecutorUpgrade` (column added on a temp v3 data copy, rowids/counts preserved, executor NULL on old rows, idempotent re-run) + `TestFreshInstallHasExecutorColumn`.
- No source-literal branches introduced in shared UI (grep of new/edited files: only doc comments name sites; logic keys on Capabilities/CurrencySuffix/SchemaMap).

## Decisions made
- **Money isolation**: chose `MoneyNeedsLtr(CurrencySuffix)` (descriptor data) over adding an `ISourceDescriptor.MoneyFlowLtr` bool — the brief said add the bool only if sources genuinely differ AND surface; deriving from the suffix is source-blind and testable, no interface churn. Mostaql `ر.س` → RTL (unchanged), nafezly `$` → LTR.
- **Executor URL / stats**: omitted (capture-manifest §1.9 shows name only in anon HTML; rating/success-count are app-gated). The mockup's executor avatar+stats cannot be sourced anonymously → renderer emits just the name row. Conservative default honored.
- **Supervised badge**: not rendered — nafezly `HasSupervisedFlag=false` (§1.10); the mockup's "مشرف عليه" chip has no data source → correctly absent.
- **Retry path**: `ActivePipeline.Queue.EnqueueAsync(new SourceProjectId(source, id))` — the parent-shipped accessor, no new singleton.

## Open questions / blockers (surfaced, not resolved)
- **Per-source live session-state read-out exceeds B4's safe boundary.** `CookieStore` is a DI singleton bound to one descriptor (MauiProgram resolves a single `ISourceDescriptor`), and `ICookieJar` exposes no `HasCookie`/count. The contributed settings session row is therefore descriptor-driven (`NeedsSessionForFullEnrichment`), which is honest for nafezly (needs no session). A true "linked/unlinked + count" per source requires registering a `CookieStore` per source (and the existing Settings cookie card reads the default-bound store today) — a facade change I deliberately did not undertake blind, since it can't be build-verified here.
- **MauiProgram untouched.** `ProjectDetailsViewModel`/`SettingsViewModel` new ctor params (`ActivePipeline`, `SourceContributionRegistry`, `IServiceProvider`) auto-resolve from existing registrations. If the parent's dev17 commit edits MauiProgram, these still bind without a wiring change.
- **Windows on-device rendering unverified** (app build parent-owned): schema-driven meta card, LTR money cell, retry button, contributed settings sections — coded against existing patterns but not XAML-compiled here. All five contract rows flipped with an explicit "Windows smoke pending" caveat.

## Suggested next actions
1. On-device (Windows) smoke: nafezly awarded project → executor row present + budget `dir=ltr`; nafezly open project → no executor; mostaql → no executor group; a `Failed` project → retry button re-enqueues + badge → Pending; settings under nafezly → contributed section renders, retry toggle persists across reopen; under mostaql → no contributed group.
2. If/when a per-source `CookieStore` refactor lands, wire the contributed session row to live cookie presence.
3. Parent to fold the DB chain change (user_version 4) into the dev17 bump.

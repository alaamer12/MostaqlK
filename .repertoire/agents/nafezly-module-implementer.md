# nafezly-module-implementer — A3 report

## Goal
Build the Nafezly source module (`Sources/Nafezly/**`) with zero page hardcoding and full
reuse of the shared `Core/Parsing` engines + `Core/Sources` contribution seam, wire it into DI
additively, and prove it with an offline fixture suite + a real-band DB coexistence check.
Executed `scratch/brief-a3-launch.md` against `capture-manifest.md` (A2 DOM truth) and the v3
`backend-ui-contract.md`.

## Files created
- `Sources/Nafezly/NafezlySource.cs` — `ISourceDescriptor`. Identity block EXACTLY per brief
  (id nafezly / نفذلي / Nafezly / ن / nafezly.com; light brand seed `#FFA200 #9A6100 #FFF4DE
  #3a2a00` + `LinkLight #9A6100 / LinkDark #FFB84D`; 60s/8rpm; `$`; band `1_000_000_000_000`;
  cookie_nafezly / empty legacy key+env; container nafezly; caps executor=true supervised=false
  session=false). `SchemaMap` seeds nafezly field order (no attachments row). `BuildListingUrl`
  appends the opaque query nafezly expects.
- `Sources/Nafezly/NafezlyParseProfile.cs` — `ParseProfile` DATA (labels, FieldProfileTable via
  `FieldInferenceEngine.Stem`, CompletedStatusText مكتمل, proposal/relative-time words; empty
  attachment/extension sets = absent-by-truth).
- `Sources/Nafezly/NafezlySelectors.cs` — `SelectorMap` DATA from manifest §5 (project-box row,
  `^https://nafezly\.com/project/(\d+)-`, skill-href `/projects/skill/`, bid `data-score`; meta-*
  recorded but the label-driven walk is what the parser actually drives).
- `Sources/Nafezly/NafezlyQueryVocabulary.cs` — 7 specialize slugs + Arabic labels, facet names,
  echo order, `BuildQuery`. Module data, never a shared-code constant.
- `Sources/Nafezly/NafezlyStates.cs` — four state strings (3 observed + defensive مغلق); text
  stored raw, colours never read into data.
- `Sources/Nafezly/NafezlyFetcher.cs` — mirrors MostaqlFetcher; per-source cookie jar; UTF-8 forced;
  429/Cloudflare-1015 exponential backoff honoring `Retry-After` with a 40s cap, surfaces via
  `HttpErrors(siteName)` after giving up; 403 returns an Err Result (no poison).
- `Sources/Nafezly/Parsing/NafezlyListingParser.cs` — rows via shared `SelectByClassContains`,
  icon-keyed meta facts, slug-id regex; `ParsePagination` reads prev/next `a.page-link[rel]`.
- `Sources/Nafezly/Parsing/NafezlyDetailParser.cs` — resolution ORDER: JSON-LD JobPosting →
  بطاقة المشروع label rows (shared `LabelDrivenExtract`) → generic `FieldInferenceEngine`; skills
  CSV, workHours=days, offer-row count cross-check for proposals; owner from app-gate anchor text
  (+avatar); gated fields null, never guessed.
- `Sources/Nafezly/Parsing/NafezlyJsonLd.cs` — schema.org JobPosting extractor (tolerates nafezly's
  raw-newline JSON; `@graph`/array aware; skills CSV, workHours, datePosted normalized).
- `Sources/Nafezly/Parsing/NafezlyParseException.cs` + `NafezlyParseErrors.cs`.
- `Sources/Nafezly/NafezlySourceServices.cs` — strategies/discovery/enrichment. Strategies are
  registered CONCRETE-only (see decisions); discovery stamps `summary.Source`; enrichment returns
  Err on fetch failure (never throws up the pipeline).
- `Sources/Nafezly/NafezlyModule.cs` + `AddNafezlySource()`.
- `Sources/Nafezly/Contributions/NafezlySettingsSectionContribution.cs` — the ONE real
  `ISourceSettingsSectionContribution` (session-state read-out + enrichment-retry toggle) + a
  DI-free `NafezlySettingsSectionViewModel` + module-local `NafezlySettingsKeys`.

## Files edited
- `MauiProgram.cs` — ADDITIVE only: `using MostaqlK.Sources.Nafezly;`, `AddNafezlySource();` next to
  `AddMostaqlSource();`, and a `SourceContributionRegistry` singleton built from the two DI
  collections (no redesign of `SourceContributions.cs`).
- `tools/ParserTests/ParserTests.csproj` — linked the nafezly offline files + `SourceContributions.cs`
  + `ISettingsRepository.cs` + the contribution. No new package needed.
- `tools/ParserTests/Program.cs` — added 9 nafezly test methods (descriptor identity, listing page1,
  filtered+pagination, three detail pages, nullable-absent, degenerate, contribution registry) using
  the shared `Check`/`Run` harness. The 135 existing checks are untouched.
- `tools/DbMigrationTests/Program.cs` — added `TestNafezlyBandCoexistence` using the REAL
  `1_000_000_000_000` band (raw SQL mirroring repository columns) + an `InsertRejected` helper.
  Existing checks left intact.
- `.repertoire/.steering/v3/backend-ui-contract.md` and `UNITS.md` — §3/§5/§7/§8/§9/§10/§11 nafezly
  rows flipped to REAL for exactly what is now true; UNITS rows for `NafezlyModule` and
  `SourceContributionRegistry` added. (These converged with a concurrent agent working the same
  rows; final wording matches this deliverable.)

## ParseProfile / Selectors provenance (manifest §5)
- State texts مفتوح/تحت التنفيذ/مكتمل (+ defensive مغلق) ← §1.4/§2/§5; CompletedStatusText=مكتمل ← §1.4.
- Meta labels حالة المشروع/تاريخ النشر/المدة المتاحة/الميزانية/عدد المتقدمين/صاحب المشروع/منفذ المشروع ← §1.3/§5.
- Skills `a[href^="…/projects/skill/"]` ← §1.8 (CSV also in LD `skills` ← §1.2).
- Budget `{min} - {max} $` ← §1.5; proposal `{n} عروض` ← §2; relative-time منذ… ← §1.7.
- Listing row `div.project-box`, id `^https://nafezly\.com/project/(\d+)-`, icons fa-usd-circle/
  fa-business-time/fa-ballot/fa-clock/fa-check-circle ← §2.
- Attachments/extensions/auth-URL substrings left EMPTY ← §1.11 absent-by-truth.
- Specialize facets + labels ← §3 (7 slugs).

## Verification (all green)
- `dotnet run --project tools/ParserTests` → TOTAL: 229 passed, 0 failed (135 existing + 94 nafezly).
  Per-page assertions match manifest §5: slug-id 56609…; states تحت التنفيذ/مكتمل/مفتوح by TRUE page
  (fixture-name shift respected); budgets `10 - 25 $`/`50 - 100 $`; proposals 72/43/30; publish
  23/1/11; skills CSV (deduped); duration 3/1/7; nullable-absent (attachments 0, rating/rate/
  completed null, profile url null, avatar captured, status Enriched); pagination prev/next.
- `dotnet run --project tools/DbMigrationTests` → ALL DB MIGRATION CHECKS PASSED incl. the
  real-band coexistence (colliding site id 101 → distinct internal ids, projects/owners/backlog
  composite keys reject duplicates, FromInternalId round-trips both).
- `dotnet build MostaqlK.csproj -f net10.0-android …` → Build succeeded, 0 Warning(s), 0 Error(s).
- Live site: SKIPPED (the single paced fetch is explicitly optional; offline gates are the mandatory
  evidence, and skipping avoids adding to the 429/1015 pressure the manifest recorded).

## Surfaced gaps (with two options each)
1. Executor info is unmodellable end-to-end today. `Capabilities.HasExecutorInfo=true` and the
   manifest §1.9 exposes the منفذ المشروع name, but `ProjectDetails`/`Owner` have NO executor field
   and there is no executor persistence. I did NOT invent a field (kept honest). Options:
   (a) add nullable `Executor*` fields to `ProjectDetails` + `RowMaps`/repository/FTS (touches
   Models + Infrastructure — A4/B-adjacent, out of pure-A3 scope); (b) defer until the B-phase
   details UI actually renders the executor block (current choice).
2. `BuildProjectUrl` uses the bare id (`…/project/{id}`, slug omitted) because
   `ISourceEnrichment.EnrichAsync` receives only the site id, not the discovered `summary.Url`
   (which carries the full slug). Slug-mismatch 404-vs-redirect is UNVERIFIED per the manifest.
   Options: (a) verify bare-id resolution with a paced live detail fetch; (b) widen the enrichment
   seam to accept the stored URL (a shared-contract change — deliberately NOT done in A3).
3. Shared `HtmlStructureEngine.ExtractMetaFields` (class-substring pairing) is shaped for Mostaql's
   `meta-row/meta-label/meta-value` grid; nafezly's بطاقة المشروع uses very generic tokens
   (`row`/`text-right`/`font-1`) so that fast path is fragile. The module drives the shared,
   identifier-blind `LabelDrivenExtract` (adjacency walk) for the meta rows instead — this needed
   NO `Core/Parsing` edit and is the sanctioned reuse path. No Core change was required or made.
4. `NafezlySettingsSectionViewModel` is a plain POCO (no `INotifyPropertyChanged`), and its
   session-state read-out is descriptor-driven (needs-session = capability), not cookie-store-backed,
   because `CookieStore` is registered for `SourceId.Default` only today. Options: (a) a per-source
   CookieStore resolution + MAUI VM wrapper in B4; (b) leave as-is (chosen — UI wiring is B4).

## Behavior identity for mostaql
Nafezly adds ONLY its own files + two additive MauiProgram lines. It registers a SECOND
`ISourceDescriptor`/`ISourceModule`/`ISourceDiscovery`/`ISourceEnrichment` (all consumed as DI
enumerables keyed by source) and its OWN HttpClient via a factory that does NOT re-register the
shared `HttpClient` singleton. Critically it does NOT add a second `IListingStrategy`/
`IDetailStrategy` registration (those resolve as a single service and would cross-wire mostaql), so
mostaql's poll/enrich path is provably unchanged.

## Concurrency note
`NafezlyListingParser.cs`, `NafezlyDetailParser.cs`, `NafezlyJsonLd.cs`, `NafezlyParseProfile.cs`,
`MauiProgram.cs` and the contract/UNITS rows were being edited by another agent in the same tree
while I worked. I adapted additively (kept their converged parsers, added `ParsePagination`,
cleared two nullable warnings, and shaped my tests to the design), and never reverted edits I did
not make. All gates verified against the final on-disk state. Nothing committed.

## Environment
Linux codespace, .NET SDK 10.0.401, ANDROID_HOME + jdk21 present; android build succeeded 0/0.
dotnet test gates run offline (fixtures only).

## Addendum (second pass, final converged state)
This report is shared across two concurrent agents doing the same A3 task. Final on-disk reconciliation
(both agents, verified by re-running the gates):
- **Budget placeholder bug fixed**: `NafezlyParseProfile.NotCalculatedMarkers` had included `"0"`; the
  shared `IsPlaceholder` is a substring match, so every nafezly budget ("10 - 25 $", "50 - 100 $")
  resolved as a placeholder and nulled. Removed `"0"` (kept `غير محدد / N/A / لا يوجد / —`). This is
  what took the detail budget checks from fail→pass (ParserTests 226→229).
- **duration provenance**: `NafezlyDetailParser` now records `fields["duration"] = FieldResolution(…,
  "jsonld", 1.0)` when JSON-LD `workHours` wins, matching the ParserTests assertion
  `FieldProvenance["duration"].Source == "jsonld"`.
- **DI strategy isolation hardened**: converged on the safest shape — `NafezlyDiscovery`/
  `NafezlyEnrichment` construct their stateless `NafezlyListingStrategy`/`NafezlyDetailStrategy`
  directly (the `new()` default) and `NafezlyModule` registers NO `IListingStrategy`/`IDetailStrategy`
  at all, so Mostaql's single interface registration stays unambiguous and cannot be cross-wired to
  nafezly's parser. (Stronger than "concrete-only registration"; same intent as noted above.)
- **DbMigrationTests owner-id correction**: the real `OwnerRepository` writes `owner_id` as the
  BANDED internal id (`ON CONFLICT(owner_id)`), so the coexistence test must NOT reuse `900` for both
  sources (that crashed on the `owner_id` PK). Naivezly owner now maps to `band + 900`; the test passes.
- **Contract flips actually made this pass** (only rows now true): §3 nafezly palette (light seed
  REAL, dark = B1), §5 per-source client isolation (A3 verified), §7 nafezly state vocabulary REAL,
  §8 nafezly query vocabulary (backend REAL / UI B3), §9 nafezly failure→summary-only+Failed (UI retry
  affordance = B4), §10 `ISourceSettingsSectionContribution` + `SourceContributionRegistry` REAL
  (`ISourcePageContribution` stays PLANNED B2 — no page contribution delivered), §11 nafezly-band
  coexistence REAL. UNITS.md: added `SourceContributionRegistry` and `NafezlyModule/…` rows.
- Final gates re-run against the on-disk tree: ParserTests `TOTAL: 229 passed, 0 failed`;
  DbMigrationTests `ALL DB MIGRATION CHECKS PASSED`. Nothing committed (left in the working tree).

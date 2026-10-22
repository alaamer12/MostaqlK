# Backend ⇄ UI Contract (v3) — LIVING DOCUMENT

> **Rules.** The UI phase codes ONLY against rows marked **REAL**. A row flips planned→real only in the
> same commit that makes it true, verified by that milestone's gate. Every name below is a real type/member
> (or will be). Source: Phase-2 plan `fresh-narrows-gull.md`; product contract: `multi-source-requirements.md`.

## 1. Source identity & registry — REAL (pre-phase)

| Surface | Shape | Status |
|---|---|---|
| `SourceId` | value object; `From/TryFrom`, `Mostaql`, `Default`; nafezly arrives via `SourceId.From("nafezly")` (no new constant in shared code) | REAL |
| `SourceProjectId` | `(SourceId Source, long ProjectId)` — the ONLY project identity crossing layers; `ProjectId` is site-local, never unique alone | REAL |
| `SourceRegistry` | `All`, `Get`, `TryGet`, `Contains`, `ToInternalId/FromInternalId/ToSiteProjectId` (band math) | REAL |
| `SourceRegistryProvider` | DI-lite accessor for VM/components: `Current`, `DescriptorFor(SourceId)` | REAL |
| `ISourceDescriptor` members | `Id DisplayNameAr DisplayNameLatin Domain RailGlyph Brands DefaultPollIntervalSeconds DefaultRequestsPerMinute CurrencySuffix OwnerFallbackMeta Capabilities CookieSecretKey LegacyCookieSecretKey LegacyCookieEnvInlineVar LegacyCookieEnvFileVar SessionCookieName UserAgent AcceptHeader AcceptLanguageHeader SettingsContainerId InternalIdBand BuildListingUrl BuildProjectUrl BuildAbsoluteUrl IsSameSiteUrl` | REAL |
| `ISourceDescriptor.SchemaMap` / `ParseProfile` / `Selectors` | field presence + render order for schema-driven details; per-source parser vocabulary as pure data consumed by shared `Core/Parsing` engines (§8.1) | REAL (A1) |
| `SourceCapabilities` `HasExecutorInfo HasSupervisedFlag NeedsSessionForFullEnrichment` | must GATE UI sections (absent = section never rendered, never empty) | data REAL, UI-read **PLANNED (B4)** — still unconsumed by backend; A1 found no behavior-identical backend read |

## 2. Pipeline & status — REAL (pre-phase)

| Surface | Shape | Status |
|---|---|---|
| `SourcePipelineOrchestrator` | one engine per registered source; `Active` (default), `For(SourceId)`, `All`, `StartAll(token)` | REAL |
| `SourcePipeline` | `Descriptor Source Status Queue Tracker Limiter Enrichment Poll Pool` | REAL |
| `SourcePipelineStatus` | per-source radar/queue/worker/scan telemetry (ObservableObject, dispatcher-guarded); `Source` property tells which source it is | REAL |
| `GlobalAppStatusService` | UI projection facade: `Active`, `StatusFor(source)`, `All`, `SetActiveProjection(source)`, `AnyScanning`, `AnyScanFailed`, + forwarded members (ProjectsAddedToday*, QueueCount, IsScanning, LastScan*, Workers, WorkerStates, ConnectionText…) | REAL |
| Pause/resume/poll-now | `IPollService` via Active facade today; per-source access = `orchestrator.For(s).Poll` | REAL |

## 3. Identity / theming — REAL

| Surface | Shape | Status |
|---|---|---|
| `BrandTokenSet` | `Brand BrandInk BrandSoft OnBrand LinkLight LinkDark` hex strings, per source descriptor `Brands` | REAL |
| `IBrandTokenProvider` | `CurrentSource Current TokensSwapped SwapTo(source)` — single swap path; components subscribe on Loaded / release on Unloaded | REAL (consumers **B1/B2**) |
| DynamicResource `Brand*` palette fed by provider | App-level dictionaries replacing inline hexes; grep gate bans new inline brand hexes. **Locked design:** `BrandTokenSet` gains the four dark counterparts (`BrandDark BrandInkDark BrandSoftDark OnBrandDark`); `LinkLight/LinkDark` already exist; provider exposes theme selection (`CurrentFor(AppTheme)`), palette swap fires on `TokensSwapped` AND theme change. Verified dual sets to seed: mostaql light `#2386C8 #1A6DA6 #EFF6FB #ffffff` / dark `#5CA8DE #5CA8DE #245CA8DE(alpha .14→ARGB) #0b1c2a`; nafezly light `#FFA200 #9A6100 #FFF4DE #3a2a00` / dark `#FFB84D #FFB84D #24FFB84D(alpha .14) #2a1d00` (mockups README = source of truth; rgba→8-hex ARGB conversion at seed time). A3 does NOT extend the record — B1 does, atomically with the hex sweep. | **PLANNED (B1)** |
| Nafezly palette | mockup orange set incl. AA-safe `BrandInk` for ≤14px text | **PLANNED (A3)** |

## 4. Settings & prefs — REAL

| Surface | Shape | Status |
|---|---|---|
| `ISettingsRepository` | `Get/Set(source,key)` namespaced containers (descriptor `SettingsContainerId`) with legacy flat read-through + lazy migrate; `GetGlobal/SetGlobal/Contains/ContainsGlobal` | REAL |
| `SettingsKeys` | per-source: PollIntervalSeconds MaxRequestsPerMinute SafeRequests QueryParams IncludeAssets MaxConcurrentDetailFetches NotificationGroupingEnabled GroupingMode GroupingThreshold · global: IsDarkMode StartupEnabled IsPollingActive (+ `mostaqlk-source` active-source persistence **PLANNED B1**) | REAL |
| Per-source settings rendering | descriptor data rows + contributed sections (see §8) | data REAL, render **PLANNED (B4)** |

## 5. HTTP / sessions / cookies — REAL

| Surface | Shape | Status |
|---|---|---|
| `ICookieJar` + `ICookieJarProvider.For(source)` | per-source, Netscape domain-column filtered, env `MOSTAQLK_COOKIE_{SOURCE}` (+legacy aliases, DEBUG only), release = encrypted store only | REAL |
| `CookieStore` | per-source secret key (`descriptor.CookieSecretKey`), seamless legacy-row rewrite, `HasCookie/CookieCount/UpdatedAtUtc/SaveFromFileContentAsync/ClearAsync/InitializeAsync` | REAL |
| `IHttpClientPolicyFactory` | builds per-source `HttpClient`/request headers from descriptor UA/Accept/Accept-Language (bot-filter behavior) | REAL — verify per-source client isolation at **A3** |
| `AssetDownloadService` | `ResolveAsync(SourceId, Asset, ct)` — descriptor-driven auth sniff + copy | REAL |

## 6. Navigation & notifications — REAL (B5 tightens)

| Surface | Shape | Status |
|---|---|---|
| Routes | `AppRoutes.ProjectDetails(SourceId, long)` → `?projectId=` (+`&sourceId=` for non-default); `SourceIdQueryParam`; deep links without sourceId ⇒ default source | REAL |
| Toasts | carry source id; button copy via `NotificationCopy.ViewOnSource(source)`; per-source activation resolves right project | REAL; SourceId.Default fallback removal + morph-first activation **PLANNED (B5)** |
| Inbox | `NotificationCenterViewModel` from `ProjectSummary` (carries `Source`) | REAL; per-source unread **PLANNED (B5)** |

## 7. Presentation data contracts

| Surface | Shape | Status |
|---|---|---|
| Budget | `BudgetFormatter.Format(raw, currencySuffix)` — suffix = `descriptor.CurrencySuffix` (mostaql `ر.س`, nafezly `$`) | REAL |
| Nafezly budget direction | `$`-figures render inside `FlowDirection=LeftToRight` isolation (Arabic page, LTR money) | **PLANNED (B4)** |
| Owner meta | `descriptor.OwnerFallbackMeta` while real per-source owner fields absent; executor block gated by `Capabilities.HasExecutorInfo` (nafezly-only) | data REAL, UI **PLANNED (B4)** |
| State vocabulary | per-source ParseProfile supplies completed/in-progress strings; UI shows scraped text verbatim | **PLANNED (A1–A3)** |

## 8. Filters / query-params vocabulary (decision 5)

| Source | Shape | Status |
|---|---|---|
| mostaql | existing `QueryParams` suffix string (onboarding presets) — `BuildListingUrl` appends | REAL |
| nafezly | facets: `specialize` ∈ {business, development, design, marketing, writing-translation, training, other}, `skills` (slugs), `pricing=min,max`, `page=N` — built INSIDE the nafezly module; UI FilterModal renders whatever vocabulary `ISourceDescriptor` exposes (typed facet model, not string concat in UI) | **PLANNED (A2/A3, UI B3)** |

## 9. Enrichment failure & retry (§8.3)

| Shape | Status |
|---|---|
| `EnrichmentStatus` Pending/Enriched/… + summary-only rows persist; 403 leaves `Failed`-style state visible | REAL today — exact failed-state + retry affordance contract | **PLANNED (A3)** |

## 10. Independent per-source growth — contribution API (decision 6)

| Surface | Shape | Status |
|---|---|---|
| `ISourcePageContribution` | `SourceId Source; route; Arabic title; rail glyph; optional badge` — rail shows them ONLY while the source is active; routes registered sourceId-scoped | **PLANNED (A3)** |
| `ISourceSettingsSectionContribution` | `SourceId; section header; view-model factory` — Settings appends active source's sections after descriptor rows | **PLANNED (A3)** |
| `SourceContributionRegistry` | mirrors `SourceRegistry`; built from `GetServices<>`; `For(SourceId)` | **PLANNED (A3)** |
| Hard rule | shared UI NEVER branches `if (source == X)`; constants in `Core/Sources` defaults + each module's own files only (grep gate) | RULE |

## 11. Storage guarantees — REAL

user_version=3 schema (composite unique keys, band-0 identity for mostaql rows), Dapper DTO row maps (`RowMaps.cs`), backlog per-source filtering, store-and-forget unchanged. Nafezly-band coexistence tests **PLANNED (A3)**. FTS: `FtsQueryService.SearchAsync(source, query)` is source-scoped and resolves composite identity like every other reader (A4 done) — UI may rely on FTS row identities.

---
*Milestones flip rows: A1 (schema map, capabilities read), A2/A3 (nafezly vocabulary, palette, failure-retry, contributions), A4 (FTS row), B1 (palette, source persistence), B4/B5 (UI reads, notification tightening).*

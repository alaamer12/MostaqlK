# MostaqlK Backbone — C# → Python Migration Plan (FROZEN REFERENCE)

> **Status:** Approved plan. Single source of truth for the Python migration.
> Sub-agents must read it (after `AGENTS.md` and their task-specific docs) and must not
> deviate from it without master approval.
>
> **Governance:** see [agents-workflow.md](agents-workflow.md) in this directory.
>
> **Date frozen:** 2026-08-25 · C# version analyzed: V1 (MAUI, Windows)

---

## Table of contents

1. Objective
2. Scope lock (user-approved decisions)
3. C# reverse-engineering results (A–J maps)
4. Behavioral parity trap checklists
5. Architecture alternatives considered
6. Final architecture
7. Directory structure
8. Shared contracts (frozen)
9. Dependency choices
10. Quality toolkit & gates
11. Execution phases
12. Intentional behavioral differences ledger
13. Testing strategy
14. Completion checklist

---

## 1. Objective

Fully migrate the existing **C# backbone** of MostaqlK into idiomatic, maintainable, modern
Python — a complete behavioral migration and architectural redesign, not a line-by-line
syntax conversion. The backbone covers: polling, scheduling, HTTP/network communication,
scraping, parsing, validation, normalization, transformation, deduplication, business rules,
database persistence, retries, error handling, logging, configuration, background execution.

All Python code lives under `.repertoire/python/`. The existing C# implementation remains
untouched during and after migration.

## 2. Scope lock (user-approved decisions)

| Decision | Choice |
|---|---|
| Runtime shape | **Headless asyncio service** (`uv run mostaql`), graceful SIGINT/SIGTERM shutdown |
| Database | **Project-local SQLite** file (default inside `.repertoire/python/data/`, config-overridable). Identical schema/semantics to C#, but NOT sharing the C# app's DB file |
| Notifications | **Out of scope entirely** — no grouper/dispatcher/toasts; workers log enriched projects through a tiny extensible hook |
| Sessions/cookies | **Out of scope** — no CookieJar/CookieStore/SecretRepository/SecretProtector/app_secrets; scraping runs anonymous |
| Asset downloads | **Out of scope** — no AssetDownloadService, no assets table, no attachment extraction in parsers |

### In scope

polling, scheduling, rate limiting, diff/dedup, scraping, parsing, validation,
normalization, transformation, business rules, SQLite persistence (`projects` / `owners` /
`project_skills` / `discovery_backlog` / `projects_fts`), retries, logging, configuration,
background execution, graceful shutdown.

### Out of scope (documented intentional differences)

Windows toasts + notification grouper/dispatcher, cookies/session secrets, asset download +
assets/app_secrets tables + attachment extraction, all UI/tray/onboarding
(`GlobalAppStatusService`, `TrayIconService`, view-models), `DesignDataSeeder`,
`MostaqlK.UITests`.

## 3. C# reverse-engineering results

Primary source files (read-only reference for every implementing agent):

```
Services/Pipeline/PollService.cs            Services/Pipeline/IPollService.cs
Services/Pipeline/DiscoveryQueue.cs         Services/Pipeline/InFlightTracker.cs
Services/Pipeline/EnrichmentService.cs      Services/Pipeline/TokenBucketRateLimiter.cs
Services/Pipeline/DiffEngine/*              Services/Pipeline/WorkerPool/*
Infrastructure/Http/MostaqlScraper.cs       Infrastructure/Http/IProjectScraper.cs
Infrastructure/Http/Parsers/{ListingParser,DetailParser,StructuralExtractor,InferenceEngine}.cs
Infrastructure/Http/Parsers/{ParseErrors,ParseException}.cs
Infrastructure/Database/{SqliteConnectionFactory,ProjectRepository,OwnerRepository}.cs
Infrastructure/Database/SearchIndex/FtsQueryService.cs
Core/{Result,DomainError}.cs                Core/Utilities/StringNormalization.cs
Core/Formatting/{ArabicRelativeTime,ArabicProposalParser}.cs
Models/{ProjectSummary,ProjectDetails,Owner,ProjectSkill,EnrichmentStatus}.cs
MauiProgram.cs / App.xaml.cs                (composition root & startup/shutdown)
Steering docs: .repertoire/.steering/base/product/architecture-pipeline.md,
               .repertoire/.steering/base/product/data-model-schema.md,
               .repertoire/.steering/v1/tech/{concurrency-model,diff-engine,
               worker-pool-and-rate-limiter,error-handling-and-resilience}.md
```

### A. Existing architecture (as-built)

Two-tier async pipeline hosted inside the MAUI UI process, DI-wired in `MauiProgram.cs`,
started fire-and-forget from `App.xaml.cs` once onboarding completes:

```
PollService loop (Tier 1)                     WorkerPool (Tier 2, 3 workers)
  ├─ TokenBucketRateLimiter.WaitForTokenAsync   ├─ DiscoveryQueue (unbounded Channel<long>, FIFO)
  ├─ MostaqlScraper.FetchListingAsync           ├─ EnrichmentWorker ×N
  │    └─ ListingParser.Parse                   │    ├─ rate-limiter token
  ├─ DiffEngine.DiffAsync                       │    ├─ scraper.FetchProjectDetailsAsync
  │    ├─ SqliteCommittedProvider (ALL ids)     │    │    └─ DetailParser.Parse
  │    └─ InFlightSetProvider                   │    │         ├─ StructuralExtractor (primary)
  ├─ InFlightTracker.TryMarkInFlight            │    │         └─ InferenceEngine (fallback)
  ├─ AddToBacklogAsync (persistent)             │    ├─ OwnerRepository.UpsertAsync (gated:
  ├─ InsertSummaryAsync (Pending row + FTS)     │    │    name non-empty OR id > 0)
  └─ DiscoveryQueue.Enqueue                     │    ├─ ProjectRepository.UpsertDetailsAsync
                                                │    │    (ONE tx: upsert + skills replace
Startup: WorkerPool re-hydrates queue from      │    │    + FTS delete/reinsert)
discovery_backlog; prunes >30d (fire&forget).   │    ├─ RemoveFromBacklogAsync (normal return)
Shutdown: App.RequestPipelineShutdown →         │    └─ NotificationDispatcher (dropped here)
cancel both loops.                              └─ finally: InFlightTracker.MarkComplete (always)
```

Policies: store-and-forget (write-once except enrichment completion upsert); three-state
dedup (`unseen` / `in_flight` / `committed`); one shared token-bucket budget across both
tiers; persistent crash-safe backlog; per-worker retry ladder 1m/2m/4m/8m/15m (5 attempts);
`Result<T>`/`DomainError` model with stable codes.

### B. Component inventory (verified behavior)

| Component | Responsibility |
|---|---|
| `PollService` | Loop: immediate first poll unless paused; tick-vs-check-now race via `Task.WhenAny`; check-now bypasses pause; interval re-read every tick, clamped ≥1s; pause honored by ticks only; status machine Idle→Polling→(BacklogDraining\|Idle), Error on failure; every cycle's outcome logged even when caller forgets; cancellation rethrown |
| `DiscoveryQueue` | Unbounded FIFO channel, multi-reader/writer, `Complete()` on stop |
| `InFlightTracker` | Concurrent ID set; atomic `TryMarkInFlight` claim (loser skipped); `MarkComplete` in worker `finally` always |
| `DiffEngine` | Pure set logic; providers = committed (**SELECT ALL project_id**, not candidate-filtered) + in-flight snapshot; provider failure → `DIFF-001`; output NewProjectIds/AlreadyKnownProjectIds preserving polled order |
| `TokenBucketRateLimiter` | capacity=rpm, refill rpm/60 per sec; safe mode adds 1s minimum inter-request spacing; fast mode (`safe_requests=false`) refills ×10, zero spacing; lazy refill-on-acquire under lock; computed wait then sleep loop ≥10ms; live Reconfigure clamps tokens to new capacity |
| `EnrichmentService` | token → fetch detail. One attempt per call (asset path dropped) |
| `WorkerPool` | Fixed pool (3); StartAsync: backlog re-hydration (TryMarkInFlight → enqueue → NotifyProjectDiscovered), fire-and-forget `CleanOldBacklogAsync(30)`; StopAsync: queue Complete() + cancel + await workers. Live increase spawns workers; decrease is cosmetic-only (quirk NOT carried over — see §12) |
| `EnrichmentWorker` | Per-ID try/finally releases in-flight ID always; unexpected exception logs ENRICH-002 and continues (never kills worker); ProcessAsync runs retry ladder internally (delays 1m/2m/4m/8m/15m between 5 attempts); success → owner upsert (gated) → UpsertDetails → RemoveFromBacklog; max-attempts → log ENRICH-001, row stays `'Pending'`, STILL removed from backlog (normal return path); nuance: exception escaping ProcessAsync skips backlog removal → retried after restart |
| `MostaqlScraper` | Listing `https://mostaql.com/projects` (+ query params normalized with leading `?`); detail `https://mostaql.com/project/{id}`; 15s linked timeout distinguishing caller-cancel vs timeout; error taxonomy Timeout/RequestFailed/Unexpected; ParseException → ParseFailed; sets `details.Url` after parse |
| `HttpClient` singleton | UA `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36`; Accept `text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8`; Accept-Language `ar,en-US;q=0.9,en;q=0.8`. Bot filter answers header-less requests with HTTP 403 — headers are load-bearing |
| `StringNormalization` | `Normalize`: regex `\s+`→single space + trim; `ToAsciiDigits`: U+0660–0669 AND U+06F0–06F9; `StripDiacritics`: removes `[\u064B-\u065F\u0670\u0640]`; `NormalizeLabel`: Normalize→StripDiacritics→fold أإآٱ→ا, ى→ي, ة→ه, ؤ→و, ئ→ي→trim chars `: ： ؛ ; . ، , - – — space`; `CleanHtml`: HTML-entity decode FIRST, then strip `<.*?>` tags, then trim `" ' space \t \r \n` |
| `ArabicRelativeTime.ParseRelativeNumber` | Explicit digit-run wins ("2024/05/01"→2024, "منذ ٧ يوما"→7); "لحظات"→0; duals (دقيقتان دقيقتين ساعتان ساعتين يومان يومين شهرين سنتان سنتين اسبوعان اسبوعين)→2; singulars (دقيقه دقيقة ساعه ساعة يوم شهر سنه سنة عام اسبوع أسبوع)→1 UNLESS plural marker present (دقائق ساعات ايام أيام اشهر أشهر شهور سنوات اعوام أعوام اسابيع أسابيع)→0; default 0. All matching after CleanHtml→ToAsciiDigits→NormalizeLabel |
| `ArabicProposalParser.Parse` | CleanHtml→NormalizeLabel→ToAsciiDigits; contains "اضف اول عرض"→0; contains "عرض واحد" or equals exactly "عرض"→1; contains "عرضان"/"عرضين"→2; first digit run ("3-10 عروض"→3 floor); contains عرض but no digit→0; else 0. Returned Text = cleaned text |
| `SqliteConnectionFactory` | Per connection open: `PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;`; schema bootstrap under process-wide lock via `PRAGMA user_version` (=1); version mismatch ⇒ `DatabaseSchemaException`; once-per-process FTS backfill for rows missing from projects_fts |
| `OwnerRepository` | Single-statement upsert; identity columns (name/profile_url/avatar_url) INSERT-only; conflict updates last_seen_at + rating + counts + registered_at only; GetById omits last_seen_at |
| `FtsQueryService` | Terms split on spaces, embedded quotes doubled, each wrapped as `"term"*`, implicit AND; `ORDER BY rank` (bm25 ascending); search-result summaries carry NO enriched_at (null) and different column layout than GetRecentAsync — map by name, never position |
| `InteractionLogger` | Line format `<DateTimeOffset.Now "O"> \| KIND \| checkpoint[\| variant=…][\| data=<JSON>][\| exception=Type: msg]`; kinds MARK/ENTER/EXIT/FAULT/ERROR (Failure→ERROR with variant=error.Code and data={Code, InternalMessage, ExternalMessage, FixMessage, Detail}); global lock; never throws |

### C. Parser specifications (condensed — full detail lives in the C# sources; agents MUST re-read them)

#### C.1 ListingParser

1. Guard empty/whitespace HTML → throw EmptyHtml.
2. Tier 1: XPath `//tr[contains(concat(' ', normalize-space(@class), ' '), ' project-row ')]`.
3. Tier 2 (if T1 empty): same exact-token trick for `div.project-item`.
4. Tier 3 (if T1+T2 empty): all `a[href*='/project/']`; id extracted; title = Normalize(DeEntitize(InnerText));
   skip id≤0/blank title; dedupe by id KEEPING FIRST OCCURRENCE.
5. Zero summaries after all tiers → throw NoProjectRows (message verbatim says "tr.project-row or
   div.project-card" though tier-2 class is actually project-item — PRESERVE message).
6. Per card: title link `.//h2/a` else `.//a` (missing ⇒ card silently skipped); url = raw href
   (no absolutization); id = FIRST `/project/(\d+)` match (not last digit-run!), fallback: trim trailing `/`,
   split on `/` and `-`, first all-digit segment; else 0. Brief from `p[contains(@class,'project__brief')]`
   inner `a` else the p itself. Meta `ul.project__meta` direct `li`s classified CONTENT-BASED in priority
   order: proposal signal (icon class contains hsoub-file-signature-icon OR i.fa-users OR text contains
   عرض/عروض) → ArabicProposalParser.Parse; time signal (contains منذ/ساعة/يوم/لحظات) → ParseRelativeNumber;
   else client name if not already set (later candidates discarded).
   Not extracted at listing time: Budget/DeliveryDays/SkillsText/ProjectStatus stay default;
   IsUnread=true; EnrichmentStatus=Pending; DiscoveredAt=UtcNow per card. No sort; tiers 1–2 never dedupe.

#### C.2 DetailParser

- Title chain: `//h1` → meta og:title (property or name) → `<title>`; Normalize + StripSiteSuffix
  (LAST occurrence of " - "/" | "/" – " whose suffix contains مستقل/Mostaql/Mostaqlk case-insensitively →
  cut+trim; then trailing bare-keyword pass). Empty ⇒ throw MissingTitle(projectId).
- Description chain: inside `#projectDetailsTab` find div whose class-token contains text-wrapper-div
  (OrdinalIgnoreCase) → NormalizeMultiline (keeps line structure); fallback og:description/description
  meta (single-line Normalize); fallback FindDensestTextBlock (div/article/section with ≤2 direct block
  children; normalized text >200 chars; longest wins; preferred over og-text ONLY if strictly longer).
- Skills: exact-token `ul.skills` OR substring-token match; per-li name=Normalize(DeEntitize),
  url=first `.//a/@href` (may be null); identifier-blind fallback (zero skills): all `a[@href]` containing
  `/skills/`, `skill=`, `/tag/` case-insensitively; name length 1..60; dedupe OrdinalIgnoreCase.
- Field combinator (per field, iterating synonym labels): structural value from ExtractMetaFields, then
  LabelDrivenExtract; SanityOk gate (null/empty fail; PLACEHOLDER markers لم يحسب بعد/غير محدد/N/A/لا يوجد
  count as VALID resolution; numeric fields must contain any digit incl. Arabic `[٠١٢٣٤٥٦٧٨٩]`);
  failure ⇒ lazily compute InferenceEngine ONCE per page; cross-validate whenever inference exists AND
  structural non-null (ValuesAgree = trim-equal or ordinal containment either direction; null on either
  side counts as agreement); disagreement appended to Mismatches; inference overrides ONLY when structural
  failed sanity; placeholder-containing final values forced to null.
- Completed-only gating pass 1: whole-page text = NormalizeLabel(DeEntitize(root.InnerText)); inferred
  values for started_since/deal_date/delivery_date/completed_projects_count lacking ANY genuine label
  occurrence → (null, none, 0.0).
- proposal_count override: count of elements with attribute data-bid-item > 0 ⇒ "{n} عروض"
  (structural, conf 1.0, EVEN n=1); elif neither عدد العروض nor عدد المقترحات occurs in pageText ⇒ null.
- Completion-status gate pass 2: project_status null or not containing مكتمل ⇒ null
  started_since/deal_date/delivery_date (NOT completed_projects_count — owner stat survives).
- Owner: name chain (owner-card h5[class*=name] → h3[class*=name] → a[href*='/u/'] → h5 → h3;
  label-driven صاحب المشروع ≤80 chars; scoped fallback first /u/ link text 1..60). ProfileUrl: owner-card
  /u/ href absolutized against https://mostaql.com ("/" inserted unless href starts with /). OwnerId:
  data-user-id attr → username segment after "/u/" → hash fallback hash*31+ord(c), abs(), over username
  then display-name; 0 when nothing found (SYNTHETIC, collision-prone — preserve).
  Numeric parsers: ParsePercent uses `\d+(?:[.,]\d+)?` after ToAsciiDigits, comma→dot, invariant double;
  ParseLeadingInt uses `\d{1,3}(?:[.,]\d{3})+|\d+` preferring grouped match (delete , and .), invariant int.
- published_at resolution: ONLY ParseRelativeNumber(publishedDateText) + raw text kept; NO absolute-date
  parsing anywhere. Proposals via ArabicProposalParser.Parse.
- Returns ProjectDetails with Url="" (scraper fills it), EnrichmentStatus=Enriched,
  DiscoveredAt=EnrichedAt=UtcNow (same instant), FieldProvenance dict + Mismatches list.
- Attachments extraction: DROPPED per scope.

#### D. StructuralExtractor ↔ InferenceEngine interaction

Structural = markup-anchored: panel `div#project-meta-panel` else first div with class-token containing
meta-container; rows = descendants with class-token containing meta-row, label/meta-value divs; owner
profile card located via profile_card/profile-card token-substring, else semantic (element whose text is
label صاحب المشروع → nearest div ancestor with >1 descendant elements), else details-area /u/-link nearest
card/box-classed ancestor. Path A: first table with class-token containing table; tr with EXACTLY 2 td.
Path B (no table): elements with class containing justify-between having exactly 2 element children.
Path C (gap-filler, always runs when no table): every descendant whose full normalized text EQUALS a
KnownLabel → value = next sibling ELEMENT; else StartsWith label and longer → remove ALL occurrences of
the label substring, trim. Label-driven = identifier-blind: elements whose OwnText (direct text-node
children, trimmed) label-normalizes equal to one of 17 KnownLabels, plus leaf elements whose full text
equals; WalkToValue adjacency ladder: next_sibling_of_label → next_td (if label is td) →
parent_next_sibling → parent_text_minus_label → grandparent_sibling_cell; first non-empty wins.

InferenceEngine (structure-independent): flatten DOM skipping script/style/noscript/template/textarea/
option/select (and anything nested); own text nodes only; `\s+`→" "; tokens carry index+element+dom-path.
Stem = normalize ws → strip `[\u064B-\u065F\u0670\u0640]` → trim punctuation set → strip prefixes
["ال","و","ف","ب","ل","لل"] repeatedly (remainder ≥2) then ONE suffix strip ["ها","هم","ه","ة","ات","ين",
"ون","ي","ا"] (remainder ≥2). Candidates seeded on digits; greedy merge up to 4 following connector/digit-%
tokens; evaluate bare seed AND merged string; keep longest per seed. Type regexes: Percent
`(\d+(?:\.\d+)?)\s*%`; Range `\$?\s*([\d.]+)\s*-\s*\$?\s*([\d.]+)`; AbsoluteDate (both dmy orders);
Float `\b\d+\.\d+\b`; Int `(?<!\.)\b\d+\b(?!\.\d)`; placeholder markers short-circuit to {PLACEHOLDER}.
Scoring over 14 field profiles: stem contribution `3.0 × damping(×0.35 if page-wide stem count ≥6) ×
(×0.5 if stem AFTER candidate) × 1/(1+min(tokenDist, domDist))` within ±12-token local window; unit +2.0
(±3-token window; hint inventories currency/duration/months/relative; requires_unit miss −1.5); type +1.0
expected / +0.25 weak; FLOAT candidate can NEVER satisfy profile lacking FLOAT. Dense-cluster prior +0.5
when ≥2 other candidates within dom-distance ≤3. Softmax per candidate (max-subtracted, zero-denominator
guarded). Resolve per field: margin top−runnerUp ≥0.20 ⇒ strategy "local_inference" else
"global_inference_ambiguous"; value = RawText (+ unit if exists and not contained); confidence =
round(topProb, 3); no candidates ⇒ (null, 0.0, "no_candidates_found").
NOTE: InferenceEngine's private Arabic-digit converter maps ONLY U+0660–0669, NOT Persian U+06F0–06F9 —
intentional divergence from StringNormalization.ToAsciiDigits; preserve both behaviors distinctly.

#### E. Persistence semantics (must replicate exactly)

DDL (Python drops `assets` and `app_secrets`):

```sql
CREATE TABLE IF NOT EXISTS projects (
    project_id INTEGER PRIMARY KEY,
    title TEXT NOT NULL,
    url TEXT NOT NULL,
    client_name TEXT,
    publish_time_number INTEGER,
    publish_time_text TEXT,
    proposal_count INTEGER,
    proposal_count_text TEXT,
    description TEXT,
    budget TEXT,
    delivery_days INTEGER,
    project_status TEXT,
    owner_id INTEGER,
    is_unread INTEGER NOT NULL DEFAULT 1,
    enrichment_status TEXT NOT NULL DEFAULT 'Pending',
    discovered_at TEXT NOT NULL,
    enriched_at TEXT
);

CREATE TABLE IF NOT EXISTS owners (
    owner_id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    profile_url TEXT,
    avatar_url TEXT,
    rating REAL,
    completed_projects_count INTEGER,
    hiring_rate_percent REAL,
    registered_at TEXT,
    open_projects_count INTEGER,
    in_progress_projects_count INTEGER,
    ongoing_communications_count INTEGER,
    last_seen_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS project_skills (
    project_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    url TEXT,
    FOREIGN KEY (project_id) REFERENCES projects (project_id)
);

CREATE TABLE IF NOT EXISTS discovery_backlog (
    project_id INTEGER PRIMARY KEY,
    discovered_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    retry_count INTEGER DEFAULT 0
);

CREATE VIRTUAL TABLE IF NOT EXISTS projects_fts USING fts5(
    project_id UNINDEXED,
    title,
    description,
    skills,
    tokenize = 'unicode61 remove_diacritics 2'
);
```

FKs are declared but NEVER enforced (PRAGMA foreign_keys never enabled) — do not enable in Python.
No indexes beyond implicit PK indexes; FTS sync is manual application code.

Write paths:

- InsertSummary (at discovery, before enqueue): tx { INSERT OR IGNORE INTO projects — identity +
  listing fields only (budget/delivery_days/project_status/owner_id/enriched_at stay NULL; is_unread
  bound as int 1/0; enrichment_status 'Pending'; discovered_at .NET "O"); ONLY IF rowsAffected>0 also
  INSERT INTO projects_fts (project_id,title,description,skills) VALUES (@id,@title,@desc,'') }.
  Result Ok(new-row?) — Ok(false) = duplicate, not an error.
- UpsertDetails: ONE transaction {

```sql
INSERT INTO projects (project_id,title,url,client_name,publish_time_number,publish_time_text,
    proposal_count,proposal_count_text,description,budget,delivery_days,project_status,owner_id,
    enrichment_status,discovered_at,enriched_at)
VALUES (16 params)
ON CONFLICT(project_id) DO UPDATE SET
    title=excluded.title, url=excluded.url, client_name=excluded.client_name,
    publish_time_number=CASE WHEN excluded.publish_time_number=0 THEN projects.publish_time_number ELSE excluded.publish_time_number END,
    publish_time_text =CASE WHEN excluded.publish_time_text='' THEN projects.publish_time_text ELSE excluded.publish_time_text END,
    proposal_count    =CASE WHEN excluded.proposal_count=0 THEN projects.proposal_count ELSE excluded.proposal_count END,
    proposal_count_text=CASE WHEN excluded.proposal_count_text='' THEN projects.proposal_count_text ELSE excluded.proposal_count_text END,
    description=excluded.description, budget=excluded.budget, delivery_days=excluded.delivery_days,
    project_status=excluded.project_status, owner_id=excluded.owner_id,
    enrichment_status=excluded.enrichment_status, enriched_at=excluded.enriched_at;

DELETE FROM project_skills WHERE project_id=@id;   -- re-insert one row per skill (url NULL if none)
DELETE FROM projects_fts  WHERE project_id=@id;    -- re-insert with skills=' '.join(skill names)
```

  discovered_at appears in INSERT column list but NOT in DO UPDATE SET (original discovery timestamp
  survives). owner_id bound NULL when model id == 0. budget/delivery_days/project_status NULL when None.
  SqliteException → log DB-002 + error result; any other exception → Fault log + RETHROW }
- Backlog ops: INSERT OR IGNORE INTO discovery_backlog (project_id) [defaults CURRENT_TIMESTAMP UTC
  second precision, retry_count 0]; DELETE WHERE project_id=@id;
  SELECT project_id ORDER BY discovered_at ASC (re-hydration order); DELETE WHERE discovered_at <
  datetime('now','-' || @days || ' days') returning deleted count (30 days, fire-and-forget at startup).
- Queries: GetAllKnownProjectIds → SELECT project_id FROM projects (ALL, HashSet);
  GetRecent(limit) → SELECT listing columns + COALESCE((SELECT group_concat(name,', ') FROM
  project_skills ...),'') ORDER BY (enriched_at IS NULL) ASC, enriched_at DESC, discovered_at DESC
  LIMIT @limit; GetNewestProjectId → ORDER BY (enrichment_status='Enriched') DESC,
  COALESCE(enriched_at,discovered_at) DESC LIMIT 1; GetDetails → LEFT JOIN owners +
  separate skills query (assets dropped); CountAddedToday → WHERE date(discovered_at)=date('now')
  (SQLite normalizes trailing offset to UTC); CountTracked → COUNT(*), COALESCE(SUM(is_unread),0);
  MarkAsRead → UPDATE ... SET is_unread=0 WHERE project_id=@id AND is_unread=1 (guarded);
  MarkAllAsRead → UPDATE ... WHERE is_unread=1. ClearAll/DeleteByProjectIdRange are seeder-only → dropped.

### F. Concurrency / control-flow / error-flow maps (summary)

- Two concurrent actors: poll task + N worker tasks sharing one unbounded FIFO queue. The ONLY mutable
  shared state is the in-flight set. DB uses per-operation connections (WAL, busy_timeout 5s) so readers
  coexist with writers.
- Startup order: paths init → global exception logging → DI build → pipeline start unless seeded/paused →
  PollService.StartAsync + WorkerPool.StartAsync off a lifetime CancellationTokenSource.
- Error flow per stage: listing poll failure = logged + status Error + next tick retries naturally (no
  fast-retry); diff provider failure = DIFF-001 aborts cycle; per-project enrichment failure = retried by
  ladder inside worker; unexpected worker exception = ENRICH-002 logged, worker survives; DB write failure
  on upsert = DB-002 logged (project remains Pending); parse failure = HTTP ParseFailed error result.
- DomainError codes: POLL-001 ListingFetchFailed, POLL-002 PollCancelled, HTTP-101 RateLimitExhausted,
  DIFF-001 KnownStateUnavailable, ENRICH-001 MaxAttemptsExhausted, ENRICH-002 Unexpected,
  DB-001 ConnectionFailed, DB-002 QueryFailed, DB-003 SchemaInvalid, DB-004 CommandFailed,
  PARSE EmptyHtml/MissingTitle/NoProjectRows, HTTP Timeout/RequestFailed/Unexpected/ParseFailed.
  External (Arabic) + internal messages + optional FixMessage + Cause — preserve codes and wording.

### G. Configuration map (C# side)

MAUI Preferences keys: `settings_poll_interval_seconds` (clamped 10..3600, default 30),
`settings_max_requests_per_minute` (default 2), `settings_safe_requests` (default true),
`query_params` (optional string, leading `?` normalized), `settings_is_polling_active`
(default false → paused on first run). Python equivalents defined in §8 contracts.

## 4. Behavioral parity trap checklists

### 4.1 Parsing traps (preserve or ledger)

1. NoProjectRows message says `div.project-card` though tier-2 selector is `project-item` — keep verbatim.
2. DetailParser returns Url=""; scraper attaches URL afterwards.
3. OwnerId is synthetic hash*31+c abs(); 0 when nothing found; collision-prone.
4. Bid-count override emits "{n} عروض" for every n incl. 1 and ≥11 (digits still round-trip).
5. Absolute dates poison PublishTimeNumber ("2024/05/01"→2024) — digit-run precedence wins.
6. TWO divergent Arabic-digit converters: inference maps only U+0660–69; StringNormalization also U+06F0–F9.
7. CleanHtml decodes entities BEFORE tag-strip (`&lt;b&gt;` becomes `<b>` then removed as tag);
   `<.*?>` misses tags spanning newlines (no DOTALL).
8. Zero-width/bidi chars (U+200E/F, U+200C/B) survive ALL normalization (category Cf; `\s` doesn't match
   them) — do NOT add stripping or label Contains/equality matching diverges on dirty pages.
9. HAP InnerText fuses inline-adjacent words without separators; lxml `text_content()` behaves alike —
   choose deliberately per call site (InferenceEngine flatten + NormalizeMultiline walk own-text instead).
10. Class matching styles differ intentionally: exact-token trick vs per-token substring vs raw whole-
    attribute substring — replicate each exactly where used.
11. Cross-validation is conditional: inference computed only when some field needed it; otherwise
    structural trusted blind (no mismatch records produced).
12. Placeholder-as-valid-resolution in SanityOk (carried through, nulled later) — never shortcut to
    "failed sanity".
13. completed_projects_count exempt from completion-status gate but subject to label-presence gate;
    started_since/deal_date/delivery_date gated by both.
14. Tier-3 dedupe keeps first occurrence; tiers 1–2 never dedupe.
15. ValuesAgree: null on either side counts as agreement (never a mismatch).
16. `@data-bid-item` attribute-name case: lxml lowercases HTML attribute names like HAP does — verified
    in tests, not assumed.
17. Concatenated-label repair removes ALL occurrences of the label substring from the value text.
18. Stem affixes: prefix stripping loops repeatedly; suffix stripped exactly once; remainder floor 2 chars.
19. Future timestamps clamp to "منذ لحظات"/0.
20. Softmax confidence rounded to 3 decimals; strategy flips at margin 0.20.
21. Proposal Format(≤0)="0 عرض"; Parse never yields negatives; "عرض واحد" requires the bigram.
22. Model defaults matter: IsUnread=true, EnrichmentStatus=Pending, empty-string-not-null for text
    fields, AvatarUrl/Rating permanently None, DiscoveredAt==EnrichedAt same instant in DetailParser.

### 4.2 Storage traps

1. TWO timestamp formats coexist: projects/app_secrets use .NET "O" round-trip
   (`2026-08-25T14:30:12.1234567+03:00`, 7-digit fraction + explicit offset) while discovery_backlog uses
   SQLite CURRENT_TIMESTAMP (`YYYY-MM-DD HH:MM:SS`, UTC, second precision). Python must emit BOTH formats
   byte-compatibly (custom formatter for the 7-digit fraction).
2. ORDER BY enriched_at DESC compares TEXT lexicographically — mixed offsets sort "wrong" relative to
   true time; reproduce byte-for-byte, do not normalize.
3. CountAddedToday relies on SQLite date() normalizing trailing offsets to UTC against UTC 'now'.
4. Sentinel conflict guards differ per column in UpsertDetails (0 or '' keep old; discovered_at never
   updated; proposal_count is listing-authoritative via the 0-guard).
5. NULL vs empty string is load-bearing (CASE guards, COALESCE, group_concat separators ', ' UI vs ' ' FTS).
6. Booleans stored as INTEGER 0/1; readers treat NULL as false.
7. owner_id == 0 binds NULL.
8. enrichment_status='Failed' is NEVER written today; terminal states are only 'Pending'/'Enriched';
   permanently-failed enrichments leave 'Pending' rows AND remove backlog entries.
9. FK constraints declared but unenforced — never enable foreign_keys pragma.
10. INSERT OR IGNORE (summary) vs ON CONFLICT DO UPDATE (details) asymmetry; rowsAffected>0 = was-new signal.
11. FTS5 standalone table, project_id UNINDEXED, tokenize unicode61 remove_diacritics 2, manual
    delete+reinsert sync, quoted-prefix per-term queries, ORDER BY rank ascending. No app-level Arabic
    orthography folding for search (hamza variants stay distinct tokens) — StringNormalization is
    parser-only.
12. No LIKE-based search anywhere — exclusively FTS MATCH.
13. Backlog ordering on second-precision CURRENT_TIMESTAMP ties resolve nondeterministically (rowid) —
    add no tie-breaker.
14. GetRecentAsync and search return different column layouts — map by name in Python.

## 5. Architecture alternatives considered

| Option | Verdict |
|---|---|
| Clean/Hexagonal layering (domain/application/infrastructure dirs) | Rejected — one pipeline, one producer/consumer flow; layers would be pass-through ceremony at this size |
| 1:1 namespace mirror of C# | Rejected as a rule, adopted as guidance — C# cohesion is genuine; collapse where Python idiom allows (repositories→one store module; Result<T>→typed exceptions at orchestrator boundaries) |
| Pure-functional dataflow (no classes) | Rejected — long-lived stateful loops (limiter, tracker, queue) fit small focused classes better |
| Repository pattern per table | Collapsed — single `ProjectStore` Protocol satisfies the mandated DB abstraction with less ceremony |
| **CHOSEN: responsibility-based package, explicit side-effect edges, storage behind one Protocol** | Best balance: mirrors natural boundaries (ingest / decide / persist / orchestrate), zero frameworks, testable seams exactly where C# already had interfaces |

## 6. Final architecture

Six cohesive areas; dependency direction strictly downward:
`runtime → pipeline → {scraping, storage} → {http, text, models, errors} → config`.

1. `models` + `text` + `errors` — pure, no I/O. Frozen dataclasses; Arabic ground utilities; typed
   exception hierarchy carrying stable DomainError codes.
2. `scraping` — owns HTTP fetching + HTML→model extraction. Parser-per-page-type preserved as a hard
   module boundary (steering-doc requirement). lxml.html chosen (XPath 1.0 parity with HtmlAgilityPack).
3. `storage` — one `ProjectStore` Protocol (the minimal DB abstraction; future Supabase/Postgres = new
   implementer, business logic untouched) + sqlite_store.py using stdlib sqlite3 executed via
   asyncio.to_thread (ms-scale ops; avoids aiosqlite dep; sync code directly unit-testable). Schema,
   PRAGMAs, timestamp formats byte-parity with C#.
4. `pipeline` — orchestration only: poller loop, queue, in-flight, diff, limiter, worker/pool. Contains
   no SQL dialects and no HTML.
5. `diagnostics` — interaction-log-compatible sink + stdlib logging wiring (same line format, MARK/
   ENTER/EXIT/FAULT/ERROR kinds, never raises).
6. `runtime` — composition root + asyncio lifecycle + signal handling (SIGINT/SIGTERM =
   RequestPipelineShutdown equivalent) + minimal PipelineEvents callback protocol (logging impl)
   replacing GlobalAppStatusService as an extension point.

Boundaries:

- Responsibilities: each area owns its side effects (scraping=network, storage=disk, diagnostics=log file).
- Forbidden dependencies: pipeline must not import storage.sqlite_store, http client internals, lxml,
  or sqlite3; models/text import nothing but each other; only runtime imports everything.
- Extension points: ProjectStore implementations; NotificationHook protocol (future); PipelineEvents
  subscribers; scraper/parser swap if markup changes.

## 7. Directory structure (result of the architecture)

```
.repertoire/python/
├── pyproject.toml            # hatchling; [tool.ruff] [tool.mypy] [tool.pytest] [tool.coverage]
├── uv.lock
├── README.md                 # quickstart + docs index
├── data/                     # default project-local SQLite location (gitignored)
├── docs/
│   ├── refactor-python-plan.md   # THIS FILE
│   └── agents-workflow.md        # governance
├── src/mostaql/
│   ├── __init__.py
│   ├── __main__.py           # python -m mostaql → runtime.main()
│   ├── config.py             # Settings dataclass + loader (env > config file > defaults)
│   ├── errors.py             # DomainError dataclass, error-code factories, exception hierarchy
│   ├── models/               # ProjectSummary, ProjectDetails, Owner, ProjectSkill,
│   │                         # EnrichmentStatus, FieldResolution/provenance
│   ├── text/
│   │   ├── normalization.py  # normalize / to_ascii_digits / strip_diacritics /
│   │   │                     # normalize_label / clean_html
│   │   ├── relative_time.py  # parse_relative_number
│   │   └── proposals.py      # parse_proposals
│   ├── http/
│   │   └── client.py         # AsyncClient factory: UA/Accept/Accept-Language headers,
│   │                         # 15s timeout semantics, typed error mapping
│   ├── scraping/
│   │   ├── scraper.py        # MostaqlScraper equivalent (URL building, error taxonomy)
│   │   └── parsers/
│   │       ├── listing.py    # ListingParser
│   │       ├── detail.py     # DetailParser (+ field combinator)
│   │       ├── structural.py # StructuralExtractor
│   │       ├── inference.py  # InferenceEngine
│   │       └── errors.py     # ParseException + PARSE error factories
│   ├── storage/
│   │   ├── schema.py         # DDL + PRAGMAs + user_version bootstrap + FTS backfill
│   │   ├── protocol.py       # ProjectStore Protocol (business-facing abstraction)
│   │   ├── sqlite_store.py   # SQLite implementation (projects/owners/skills/backlog)
│   │   ├── timestamps.py     # .NET "O"-compatible formatter/parser helpers
│   │   └── search.py         # FTS query building + search query execution
│   ├── pipeline/
│   │   ├── ratelimit.py      # async token bucket (monotonic clock)
│   │   ├── queue.py          # DiscoveryQueue over asyncio.Queue
│   │   ├── inflight.py       # InFlightTracker
│   │   ├── diff.py           # DiffEngine + KnownStateProvider protocol + impls
│   │   ├── enrich.py         # EnrichmentService (token → fetch)
│   │   ├── worker.py         # EnrichmentWorker retry ladder + finally-release
│   │   ├── pool.py           # WorkerPool startup/re-hydration/prune/stop
│   │   └── poller.py         # PollService loop (tick vs check-now race, pause, status)
│   ├── diagnostics/
│   │   └── interaction_log.py# interaction-log-compatible sink + logging setup
│   └── runtime.py            # composition root, lifespan, signals, PipelineEvents
└── tests/
    ├── unit/                 # parsers, text, models, limiter, diff, cookie-free logic
    ├── contract/             # ProjectStore contract suite (SQLite impl + in-memory fake)
    ├── integration/          # end-to-end pipeline with httpx MockTransport + temp DB
    ├── regression/fixtures/  # captured mostaql HTML + golden C# exports
    └── conftest.py
```

No `utils/` / `helpers/` dumping grounds. UNITS.md is unaffected — it catalogs UI units; the Python
backbone introduces none.

## 8. Shared contracts (frozen — parallel agents code against these)

Python ≥3.12. Signatures below are binding; changes require master approval and a plan-doc update.

```python
# --- errors.py ---
@dataclass(frozen=True, slots=True)
class DomainError:
    code: str; internal_message: str; external_message: str
    fix_message: str | None = None; cause: Exception | None = None

class BackboneError(Exception):           # base; carries .error: DomainError
class NetworkTimeoutError(BackboneError)  # HTTP timeout
class HttpRequestError(BackboneError)     # transport failure
class HttpUnexpectedError(BackboneError)
class ParseException(BackboneError)       # PARSE-* codes
class SchemaMismatchError(BackboneError)  # DB-003
class StoreOperationError(BackboneError)  # DB-002/DB-004

def poll_listing_fetch_failed(cause) -> DomainError        # POLL-001
def poll_cancelled() -> DomainError                        # POLL-002
def diff_known_state_unavailable(cause) -> DomainError     # DIFF-001
def enrich_max_attempts_exhausted(pid, attempts, last) -> DomainError   # ENRICH-001
def enrich_unexpected(pid, cause) -> DomainError           # ENRICH-002

# --- models (frozen dataclasses, slots=True) ---
class EnrichmentStatus(StrEnum): PENDING="Pending"; ENRICHED="Enriched"; FAILED="Failed"

@dataclass class ProjectSkill: name: str; url: str | None = None
@dataclass class Owner:
    owner_id: int = 0; name: str = ""; profile_url: str | None = None
    avatar_url: str | None = None; rating: float | None = None
    completed_projects_count: int | None = None; hiring_rate_percent: float | None = None
    registered_at: str | None = None; open_projects_count: int | None = None
    in_progress_projects_count: int | None = None; ongoing_communications_count: int | None = None

@dataclass class ProjectSummary:
    project_id: int; title: str; url: str = ""; client_name: str = ""
    publish_time_number: int = 0; publish_time_text: str = ""
    proposal_count: int = 0; proposal_count_text: str = ""
    description: str = ""; budget: str | None = None; delivery_days: int | None = None
    skills_text: str = ""; project_status: str | None = None
    is_unread: bool = True; enrichment_status: EnrichmentStatus = EnrichmentStatus.PENDING
    discovered_at: datetime; enriched_at: datetime | None = None

@dataclass class FieldResolution:
    value: str | None; source: str; confidence: float   # source: structural|inference|none

@dataclass class ProjectDetails(ProjectSummary-like fields plus):
    owner: Owner; skills: list[ProjectSkill]
    field_provenance: dict[str, FieldResolution]; mismatches: list[tuple[str, str|None, str|None]]

# --- text/normalization.py ---
def normalize(s: str | None) -> str
def to_ascii_digits(s: str | None) -> str                  # U+0660-69 AND U+06F0-F9
def strip_diacritics(s: str | None) -> str
def normalize_label(s: str | None) -> str
def clean_html(s: str | None) -> str

# --- text/relative_time.py / proposals.py ---
def parse_relative_number(text: str | None) -> int
def parse_proposals(text: str | None) -> tuple[int, str]

# --- http/client.py ---
class FetchFailure(Exception): ...                          # internal tri-state via subclasses
class PageFetcher:                                          # wraps httpx.AsyncClient
    def __init__(self, client: httpx.AsyncClient, timeout_seconds: float = 15.0): ...
    async def get_html(self, url: str, *, cancel: asyncio.Event | None = None) -> str
    # raises NetworkTimeoutError / HttpRequestError / HttpUnexpectedError;
    # caller cancellation propagates as asyncio.CancelledError

# --- scraping/parsers (sync, pure) ---
class ListingParser:  @staticmethod parse(html: str) -> list[ProjectSummary]
class DetailParser:   @staticmethod parse(project_id: int, html: str) -> ProjectDetails
# structural/inference expose internals used by detail.py only

# --- scraping/scraper.py ---
class MostaqlScraper:
    LISTING_URL = "https://mostaql.com/projects"
    DETAIL_URL_FORMAT = "https://mostaql.com/project/{0}"
    def __init__(self, fetcher: PageFetcher, now: Callable[[], datetime] = datetime_now_utc): ...
    async def fetch_listing(self, query_params: str | None, cancel) -> list[ProjectSummary]
    async def fetch_project_details(self, project_id: int, cancel) -> ProjectDetails
    # raises typed network errors or ParseException; fills details.url post-parse

# --- storage/protocol.py ---
class ProjectStore(Protocol):
    async def insert_summary(self, s: ProjectSummary) -> bool                # True=new row
    async def upsert_details(self, d: ProjectDetails) -> None                # raises StoreOperationError
    async def upsert_owner(self, o: Owner) -> None
    async def get_all_known_project_ids(self) -> set[int]
    async def add_to_backlog(self, project_id: int) -> None
    async def remove_from_backlog(self, project_id: int) -> None
    async def get_backlog_ids(self) -> list[int]                             # discovered_at ASC
    async def clean_old_backlog(self, days: int = 30) -> int
    async def get_recent(self, limit: int) -> list[ProjectSummary]
    async def mark_as_read(self, project_id: int) -> None
    async def mark_all_as_read(self) -> None
    async def count_added_today(self) -> int
    async def count_tracked(self) -> tuple[int, int]
    def search(self, query: str) -> list[ProjectSummary]                     # FTS, sync helper

# --- pipeline/ratelimit.py ---
class TokenBucketRateLimiter:
    DEFAULT_REQUESTS_PER_MINUTE = 2; FAST_MODE_REFILL_MULTIPLIER = 10.0
    SAFE_MODE_MINIMUM_SPACING = timedelta(seconds=1)
    def __init__(self, requests_per_minute=2, safe_requests=True, clock=time.monotonic): ...
    async def wait_for_token(self) -> None                                   # cooperative cancel
    def reconfigure(self, requests_per_minute: int, safe_requests: bool) -> None
    @property def available_tokens(self) -> float

# --- pipeline/diff.py ---
class KnownStateProvider(Protocol):
    async def known_project_ids(self) -> set[int]
class CommittedIdsProvider:      # wraps store.get_all_known_project_ids
class InFlightSetProvider:       # wraps InFlightTracker.snapshot()
class DiffResult: new_project_ids: list[int]; already_known_ids: list[int]
class DiffEngine:
    async def diff(self, polled: Sequence[ProjectSummary]) -> DiffResult
    # raises DiffStateError(DIFF-001) on provider failure

# --- pipeline/queue.py ---
class DiscoveryQueue:
    async def enqueue(self, project_id: int) -> None
    async def drain_all(self, cancel) -> AsyncIterator[int]   # multi-consumer safe
    def complete(self) -> None
    @property def count(self) -> int

# --- pipeline/inflight.py ---
class InFlightTracker:
    def try_mark_in_flight(self, project_id: int) -> bool     # atomic claim
    def mark_complete(self, project_id: int) -> None
    def is_in_flight(self, project_id: int) -> bool
    def snapshot(self) -> set[int]

# --- pipeline/enrich.py ---
class EnrichmentService:
    async def enrich(self, project_id: int) -> ProjectDetails  # token → scraper.fetch_details

# --- pipeline/worker.py ---
RETRY_DELAYS_SECONDS = (60, 120, 240, 480, 900)
class EnrichmentWorker:
    def __init__(self, worker_id, queue, enrichment, tracker, store, events): ...
    async def run(self, cancel: asyncio.Event) -> None        # never exits on unexpected errors

# --- pipeline/pool.py ---
class WorkerPool:
    WORKER_COUNT = 3
    def __init__(self, queue, enrichment, tracker, store, events): ...
    async def start(self, cancel: asyncio.Event) -> None      # re-hydrate backlog first
    async def stop(self) -> None                              # queue.complete + await workers

# --- pipeline/poller.py ---
class PollServiceStatus(Enum): IDLE, POLLING, BACKLOG_DRAINING, ERROR
class PollService:
    poll_interval_seconds: int = 30                           # re-read each tick
    query_params: str = ""
    status: PollServiceStatus                                 # observable via events hook
    def __init__(self, scraper, diff_engine, queue, tracker, store, limiter, events, clock=...): ...
    async def start(self, cancel: asyncio.Event) -> None      # immediate first poll unless paused
    async def stop(self) -> None
    def set_paused(self, paused: bool) -> None
    def request_check_now(self) -> None                       # bypasses pause
    async def poll_once(self) -> int                          # returns enqueued count; raises typed

# --- diagnostics/interaction_log.py ---
class InteractionLogger:                                    # process-wide singleton accessor
    def mark(self, checkpoint: str, variant: str, data=None) -> None
    def fault(self, checkpoint: str, exc: Exception, data=None) -> None
    def failure(self, checkpoint: str, error: DomainError, data=None) -> None
    # line format byte-parity with C# InteractionLogger; local-offset timestamp; never raises

# --- config.py ---
@dataclass(slots=True) class Settings:
    db_path: Path                    # env MOSTAQL_DB_PATH > config file > ./data/mostaqlk.db
    poll_interval_seconds: int = 30  # clamped 10..3600
    max_requests_per_minute: int = 2
    safe_requests: bool = True
    start_paused: bool = False       # intentional difference vs C# first-run-paused (§12)
    query_params: str = ""
    log_file_path: Path              # default <package>/../../log? NO → data/../log dir under data root
    log_level: str = "INFO"
def load_settings(env: Mapping[str, str], config_file: Path | None = None) -> Settings

# --- runtime.py ---
class PipelineEvents:   # replaces GlobalAppStatusService; logging implementation provided
    def on_status_changed(self, status) -> None
    def on_project_discovered(self, project_id: int, title: str) -> None
    def on_worker_state(self, worker_id: int, state: str) -> None
    def on_scan_succeeded(self, seen: int, enqueued: int) -> None
    def on_scan_failed(self, error: DomainError) -> None
    def on_enriched(self, details: ProjectDetails) -> None   # notification hook point (future)
async def main(argv: Sequence[str] | None = None) -> int   # signals, graceful shutdown
```

## 9. Dependency choices

Runtime deps (justified):
- `httpx` — async client with MockTransport test seam; chosen over aiohttp for transport mocking and
  API stability. Powers all scraping.
- `lxml` — XPath 1.0 parity with HtmlAgilityPack expressions (translate verbatim), fast C parser;
  `text_content()` matches HAP InnerText fusing semantics.
Everything else stdlib (`dataclasses`, `enum`, `sqlite3`, `asyncio`, `logging`, `pathlib`).

Rejected: pandas (record-oriented workload, no tabular batches); aiosqlite (to_thread suffices at this
request rate and keeps sync testability); BeautifulSoup (no native XPath 1.0; selector translation risk);
tenacity (bespoke fixed retry ladder is 10 lines); attrs/pydantic (stdlib dataclasses sufficient);
SQLAlchemy (direct SQL behind Protocol preserves exact C# semantics best).

## 10. Quality toolkit & gates

| Gate | Tool | Why selected | Command |
|---|---|---|---|
| lint+format+cyclomatic+security rules | ruff (incl. bandit `S` ruleset, mccabe ≤10) | one fast tool replaces flake8/black/isort/bandit overlap | `uv run ruff check .` / `uv run ruff format --check .` |
| type-check | mypy --strict | standard, pure-pip | `uv run mypy src` |
| cognitive complexity | radon+xenon (block grade ≤ B) | distinct dimension from cyclomatic; requested by requirements | `uv run xenon src -b B` |
| architecture boundaries | import-linter | enforces forbidden-import contracts mechanically | `uv run lint-imports` |
| tests+coverage | pytest + pytest-asyncio + pytest-cov (`--cov-fail-under=85`) | industry standard, async-native | `uv run pytest` |
| dependency security | pip-audit | maintained, uv-friendly | `uv run pip-audit` |
| build | hatchling via uv | mandated packaging | `uv build` |

import-linter contracts (enforced):
- `mostaql.pipeline` may NOT import: `mostaql.storage.sqlite_store`, `mostaql.http`, `lxml`, `sqlite3`
  (pipeline sees only `storage.protocol`, its own abstractions).
- `mostaql.models`, `mostaql.text`, `mostaql.errors` import nothing internal except each other.
- Only `mostaql.runtime` may import every layer.
- No module imports `httpx` outside `mostaql.http`.

## 11. Execution phases

| Phase | Work | Verification |
|---|---|---|
| 4 Foundation | uv init, hatchling packaging, pyproject tool configs, config loader, logging sink, directory skeleton | `uv sync && uv build`; gates green on skeleton |
| 5 Models/text/errors | domain dataclasses, enums, normalization + Arabic parsers | unit tests incl. §4.1 trap checklist |
| 6 HTTP/scraping core | PageFetcher, MostaqlScraper (URL/timeout/error mapping, no cookies) | MockTransport tests: timeout-vs-cancel distinction, non-2xx, header presence |
| 7 Parsing | listing/detail/structural/inference against captured HTML fixtures (reuse tools/capture_mostaql_projects.py; C# `--debug-via-json` export as golden output) | regression: same HTML ⇒ equivalent models; malformed matrix never aborts batch |
| 8 Persistence | schema bootstrap, ProjectStore Protocol + sqlite impl, FTS search, timestamp helpers | contract suite vs SQLite impl AND in-memory fake; sentinel-upsert/ordering/timestamp tests |
| 9 Orchestration | EnrichmentService, DiffEngine, InFlightTracker, queue wiring | unit tests w/ fake store/scraper |
| 10 Polling/concurrency | Poller loop (asyncio.wait race), pause/check-now, limiter, worker pool, re-hydration + prune, retry ladder, graceful shutdown | integration: fake-HTTP E2E incl. crash-restart re-hydration; bounded-concurrency assertions |
| 11 Parity | parity ledger filled (ordering, nulls, duplicates, dates, Unicode, whitespace, DB bytes) | documented C#/Py input-output pairs |
| 12 Hardening | failure injection: network, HTTP, parse, DB-busy, shutdown mid-cycle | no stuck in-flight IDs; no partial transactions; workers survive exceptions |
| 13 Quality gates | full toolkit, thresholds enforced | all gates red-proof |
| 14 E2E | short live rate-limited run against mostaql.com; inspect DB + log | evidence in final report |

## 12. Intentional behavioral differences ledger

| # | Difference | Reason |
|---|---|---|
| 1 | `Result<T>` replaced by typed exceptions caught at orchestrator boundaries; codes preserved in logs | idiomatic Python; error taxonomy unchanged |
| 2 | Limiter uses `time.monotonic()` not wall clock | immune to system clock changes; externally identical spacing/refill |
| 3 | First-run default is RUNNING (`start_paused=false`), C# defaults paused | headless daemon's purpose is polling; configurable |
| 4 | Worker-count live decrease quirk (cosmetic-only in C#) dropped — pool size fixed at start | C# behavior was documented-as-broken; increase-only still supported via config restart |
| 5 | Notifications entirely absent; `PipelineEvents.on_enriched` hook instead | user scope decision |
| 6 | Cookies/secrets/assets absent; anonymous scraping | user scope decision |
| 7 | Log file lives under the Python project data dir, not `%LocalAppData%\MostaqlK\log` | project-local isolation decision |
| 8 | DB file is project-local, not the C# app's file | user scope decision |
| 9 | `assets` and `app_secrets` tables omitted from schema | dead scope |
| 10 | `DesignDataSeeder`, ClearAll/DeleteRange seeder methods omitted | UI/dev-tool territory |
| 11 | GlobalAppStatusService replaced by PipelineEvents callbacks | no UI to observe |
| 12 | C# `GlobalAppStatusService.UpdateQueueCount` → `PipelineEvents.on_queue_count_changed(int)` callback | minimal event surface for headless runtime |
| 13 | Retry ladder injectable (`retry_delays` param, default 60/120/240/480/900s) instead of hard-coded array | testability; production default identical to C# |
| 14 | Enqueue after `complete()` raises RuntimeError (Channel closed analog) | asyncio.Queue has no built-in closed-write exception |
| 15 | Scraper detail URL constant kept positional `{0}` + `.format(project_id)` exactly like C# `string.Format` | frozen-contract conformance |

## 13. Testing strategy

- Unit: parsers (fixture HTML), text utilities (trap checklist), limiter (fake clock), diff engine,
  in-flight semantics, config loading, timestamp formatting.
- Contract: `ProjectStoreContractSuite` parameterized over sqlite impl + in-memory fake; future remote
  stores must pass the same suite.
- Integration: full pipeline with httpx.MockTransport feeding captured pages into temp SQLite;
  assertions on DB rows, FTS contents, backlog lifecycle, bounded concurrency.
- Regression: golden fixtures — captured real listing/detail HTML + exported JSON from the C#
  app (`--debug-via-json`) compared field-by-field.
- Hardening: malformed HTML matrices, network failures mid-cycle, shutdown during retries,
  concurrent poll-during-backlog races.

## 14. Completion checklist

- [x] Entire relevant backbone analyzed (§3)
- [x] Execution + data flows understood (§3.A/F)
- [x] C#→Python mapping documented (§8 contracts)
- [x] Architecture derived, alternatives considered, final justified (§5–6)
- [x] Directory structure follows architecture (§7)
- [x] Polling/scheduling migrated (pipeline/poller.py — tick-vs-check-now race, pause, immediate first poll)
- [x] HTTP/scraping migrated (http/client.py, scraping/scraper.py — exact headers, 15s timeout, error taxonomy)
- [x] Parsing/validation/normalization/transformation migrated (scraping/parsers/*, text/* — all trap checklists tested)
- [x] Business rules + dedup migrated (diff.py three-state engine, inflight.py, store sentinel upserts)
- [x] Persistence migrated; SQLite works; abstracted behind ProjectStore Protocol; pipeline imports no sqlite3/lxml/http (import-linter-enforced)
- [x] Remote-store replaceable without business-logic rewrite (contract suite passes against SQLite impl AND in-memory fake)
- [x] Configuration centralized (config.py; env > TOML > defaults); logging implemented (interaction-log byte-parity sink); explicit typed error handling (errors.py codes POLL/HTTP/DIFF/ENRICH/DB/PARSE); bounded retries (1m/2m/4m/8m/15m ladder, injectable for tests)
- [x] Bounded concurrency (fixed 3-worker pool) + graceful shutdown (signal handlers → poller.stop → pool.stop drain)
- [x] Unit/contract/integration/regression tests exist and pass — 487 tests, 95.42% branch coverage (fail_under=85 enforced)
- [x] Gates pass: ruff check/format · mypy --strict · xenon ≤B · lint-imports 3/3 · pytest+coverage · pip-audit (no vulnerabilities) · uv build (sdist+wheel)
- [x] Package builds; uv workflow works; all code under `.repertoire/python/*`
- [x] Documentation complete (this file + agents-workflow.md + README); end-to-end execution verified against fixture HTML via MockTransport (live mostaql.com run intentionally NOT performed during automated waves — see remaining risks)

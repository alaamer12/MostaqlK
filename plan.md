# MostaqlK — Development Plan

> Generated after reviewing: all `.repertoire/.steering/` docs (product + tech), both MVP design HTML mockups (`projects.html` and `project-details.html`), and the entire current codebase.

---

## Table of Contents

1. [Project Summary](#1-project-summary)
2. [Current Codebase State](#2-current-codebase-state)
3. [Design Review Notes](#3-design-review-notes)
4. [Missing Pieces and Technical Issues](#4-missing-pieces-and-technical-issues)
5. [Target Folder Structure](#5-target-folder-structure)
6. [Phase 1 — MVP: The Core Pipeline](#phase-1--mvp-the-core-pipeline)
7. [Phase 2 — MVP: Windows UI](#phase-2--mvp-windows-ui)
8. [Phase 3 — v2: Richer Windows Experience](#phase-3--v2-richer-windows-experience)
9. [Phase 4 — v3: Android Companion and Sync](#phase-4--v3-android-companion-and-sync)
10. [Dependency Map](#10-dependency-map)
11. [Implementation Notes and Conventions](#11-implementation-notes-and-conventions)

---

## 1. Project Summary

**MostaqlK** is a Windows desktop tray app (and future Android companion) that polls the Mostaql open-projects feed, detects newly posted projects via delta comparison against a local SQLite database, enriches them with full details, persists them locally, and fires a native toast per discovery — all without any cloud backend.

The pipeline is: **poll → diff → enqueue → enrich → commit → notify → display**.

The three delivery phases are:
- **v1 (MVP):** Full background pipeline + minimal UI to prove data flows correctly.
- **v2:** Richer Windows UX — search, filtering, query builder, notification grouping, unread highlighting.
- **v3 (stretch):** Android companion, LAN peer sync, push notifications via FCM/APNs.

---

## 2. Current Codebase State

### What Exists (template scaffolding only)

| File | State | Notes |
|---|---|---|
| `MostaqlK.csproj` | ✅ Exists | Targets `net10.0-android` and `net10.0-windows10.0.19041.0`. No NuGet packages beyond MAUI + Logging.Debug. |
| `MauiProgram.cs` | ✅ Exists | Vanilla MAUI template — only registers OpenSans fonts and debug logging. No DI registrations. |
| `App.xaml` / `App.xaml.cs` | ✅ Exists | Default template shell. `CreateWindow` returns a bare `AppShell`. |
| `AppShell.xaml` | ✅ Exists | Single route pointing to `MainPage`. |
| `MainPage.xaml` / `MainPage.xaml.cs` | ✅ Exists | Placeholder counter demo page — needs full replacement. |
| `Platforms/Windows/` | ✅ Exists | Standard WinUI `MauiWinUIApplication` subclass, `Package.appxmanifest`, `app.manifest`. No tray or notification code. |
| `Platforms/Android/` | ✅ Exists | Standard MAUI template — `MainActivity`, `MainApplication`, `AndroidManifest.xml`. No custom Android behavior. |
| `Resources/Styles/Colors.xaml` | ✅ Exists | MAUI default color tokens (purple theme). Needs full replacement with the blue/green palette from DESIGN.md. |
| `Resources/Styles/Styles.xaml` | ✅ Exists | MAUI default component styles. Needs updating to match design system. |
| `Resources/Fonts/` | ✅ Exists | Only `OpenSans-Regular.ttf` and `OpenSans-Semibold.ttf`. Missing Arabic fonts. |

### What Does Not Exist (all new work)

- `Features/` — no pages, view-models, or feature folders
- `Models/` — no domain models
- `Services/` — no polling, orchestration, or use-case code
- `Infrastructure/Scraping/` — no listing or detail parsers
- `Infrastructure/Storage/` — no SQLite setup, no repositories
- `Infrastructure/Notifications/` — no toast delivery
- `Infrastructure/Networking/` — no HTTP client configuration
- `Platforms/Windows/` tray icon implementation
- Any application configuration / settings model

---

## 3. Design Review Notes

The HTML mockups in `.repertoire/design/mvp/` define the full visual spec for what ships in the MVP UI. Key observations:

### `projects.html` (Project List / Dashboard)

- **Layout:** Fixed two-column (sidebar + main content area). The sidebar is 256px wide, fixed.
- **Sidebar contents:** Logo (`M` letter in blue pill), navigation links (المشاريع, البحث المتقدم, التنبيهات, الإعدادات, حول التطبيق), a "projects added today" counter, and a dark-mode toggle switch.
- **Header controls bar:** Combined search input + live status indicator (pulse dot + "مباشر") + rate budget display + pause/resume polling button + settings gear icon.
- **Active query indicator:** Shows the current `query_params` target ("كل المشاريع") with an edit button.
- **Project cards:** Each card has: title (bold for unread, normal weight for read), enrichment status badge (تم الإثراء / قيد الإثراء / فشل الإثراء), description excerpt (2 lines clamped), skill tags (blue chips), client info row (avatar, name, country, join year), time posted, and a bottom row with budget / delivery days / execution days / proposal count, plus unread indicator.
- **Inline-start border:** 4px colored border on card left edge (blue for unread, slate-300 for read) implemented via CSS logical property `border-inline-start` — correct RTL-aware approach.
- **Status bar (footer):** Total projects tracked, unread count + "تحديد الكل كمقروء" button, connection status dot, "last polled N seconds ago" + force-retry icon.
- **Font:** Tajawal (Google Fonts) — the Arabic UI font used across the mockup. This replaces the OpenSans currently in the codebase.

### `project-details.html` (Project Detail View)

- **Layout:** Back nav breadcrumb bar, then a 3-column grid (2 cols for main content, 1 col for sticky summary card on the right in RTL).
- **Main content:** Title block with enrichment badge and posted time, project description (full text, fluid height, `overflow-wrap: break-word`), required skills chips, and attachments section (only visible if assets exist).
- **Summary card (sticky):** Project status (مفتوح), posted date, budget, delivery days, proposal count, skill chips (compact), and owner profile (avatar, name, title, joined date, hire rate, open projects, in-progress projects, active communications).
- **Owner data fields:** `display_name`, `title`, `joined_at`, `hire_rate`, `open_projects_count`, `in_progress_projects_count`, `ongoing_communications_count` — all from the `owners` table schema.

### Design System (from `DESIGN.md`)

- **Primary accent:** `#2386C8` (Mostaql blue) / `#5CA8DE` dark mode
- **Positive accent:** `#2E9E6B` (nature green) / `#4FBF8C` dark mode
- **Font:** Tajawal for Arabic UI/content (from Google Fonts). A Latin grotesque for numbers and English metadata.
- **RTL:** Use logical properties throughout. Not just `dir="rtl"` on the root.
- **Theme:** Both light and dark at v1. Semantic CSS tokens, not hardcoded hex in components.
- **Icons:** Tabler Icons (outline style). Font Awesome is used in the HTML mockup as a placeholder — switch to Tabler for the actual MAUI implementation.

> **Note:** `DESIGN.md` references `shadcn/ui` and `Tailwind` — these are web framework concepts that do not apply directly to MAUI. The equivalent in MAUI is: define all color/font/spacing as `ResourceDictionary` tokens in XAML (`Colors.xaml`, `Styles.xaml`) and build reusable `ControlTemplate`s / `Style`s rather than ad-hoc per-control values.

---

## 4. Missing Pieces and Technical Issues

### Critical — Blocks MVP

| Issue | Detail | Fix |
|---|---|---|
| No NuGet packages for core functionality | `sqlite-net-pcl` or `SQLitePCLRaw` not added; `HtmlAgilityPack` or `AngleSharp` for HTML parsing not added; no HTTP client pooling | Add NuGet packages in Phase 1 |
| No tray icon implementation | MAUI on Windows has no built-in system tray; requires P/Invoke to Win32 Shell_NotifyIcon or a wrapper library | Implement `WindowsTrayService` using `NotifyIcon` equivalent in Phase 2 |
| Windows toast notifications via MAUI | MAUI does not expose WinRT toast APIs natively; requires `Microsoft.Toolkit.Uwp.Notifications` or `CommunityToolkit.WinUI.Notifications` NuGet | Add to csproj and implement in Phase 1 |
| `WindowsPackageType = None` in csproj | Unpackaged apps cannot use WinRT notification APIs without extra setup (COM server registration) | Use community toast library that supports unpackaged apps, or switch to packaged deployment |
| Missing Arabic font | `Tajawal` not bundled | Add font TTF files to `Resources/Fonts/` and register in `MauiProgram.cs` |
| No DI registrations | `MauiProgram.cs` only has fonts and logging — services, repositories, pages, view-models need wiring | Phase 1 and 2 work |
| `MainPage` is a placeholder | The counter demo page needs replacement with the `ProjectsPage` | Phase 2 work |

### Non-Critical — Should Fix Before v2

| Issue | Detail |
|---|---|
| `Colors.xaml` uses MAUI default purple palette | Must be replaced with the Mostaql blue/green design tokens |
| `ApplicationId` is `com.companyname.mostaqlk` | Should be updated to a real identifier |
| No app configuration file | Settings (poll interval, rate limit, etc.) have no persistence layer yet |
| `Package.appxmanifest` uses MAUI defaults | Title and other metadata should be updated |

### Architecture Watch Points

| Point | Detail |
|---|---|
| No-update policy must be enforced | `INSERT OR IGNORE` (not upsert) for the `projects` table at the SQLite layer — do not accidentally use `INSERT OR REPLACE` |
| In-flight tracker must use `ConcurrentDictionary<long, byte>` | Do not use a `HashSet<long>` with a lock — the concurrent dictionary is the designed approach (see `concurrency-model.md`) |
| Rate limiter is shared across BOTH tiers | The listing poll itself must call `WaitForTokenAsync()` — it is not exempt |
| `finally` block around `EnrichAndCommitAsync` is mandatory | Without it, a failed worker permanently hides the project from future polls |
| Transaction boundary: project row + skills (v2+) in one SQLite transaction | `MarkComplete` on `InFlightTracker` only after the transaction commits |
| Parser sanity check on listing | If zero project cards are parsed, treat it as a parse failure (not as "no new projects") |
| `DESIGN.md` web-only references | `shadcn/ui` and `Tailwind` do not translate to MAUI — implement the equivalent via MAUI `ResourceDictionary` and `Style`/`ControlTemplate` |

---

## 5. Target Folder Structure

All new code goes into these directories. Existing template files at the root remain unchanged until Phase 2 replaces `MainPage`.

```
MostaqlK/
├── App.xaml / App.xaml.cs                    [EXISTING — update to start background service]
├── AppShell.xaml / AppShell.xaml.cs          [EXISTING — update for multi-page navigation]
├── MainPage.xaml / MainPage.xaml.cs          [REPLACE in Phase 2 — becomes ProjectsPage]
├── MauiProgram.cs                            [EXISTING — extend with full DI wiring]
├── MostaqlK.csproj                           [EXISTING — add NuGet packages per phase]
│
├── Models/                                   [NEW in Phase 1]
│   ├── Project.cs
│   ├── Owner.cs
│   ├── AppSettings.cs
│   └── Enums/
│       ├── EnrichmentStatus.cs
│       └── PollState.cs
│
├── Services/                                 [NEW in Phase 1]
│   ├── PollOrchestrator.cs
│   ├── Interfaces/
│   │   ├── IProjectRepository.cs
│   │   ├── INotificationService.cs
│   │   ├── ISettingsService.cs
│   │   └── ITrayService.cs
│   └── Settings/
│       └── SettingsService.cs
│
├── Infrastructure/
│   ├── Scraping/                             [NEW in Phase 1]
│   │   ├── ListingParser.cs
│   │   └── DetailParser.cs
│   ├── Storage/                             [NEW in Phase 1]
│   │   ├── DatabaseInitializer.cs
│   │   ├── ProjectRepository.cs
│   │   └── OwnerRepository.cs
│   ├── Notifications/                       [NEW in Phase 1]
│   │   └── WindowsNotificationService.cs
│   └── Networking/                          [NEW in Phase 1]
│       └── HttpClientFactory.cs
│
├── Pipeline/                                [NEW in Phase 1 — core pipeline internals]
│   ├── InFlightTracker.cs
│   ├── TokenBucketRateLimiter.cs
│   ├── DiffEngine.cs
│   ├── Providers/
│   │   ├── IKnownStateProvider.cs
│   │   ├── SqliteCommittedProvider.cs
│   │   └── InFlightSetProvider.cs
│   └── WorkerPool.cs
│
├── Features/
│   ├── Projects/                            [NEW in Phase 2]
│   │   ├── ProjectsPage.xaml / .cs
│   │   ├── ProjectsViewModel.cs
│   │   ├── ProjectDetailPage.xaml / .cs
│   │   └── ProjectDetailViewModel.cs
│   ├── Settings/                            [NEW in Phase 2]
│   │   ├── SettingsPage.xaml / .cs
│   │   └── SettingsViewModel.cs
│   └── Notifications/                       [NEW in v2]
│       └── NotificationsPage.xaml / .cs
│
├── Platforms/
│   ├── Windows/
│   │   ├── App.xaml / App.xaml.cs           [EXISTING]
│   │   ├── Package.appxmanifest             [EXISTING — update metadata]
│   │   ├── app.manifest                     [EXISTING]
│   │   └── WindowsTrayService.cs            [NEW in Phase 2]
│   └── Android/                             [EXISTING — extended in Phase 4]
│
└── Resources/
    ├── Fonts/
    │   ├── OpenSans-Regular.ttf             [EXISTING]
    │   ├── OpenSans-Semibold.ttf            [EXISTING]
    │   ├── Tajawal-Regular.ttf              [ADD in Phase 2]
    │   ├── Tajawal-Medium.ttf               [ADD in Phase 2]
    │   └── Tajawal-Bold.ttf                 [ADD in Phase 2]
    └── Styles/
        ├── Colors.xaml                      [REPLACE in Phase 2]
        └── Styles.xaml                      [EXTEND in Phase 2]
```

---

## Phase 1 — MVP: The Core Pipeline

> **Goal:** The full `poll → diff → enqueue → enrich → commit → notify` pipeline runs correctly in the background, unattended, with no UI work yet. MVP is done when the pipeline fires exactly one accurate toast per new project, never duplicates, never drops under concurrent load, and stores full details in SQLite.

### Step 1.1 — Add NuGet packages

Edit `MostaqlK.csproj`:

```xml
<!-- HTML parsing -->
<PackageReference Include="HtmlAgilityPack" Version="1.*" />

<!-- SQLite ORM -->
<PackageReference Include="sqlite-net-pcl" Version="1.*" />
<PackageReference Include="SQLitePCLRaw.bundle_green" Version="*" />

<!-- Windows toast notifications (works unpackaged) -->
<PackageReference Include="Microsoft.Toolkit.Uwp.Notifications" Version="7.*"
                  Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'" />

<!-- MVVM toolkit for view-models (Phase 2) -->
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />

<!-- Optional: Polly for retry backoff -->
<PackageReference Include="Polly" Version="8.*" />
```

> The app currently uses `WindowsPackageType = None` (unpackaged). `Microsoft.Toolkit.Uwp.Notifications` v7+ supports unpackaged apps with a one-time COM registration call at startup. This is the recommended approach for a standalone `.exe` without MSIX packaging.

### Step 1.2 — Domain Models (`Models/`)

Create the following models. Keep them POCO — no UI framework references.

**`Models/Enums/EnrichmentStatus.cs`**
```csharp
public enum EnrichmentStatus { Pending, Enriched, Failed }
```

**`Models/Enums/PollState.cs`**
```csharp
public enum PollState { Idle, Polling, BacklogDraining, Error, Paused }
```

**`Models/Owner.cs`**
Full schema per `data-model-schema.md § owners`: `OwnerId`, `DisplayName`, `Title`, `JoinedAt`, `HireRate` (nullable decimal), `OpenProjectsCount`, `InProgressProjectsCount`, `OngoingCommunicationsCount`, `LastSeenAt`.

**`Models/Project.cs`**
Full schema per `data-model-schema.md § projects`: `ProjectId` (PK), `Title`, `Url`, `Description`, `OwnerId`, `Category`, `BudgetMin`, `BudgetMax`, `DeliveryDays`, `ProposalCount`, `PostedAt`, `ScrapedAt`, `IsRead`, `EnrichmentStatus`, `SourceQueryParams`.

Use `sqlite-net-pcl` attributes (`[Table]`, `[PrimaryKey]`, `[Column]`) on the model classes.

**`Models/AppSettings.cs`**
Plain POCO with: `PollIntervalSeconds = 30`, `MaxRequestsPerMinute = 2`, `MaxConcurrentDetailFetches = 2`, `QueryParams = ""`, `IncludeAssets = false`, `NotificationGroupingEnabled = false`.

### Step 1.3 — Infrastructure: Storage (`Infrastructure/Storage/`)

**`DatabaseInitializer.cs`**
- Gets the local app data path (use `FileSystem.AppDataDirectory` from MAUI).
- Creates and migrates the SQLite schema.
- Creates the `projects` table with the `project_id` column as `PRIMARY KEY` (the DB-level duplicate backstop).
- Creates the `owners` table.
- Does NOT create `project_skills` or `assets` tables yet (deferred to v2).
- Exposes `InitializeAsync()` — called once at startup before any pipeline work begins.

**`ProjectRepository.cs`** — implements `IProjectRepository`
- `GetExistingIdsAsync(IEnumerable<long> candidates)` — parameterized `SELECT project_id FROM projects WHERE project_id IN (...)`. Returns a `HashSet<long>`. This is the committed-ID provider.
- `InsertAsync(Project project)` — single-row insert using `INSERT OR IGNORE`. Returns `true` if the row was actually inserted (new), `false` if it was already present (duplicate backstop activated).
- `GetAllAsync()` / `GetPagedAsync()` — for the UI feed (Phase 2).
- `MarkReadAsync(long projectId)` — sets `is_read = true` (Phase 2).
- `MarkStatusAsync(long projectId, EnrichmentStatus status)` — updates `enrichment_status` only (used when marking a project as failed after max retries).

**`OwnerRepository.cs`**
- `UpsertAsync(Owner owner)` — owners are the one exception to the no-update policy (`last_seen_at` and stats are updated on re-encounter). Use `INSERT OR REPLACE` here, not on projects.

**`SettingsService.cs`** — implements `ISettingsService`
- Persists `AppSettings` as a JSON file in `FileSystem.AppDataDirectory`. Simple `System.Text.Json` serialize/deserialize.
- `LoadAsync()` / `SaveAsync(AppSettings settings)` / `GetAsync()` (cached in memory after first load).

### Step 1.4 — Infrastructure: Networking (`Infrastructure/Networking/`)

**`MostaqlHttpClient.cs`** (wrapper around `HttpClient`)
- Configures a single `HttpClient` with:
  - A descriptive `User-Agent` header (identify the app; do not disguise as a browser).
  - Reasonable `Timeout` (e.g. 30s).
  - `DefaultRequestHeaders.AcceptLanguage` set to `ar` (Mostaql returns Arabic content by default; this ensures consistency).
- Registers the client as a singleton for the lifetime of the process.

### Step 1.5 — Infrastructure: Scraping (`Infrastructure/Scraping/`)

This is a single isolated module per page type, as required by `error-handling-and-resilience.md § parser failures`.

**`ListingParser.cs`**
- Input: raw HTML string of `https://mostaql.com/projects` (or with `query_params`).
- Output: `IReadOnlyList<ProjectSummary>` where `ProjectSummary` is a `record {long ProjectId, string Title, string Url, string ClientName, int ProposalCount, DateTimeOffset PostedAt}`.
- Uses HtmlAgilityPack to select project card elements, extract `project_id` from the URL (the numeric segment), title, client name, proposal count string (then normalize), and relative time string (convert to absolute `DateTimeOffset` at parse time).
- **Sanity check:** if fewer than 1 card with a valid numeric ID is found, throw a `ListingParseException` — never return an empty list silently.
- Proposal count normalization: handle `"61 عرض"` → 61, `"عرض واحد"` → 1, `"عرضان"` → 2, `"أضف أول عرض"` → 0.
- Relative time normalization: `"منذ 4 ساعات"` → `DateTimeOffset.UtcNow - 4h` (resolve at parse time; never store the relative string).

**`DetailParser.cs`**
- Input: raw HTML string of `https://mostaql.com/projects/{id}`.
- Output: `ProjectDetail` record with all enriched fields: `Description`, `BudgetMin`, `BudgetMax`, `DeliveryDays`, `Category`, full `Owner` object.
- Budget normalization: `"$250.00 - $500.00"` → `(250.0m, 500.0m)`, nullable.
- Delivery normalization: `"20 يوما"` → 20, nullable.
- Owner hire rate: `"لم يحسب بعد"` → stored as `null`, not as a string.

### Step 1.6 — Pipeline: Core Concurrency Primitives (`Pipeline/`)

Implement exactly as specified in the tech docs.

**`InFlightTracker.cs`**
```csharp
public sealed class InFlightTracker
{
    private readonly ConcurrentDictionary<long, byte> _ids = new();

    public bool TryMarkInFlight(long projectId) => _ids.TryAdd(projectId, 0);

    public void MarkComplete(long projectId) => _ids.TryRemove(projectId, out _);

    public HashSet<long> Snapshot() => _ids.Keys.ToHashSet();
}
```

**`TokenBucketRateLimiter.cs`**
Implement exactly as shown in `worker-pool-and-rate-limiter.md`: token bucket with continuous refill, `SemaphoreSlim` gate, 250ms polling interval when depleted. Constructor takes `int requestsPerMinute`.

**`IKnownStateProvider.cs` + implementations**
```csharp
public interface IKnownStateProvider
{
    Task<HashSet<long>> GetKnownIdsAsync(IReadOnlySet<long> candidates);
}
```
- `SqliteCommittedProvider` — wraps `IProjectRepository.GetExistingIdsAsync`
- `InFlightSetProvider` — wraps `InFlightTracker.Snapshot()` (filters snapshot to only the candidates)

**`DiffEngine.cs`**
```csharp
public sealed class DiffEngine
{
    public async Task<DiffResult> ResolveAsync(
        IReadOnlySet<long> candidates,
        IEnumerable<IKnownStateProvider> providers)
    {
        var known = new HashSet<long>();
        foreach (var p in providers)
            known.UnionWith(await p.GetKnownIdsAsync(candidates));

        var unseen = candidates.Except(known).ToHashSet();
        return new DiffResult(unseen, known);
    }
}
```

**`WorkerPool.cs`**
- Creates an unbounded `Channel<long>` (FIFO queue — preserves fairness per backlog-handling spec).
- Starts `N` consumer `Task`s on `Task.Run`, each looping on `channel.Reader.ReadAllAsync()`.
- Each iteration: `await rateLimiter.WaitForTokenAsync()` → `await enrichAndCommitAsync(id)` inside a `try/finally` that always calls `inFlightTracker.MarkComplete(id)`.
- Exposes `EnqueueAsync(long projectId)` for the poll loop to call.
- `StartAsync()` / `StopAsync()` for lifecycle management.

### Step 1.7 — Infrastructure: Notifications (`Infrastructure/Notifications/`)

**`WindowsNotificationService.cs`** — implements `INotificationService`

On Windows, use `Microsoft.Toolkit.Uwp.Notifications`. For unpackaged apps, register the COM activator at startup (one-time call before any toast is shown):
```csharp
ToastNotificationManagerCompat.OnActivated += OnToastActivated;
```

`NotifyAsync(Project project)`:
```csharp
new ToastContentBuilder()
    .AddText(project.Title)
    .AddText($"{ownerName} · {project.ProposalCount} عرض · {formattedBudget}")
    .AddText(formattedPostedAt)
    .AddArgument("projectId", project.ProjectId)
    .Show();
```

Click action (`OnToastActivated`): parse `projectId` argument and signal the main window to navigate to that project detail page (wired up fully in Phase 2).

### Step 1.8 — Services: Poll Orchestrator (`Services/PollOrchestrator.cs`)

This is the top-level coordinator. It owns the poll loop and ties all pipeline components together.

**Dependencies (injected):** `IProjectRepository`, `IOwnerRepository`, `InFlightTracker`, `DiffEngine`, `WorkerPool`, `TokenBucketRateLimiter`, `ListingParser`, `DetailParser`, `INotificationService`, `ISettingsService`, `HttpClient`, `ILogger<PollOrchestrator>`.

**`StartAsync(CancellationToken ct)`** — the main poll loop:
1. `await rateLimiter.WaitForTokenAsync(ct)` — listing poll consumes a token from the shared budget.
2. Fetch `https://mostaql.com/projects{settings.QueryParams}` via `HttpClient`.
3. Parse with `ListingParser`. On `ListingParseException`: log the error, set `PollState.Error`, skip this cycle and continue to next scheduled poll.
4. Build the candidate `HashSet<long>` from parsed project IDs.
5. `DiffEngine.ResolveAsync(candidates, [sqliteCommittedProvider, inFlightSetProvider])`.
6. For each `unseen` ID: if `inFlightTracker.TryMarkInFlight(id)` returns `true`, call `workerPool.EnqueueAsync(id)`.
7. `await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), ct)`.
8. Loop.

**`EnrichAndCommitAsync(long projectId)`** — called by each worker (registered as the worker callback in `WorkerPool`):
1. `await rateLimiter.WaitForTokenAsync()`.
2. Fetch detail page via `HttpClient` with Polly retry (exponential backoff: 1 min → 2 min → 4 min, max 5 attempts).
3. Parse with `DetailParser`.
4. Begin SQLite transaction.
5. `ownerRepository.UpsertAsync(owner)`.
6. Build `Project` model with `EnrichmentStatus = Enriched`, `ScrapedAt = DateTimeOffset.UtcNow`.
7. `projectRepository.InsertAsync(project)`. If returns `false` (duplicate backstop activated): commit and return.
8. Commit transaction.
9. `notificationService.NotifyAsync(project)`.
10. On permanent failure (all retries exhausted): `projectRepository.MarkStatusAsync(id, EnrichmentStatus.Failed)`.

**`PauseAsync()` / `ResumeAsync()`** — called by the tray and UI to control polling.

### Step 1.9 — App Startup Wiring (`MauiProgram.cs`)

Register all services in the DI container and start the pipeline after the DB is initialized:

```csharp
// Shared infrastructure
builder.Services.AddSingleton<InFlightTracker>();
builder.Services.AddSingleton<DiffEngine>();
builder.Services.AddSingleton<ListingParser>();
builder.Services.AddSingleton<DetailParser>();

// Storage
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<IProjectRepository, ProjectRepository>();
builder.Services.AddSingleton<IOwnerRepository, OwnerRepository>();

// Settings (load before rate limiter is constructed)
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<TokenBucketRateLimiter>(sp => {
    var settings = sp.GetRequiredService<ISettingsService>().LoadAsync().GetAwaiter().GetResult();
    return new TokenBucketRateLimiter(settings.MaxRequestsPerMinute);
});

// Providers for DiffEngine
builder.Services.AddSingleton<IKnownStateProvider, SqliteCommittedProvider>();
builder.Services.AddSingleton<InFlightSetProvider>();

// Worker pool and orchestrator
builder.Services.AddSingleton<WorkerPool>();
builder.Services.AddSingleton<PollOrchestrator>();

// HTTP
builder.Services.AddHttpClient("mostaql", client => { /* configure User-Agent, timeout */ });

// Platform-specific services
#if WINDOWS
builder.Services.AddSingleton<INotificationService, WindowsNotificationService>();
builder.Services.AddSingleton<ITrayService, WindowsTrayService>();
#endif

// Start pipeline after app builds
var mauiApp = builder.Build();
await mauiApp.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
await mauiApp.Services.GetRequiredService<PollOrchestrator>().StartAsync(CancellationToken.None);
return mauiApp;
```

### Step 1.10 — Device Identity (`Services/IdentityService.cs`)

Implement "Buzz-style lite" per `identity-and-auth.md`:
- On first launch: generate a secp256k1 keypair. Use `BouncyCastle.NetCore` or `NSec` NuGet for key generation.
- Store private key bytes in `SecureStorage.SetAsync("device_identity_private", base64key)`.
- Derive a stable public identifier (or a UUID bound to the keypair) stored as `device_identity_public`.
- If `SecureStorage.GetAsync` returns null (key lost on OS wipe etc.), generate a fresh keypair and log a warning per the spec.
- The key is **dormant** in MVP and v2 — not used for any cryptographic operation yet. Just stored for future v3 peer sync activation.

---

## Phase 2 — MVP: Windows UI

> **Goal:** The designed window UI from the HTML mockups is implemented in MAUI XAML. The tray icon is live. The project feed reads from the local SQLite DB. The app behaves as a tray-resident tool — window hide-on-close, quit-only from tray.

### Step 2.1 — Design System (ResourceDictionary)

Replace `Resources/Styles/Colors.xaml` with the Mostaql blue/green semantic tokens:

```xml
<!-- Accent colors — per DESIGN.md -->
<Color x:Key="AccentPrimary">#2386C8</Color>
<Color x:Key="AccentPrimaryDark">#5CA8DE</Color>
<Color x:Key="AccentPositive">#2E9E6B</Color>
<Color x:Key="AccentPositiveDark">#4FBF8C</Color>
<Color x:Key="AccentWarning">#F59E0B</Color>
<Color x:Key="AccentDanger">#EF4444</Color>

<!-- Surface and background tokens -->
<Color x:Key="SurfaceLight">#FFFFFF</Color>
<Color x:Key="SurfaceDark">#1E293B</Color>
<Color x:Key="BackgroundLight">#F1F5F9</Color>
<Color x:Key="BackgroundDark">#0F172A</Color>
<Color x:Key="BorderLight">#E2E8F0</Color>
<Color x:Key="BorderDark">#334155</Color>
<Color x:Key="TextPrimaryLight">#1E293B</Color>
<Color x:Key="TextPrimaryDark">#E2E8F0</Color>
<Color x:Key="TextSecondaryLight">#64748B</Color>
<Color x:Key="TextSecondaryDark">#94A3B8</Color>
```

Use `AppThemeBinding` throughout all XAML components to switch between light/dark values automatically.

Add Tajawal font files (Regular, Medium, Bold) to `Resources/Fonts/` and register in `MauiProgram.cs`:
```csharp
fonts.AddFont("Tajawal-Regular.ttf", "TajawalRegular");
fonts.AddFont("Tajawal-Medium.ttf", "TajawalMedium");
fonts.AddFont("Tajawal-Bold.ttf", "TajawalBold");
```

### Step 2.2 — Projects Page (`Features/Projects/ProjectsPage.xaml`)

Replace `MainPage` entirely. This page is the feed view, matching `projects.html`.

**Layout:**
- Root `Grid` with two columns: sidebar (`260`) and main content area (`*`).
- `FlowDirection="RightToLeft"` on the root to match the RTL mockup.

**Sidebar:**
- App logo (blue rounded `Frame` with letter "M" + "Mostaqlk" text + version).
- Navigation items: a `VerticalStackLayout` of styled `Button`s with icons (Tabler Icons). Active item has a blue background tint and a brand-colored indicator bar on the inline-start edge.
- "Projects added today" stats widget.
- Dark mode toggle (a custom `Switch` styled as a pill toggle).

**Main area:**
- Header `Grid`: search `Entry` + status badge (live dot + "مباشر" text) + rate budget label + pause/resume `Button` + settings `ImageButton`.
- Active query indicator `Frame` (query params target + edit button).
- `CollectionView` for the project list (reverse-chronological, `ItemsSource` bound to `ProjectsViewModel.Projects`).
- Footer `HorizontalStackLayout`: tracked count, unread count, "mark all read" `Button`, connection dot, last polled time + force-retry `ImageButton`.

**Project card `DataTemplate`:**
The MAUI equivalent of the CSS `border-inline-start` trick:
```xml
<Grid ColumnDefinitions="4,*">
    <!-- Column 0: accent bar — color bound to IsRead -->
    <BoxView Grid.Column="0" Color="{Binding UnreadAccentColor}" />
    <!-- Column 1: card body -->
    <Frame Grid.Column="1" ... >
        <!-- title, badge, description, skills, client info, stats -->
    </Frame>
</Grid>
```
- Title `Label`: `FontAttributes` bound to `IsRead` (Bold when unread, None when read).
- Enrichment status badge: a `Frame` with `BackgroundColor` and `TextColor` bound to `EnrichmentStatus`.
- Skills: `FlexLayout` with `Wrap="Wrap"` containing skill chip `Frame`s.
- Budget, delivery days, proposal count displayed as labeled stat blocks in a `HorizontalStackLayout`.

**`ProjectsViewModel.cs`** (using `CommunityToolkit.Mvvm`):
- `[ObservableProperty] ObservableCollection<ProjectItemViewModel> Projects`
- `[ObservableProperty] string StatusText`
- `[ObservableProperty] int TotalProjects`, `int UnreadCount`
- `[ObservableProperty] string LastPollText`
- `[ObservableProperty] bool IsPolling`
- `[RelayCommand] Task PauseResume()` — calls `PollOrchestrator.PauseAsync()` or `ResumeAsync()`
- `[RelayCommand] Task ForceRetry()` — triggers an immediate poll cycle
- `[RelayCommand] Task MarkAllRead()`
- `[RelayCommand] Task NavigateToProject(long projectId)`
- Subscribes to a `PollOrchestrator.ProjectCommitted` event (or `WeakReferenceMessenger`) to add new projects in real time on the UI thread.

### Step 2.3 — Project Detail Page (`Features/Projects/ProjectDetailPage.xaml`)

Matching `project-details.html`.

**Layout:**
- Back navigation breadcrumb bar (`HorizontalStackLayout` with back arrow + project title truncated).
- Body: `Grid` with two columns (`2*` main content + `1*` summary card).
- Main content `ScrollView`: title block + enrichment badge + description `Label` (word-wrapped, fluid) + skills chips + attachments section (visible only when `Assets.Count > 0`).
- Summary card (`VerticalStackLayout` sticky via `VerticalOptions="Start"`): project metadata rows, skills chips (compact), owner profile (avatar initials `Frame`, name, title, stats).

**`ProjectDetailViewModel.cs`**:
- Receives `projectId` as navigation parameter (`[QueryProperty]`).
- `OnAppearing`: loads `Project` + `Owner` from repositories, calls `projectRepository.MarkReadAsync(ProjectId)`.
- Properties: `Project ProjectModel`, `Owner OwnerModel`, `bool HasAssets`.

### Step 2.4 — Settings Page (`Features/Settings/SettingsPage.xaml`)

Minimal for MVP — only expose the v1 settings:
- `poll_interval_seconds` — `Slider` (range 10–300) with a numeric display label.
- `max_requests_per_minute` — `Entry` with integer validation (1–10).
- `max_concurrent_detail_fetches` — `Picker` (items: 1, 2, 3, 4, 5).
- A "Save" button that calls `ISettingsService.SaveAsync()` and re-initializes the `TokenBucketRateLimiter` and `WorkerPool` with new values.

### Step 2.5 — Windows Tray Icon (`Platforms/Windows/WindowsTrayService.cs`)

MAUI on Windows has no built-in tray API. Implement using a hidden WinForms `NotifyIcon` on a dedicated STA thread (the lightest approach for an unpackaged MAUI/WinUI app):

```csharp
var trayThread = new Thread(() =>
{
    var notifyIcon = new System.Windows.Forms.NotifyIcon
    {
        Icon = LoadIcon("normal"),
        Text = "MostaqlK",
        Visible = true,
        ContextMenuStrip = BuildContextMenu()
    };
    System.Windows.Forms.Application.Run();
});
trayThread.SetApartmentState(ApartmentState.STA);
trayThread.IsBackground = true;
trayThread.Start();
```

Add `System.Windows.Forms` reference via `UseWindowsForms = true` in the csproj (Windows condition only). This is the standard pattern for MAUI unpackaged apps that need a tray icon.

**Tray icon states** (four distinct icon images stored in `Resources/Images/`):
- `tray_idle.ico` — normal brand icon (M on blue).
- `tray_polling.ico` — brightened/animated variant.
- `tray_error.ico` — red-tinted icon.
- `tray_paused.ico` — grey icon.

**Right-click context menu items (per `ui-ux-design.md`):**
- "Open main window" → `Application.Current.Windows[0].Show()` (or bring to front).
- Separator.
- "Pause / Resume monitoring" → `pollOrchestrator.PauseAsync()` / `ResumeAsync()`.
- "Check now" → immediate poll cycle.
- Separator.
- "Recent notifications" → submenu with last 5 committed project titles (stored in a circular buffer in `PollOrchestrator`). Clicking a recent item opens the detail page for that project.
- Separator.
- "Preferences / Settings" → opens `SettingsPage` in the main window.
- Separator.
- "Quit" → `Application.Current.Quit()`.

`ITrayService` interface:
```csharp
public interface ITrayService
{
    void SetState(PollState state);
    void AddRecentNotification(long projectId, string title);
}
```

### Step 2.6 — App Shell and Navigation (`AppShell.xaml`)

Update:
- `Shell.FlyoutBehavior = "Disabled"` (custom sidebar handles navigation).
- Register named routes:
  ```csharp
  Routing.RegisterRoute("projects", typeof(ProjectsPage));
  Routing.RegisterRoute("projectdetail", typeof(ProjectDetailPage));
  Routing.RegisterRoute("settings", typeof(SettingsPage));
  ```
- Use `Shell.GoToAsync("//projects")` as the startup route.
- Navigation to detail: `Shell.GoToAsync($"projectdetail?projectId={id}")`.

### Step 2.7 — App Lifecycle (Window Hide-on-Close)

In `App.xaml.cs`, intercept the close event to hide rather than quit:

```csharp
protected override Window CreateWindow(IActivationState? activationState)
{
    var window = new Window(new AppShell()) { Title = "MostaqlK" };
    window.Destroying += OnWindowDestroying;
    return window;
}

private void OnWindowDestroying(object? sender, EventArgs e)
{
    // Cancel the destroy; hide the window instead.
    // Only the tray "Quit" menu item calls Application.Current.Quit().
    // Implementation: set Window.IsVisible = false (where supported)
    // or use platform-specific WinUI window management.
}
```

---

## Phase 3 — v2: Richer Windows Experience

> **Goal:** All the v2 features listed in `overview.md § v2 scope`, building on top of the working MVP pipeline and UI.

### Step 3.1 — Query Params Override

- Add `QueryParams` text field to the Settings page (optional URL fragment like `?category=development`).
- The active query indicator in `ProjectsPage` reads from `settings.QueryParams` ("All projects" when empty; the actual fragment otherwise) with an inline edit button.
- `PollOrchestrator` reads `settings.QueryParams` each cycle and appends it to the base URL.
- Store `source_query_params` on each `Project` row — already in the schema.

### Step 3.2 — `include_assets`

- Add boolean toggle to Settings.
- In `EnrichAndCommitAsync`: after committing the project row, if `settings.IncludeAssets`, parse attachment URLs from the detail HTML, enqueue them via an asset-download sub-queue (also rate-limited via the shared budget, with a per-project cap on asset count).
- Create the `assets` table in SQLite (additive migration — `DatabaseInitializer` gains a `MigrateV2Async()` call).
- Display attachment thumbnails/links in `ProjectDetailPage` (the attachments section in the mockup is already designed).

### Step 3.3 — FTS5 Search Index

- Add a `projects_fts` FTS5 virtual table to the SQLite schema (created in `DatabaseInitializer`).
- In `ProjectRepository.InsertAsync`, after inserting the project row, insert `title + " " + description` into `projects_fts` in the **same transaction**.
- Implement `SearchAsync(string query)` in `ProjectRepository`:
  - Normalize the query at call time: fold Alef variants (أ/إ/آ → ا), strip diacritics (remove tashkeel), normalize ة/ه and ي/ى interchange.
  - Run `SELECT project_id FROM projects_fts WHERE projects_fts MATCH ?`.
  - For fuzzy tolerance, do a second pass on the result set with app-side edit-distance ranking.
- Wire the search input in `ProjectsViewModel` to call `SearchAsync` with 300ms debounce after the last keystroke.
- Normalize the FTS index content at write time using the same normalization function applied to queries.

### Step 3.4 — Dynamic Query Builder and Filters

- Add a "chips" strip above the project list for adding structured filters.
- `QueryBuilderViewModel` holds `ObservableCollection<FilterChip>` where `FilterChip` is a `record {string Field, string Operator, object Value}`.
- `QueryCompiler` class converts chips to a parameterized SQL `WHERE` clause — never raw string concatenation; field/operator whitelisted.
- Supported fields (from `search-and-filtering.md`): `title`, `category`, `posted_at`, `proposal_count`, `budget_min`, `budget_max`, `delivery_days`, `is_read`, `enrichment_status`.
- `unread only` = a pre-built shortcut chip for `is_read = false`.
- Sort control: any structured field, ascending/descending, plus `relevance` sort when a search query is active.

### Step 3.5 — Unread/Read Highlighting

Already partially wired in Phase 2 (card visual distinction is in the `DataTemplate`). Fully activate:
- `ProjectsViewModel.MarkAllReadCommand` calls `projectRepository.MarkAllReadAsync()`.
- Unread count in footer: `SELECT COUNT(*) FROM projects WHERE is_read = 0`.
- `ProjectDetailViewModel` calls `MarkReadAsync` on `OnAppearing` (already planned for Phase 2, now also updates the observable in `ProjectsViewModel` via messaging).

### Step 3.6 — Notification Grouping

- Add `notification_grouping_enabled`, `notification_grouping_mode`, `notification_grouping_param` to Settings.
- Implement `NotificationGroupingBuffer`:
  - Accepts new projects via `AddAsync(Project project)`.
  - Depending on mode (`end_of_minute`, `after_minutes`, `after_count`): accumulates until a time window or count threshold, then flushes.
  - Flush with exactly 1 item → individual detailed toast (falls back to per-project behavior).
  - Flush with 2+ items → summary toast ("هناك N مشاريع جديدة — تحقق منها هنا"), click opens window filtered to `is_read = false`.
- `PollOrchestrator.EnrichAndCommitAsync` routes through `NotificationGroupingBuffer.AddAsync` instead of calling `INotificationService.NotifyAsync` directly.

---

## Phase 4 — v3: Android Companion and Sync

> **Goal:** The mobile companion app, LAN pairing, and peer sync described in `roadmap-future.md`. This phase requires a second full application and significant new infrastructure.

### Step 4.1 — Android Platform Activation

- Implement `Platforms/Android/` — Android foreground service for background polling, Android notification channel setup, `AndroidNotificationService` implementation of `INotificationService`.
- Add `Services/Sync/` — sync use cases and contracts.
- Add `Infrastructure/Sync/` — LAN peer communication implementation (TCP listener/connector, manifest exchange protocol).

### Step 4.2 — Device Identity Activation

- `IdentityService` (dormant since Phase 1) becomes active: the stored secp256k1 keypair begins signing peer manifests.
- Add QR code generation in `SettingsPage` (Windows side) encoding LAN IP + port + short-lived pairing token.
- Add QR code scanner in the Android companion.
- On successful pairing, store the paired device's public identifier in the local DB.

### Step 4.3 — Peer Sync via Diff Engine

The `DiffEngine` already accepts pluggable `IKnownStateProvider` implementations. Add:
- `PeerManifestProvider` — exchanges `project_id` lists with a connected LAN peer over the established TCP connection.
- On reconnect, run `DiffEngine.ResolveAsync` in both directions (`desktop_missing`, `mobile_missing`).
- Each side requests and inserts the other's missing rows. The no-update policy means there are no conflict-resolution cases — presence/absence is the only thing to resolve.

### Step 4.4 — Push Notifications (FCM/APNs)

- Capture the mobile push token during QR/LAN pairing; store it locally on the desktop.
- When a new project is committed and the mobile device is paired, the desktop calls FCM/APNs directly using the stored push token (`push_notifications_enabled` setting controls this).
- Use `collapse_key` (FCM) / `apns-collapse-id` (APNs) defensively to avoid delivery storms on reconnect.
- Batching/summarization (multiple new projects → one "N new projects" push) is done on the sender (desktop) side, not by relying on the push infra to merge messages.

---

## 10. Dependency Map

The following table shows the strict ordering — nothing should be built before its dependencies are complete.

```
Step 1.1 (NuGet packages)
    └─► Step 1.2 (Models)
            └─► Step 1.3 (Storage: DB init + repositories)
            └─► Step 1.4 (Networking: HTTP client)
            └─► Step 1.5 (Scraping: parsers)
            └─► Step 1.6 (Pipeline: InFlightTracker + DiffEngine + WorkerPool + RateLimiter)
                    └─► Step 1.7 (Notifications: WindowsNotificationService)
                    └─► Step 1.8 (PollOrchestrator) ← depends on all of 1.3–1.7
                            └─► Step 1.9 (DI wiring in MauiProgram.cs) ← final Phase 1 step
                            └─► Step 1.10 (IdentityService) ← parallel with 1.9

Phase 1 complete ──►

Step 2.1 (Design system: Colors.xaml + fonts)
    └─► Step 2.2 (ProjectsPage + ProjectsViewModel)
    └─► Step 2.3 (ProjectDetailPage + ProjectDetailViewModel)
    └─► Step 2.4 (SettingsPage + SettingsViewModel)
Step 2.5 (WindowsTrayService) ← can be built in parallel with 2.1–2.4
Step 2.6 (AppShell navigation) ← after 2.2–2.4 routes exist
Step 2.7 (Window hide-on-close lifecycle) ← after 2.5 tray exists

Phase 2 complete ──►

Steps 3.1–3.6 (v2 features) ← each is relatively independent; order by user value:
    3.5 (unread highlighting) — smallest delta on Phase 2 work
    3.4 (query builder + filters)
    3.3 (FTS5 search)
    3.1 (query params)
    3.6 (notification grouping)
    3.2 (include_assets) — most complex, save for last

Phase 3 complete ──►

Phase 4 (v3 Android + sync) — all of Phase 3 must be stable first
```

---

## 11. Implementation Notes and Conventions

### Build and Tooling

- Use `dotnet` CLI for all package and build operations (`dotnet add package`, `dotnet build --framework net10.0-windows10.0.19041.0`).
- This is a pure .NET project — no bun/npm/node tooling needed at any phase.
- Debug on Windows target: `dotnet run --framework net10.0-windows10.0.19041.0`.

### C# Conventions

- All I/O methods are `async`/`await`. Never use `.Result` or `.Wait()` in production code (deadlock risk on MAUI's synchronization context).
- Use `ILogger<T>` (from `Microsoft.Extensions.Logging`) for all structured logging. No `Console.WriteLine` in production paths.
- Use `CancellationToken` propagation throughout the pipeline — the orchestrator's token flows into every `await`-able call so the pipeline shuts down cleanly on app quit.
- `record` types for immutable DTOs: `DiffResult`, `ProjectSummary`, `ProjectDetail`, `FilterChip`.
- `sealed` on all service implementations (prevents accidental subclassing).
- Namespace convention: `MostaqlK.{Layer}.{Sublayer}` — e.g. `MostaqlK.Infrastructure.Storage`, `MostaqlK.Features.Projects`, `MostaqlK.Pipeline`.

### MAUI XAML Conventions

- RTL: set `FlowDirection="RightToLeft"` on the root `ContentPage`. The layout engine mirrors `Start`/`End` margin and padding values automatically in RTL, which is the equivalent of CSS `margin-inline-start`.
- Dark mode: use `AppThemeBinding` on every color-bearing property. Never hardcode hex values in XAML component definitions.
- Use `CollectionView` (not `ListView`) for the project feed — better performance with large lists, more flexible item templates.
- ViewModels use `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`) to eliminate boilerplate `INotifyPropertyChanged` code.
- Navigation: `Shell.GoToAsync` with query parameters for the detail page (`?projectId=123`). Receive with `[QueryProperty("ProjectId", "projectId")]` on the view-model.

### Parsing Defensiveness

- Every HtmlAgilityPack selector call must be followed by a null-check — the site can change its markup at any time.
- `ListingParser` and `DetailParser` must remain pure functions: no direct DB or HTTP calls from inside them. They receive a string, return a typed result.
- Log the first 200 characters of any page that fails to parse (to aid debugging markup changes without storing the entire page body in logs).
- A listing page that parses to zero valid project cards must throw `ListingParseException`, not return an empty `IReadOnlyList<ProjectSummary>`. An empty parse looks identical to "no new projects" and would silently suppress the error state.

### SQLite Conventions

- `INSERT OR IGNORE` for the `projects` table — enforced in `ProjectRepository.InsertAsync`, not as a query hint but as the canonical insert method.
- `INSERT OR REPLACE` for the `owners` table only (the one legitimate update exception).
- All enrichment writes (project row, and in v2: FTS row) in a single `BeginTransaction` / `CommitTransaction` block. `InFlightTracker.MarkComplete` is called only after `CommitTransaction` succeeds.
- Use parameterized queries everywhere. The `QueryCompiler` in v2 must produce parameterized SQL — never string concatenation with user-supplied filter values.

### Design System Mapping (Web → MAUI)

| Web concept (from DESIGN.md / mockup) | MAUI equivalent |
|---|---|
| CSS custom properties (`--accent-primary`) | `ResourceDictionary` `Color` entries in `Colors.xaml` |
| `AppThemeBinding` light/dark | `AppThemeBinding` on every XAML property |
| `border-inline-start: 4px solid` (RTL accent bar) | `BoxView` as first column in a `Grid`, color bound to `IsRead` |
| `FlexLayout` wrapping skill chips | MAUI `FlexLayout` with `Wrap="Wrap"` |
| `line-clamp-2` on description | MAUI `Label` with `MaxLines="2"` + `LineBreakMode="TailTruncation"` |
| `overflow-wrap: break-word` (detail description) | MAUI `Label` with `LineBreakMode="WordWrap"` |
| Tailwind utility classes | Named `Style` resources in `Styles.xaml` |
| shadcn/ui card component | MAUI `Frame` or `Border` with styled content template |
| Font Awesome icons (in mockup) | Tabler Icons font (or PNG/SVG assets via `MauiImage`) |
| `position: sticky` (summary card) | `VerticalOptions="Start"` inside a `ScrollView` outer container |

---

*This plan reflects the state of the project as of the review date (August 2026). Update it as implementation decisions are made and new information emerges. The source of truth for scope and behavior remains the `.repertoire/.steering/` docs; this plan is a development-level interpretation of those docs, not a replacement for them.*

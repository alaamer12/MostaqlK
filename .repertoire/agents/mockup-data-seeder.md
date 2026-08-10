# mockup-data-seeder

## Goal

Make the data the app displays match the MVP mockups exactly by seeding the real SQLite store and
fixing genuine formatting defects in the display layer — without hardcoding display-only strings in
the views, and without touching colours, fonts, layout or sizing.

## What was done

### 1. Investigation

- Read `.repertoire/.steering/base/product/data-model-schema.md` and the live schema in
  `Infrastructure/Database/SqliteConnectionFactory.cs` (`InitialSchemaSql`).
- Dumped `C:\Users\amrmu\AppData\Local\User Name\com.companyname.mostaqlk\Data\mostaqlk.db`:
  97 projects, 96 unread, 36 added today. **`posted_relative` and `proposal_count` were `NULL` on
  every row**, and `budget` was stored verbatim as `"$250.00 - $500.00"`. Every row was `Enriched`.
- Extracted every string/number from `.repertoire/design/mvp/projects.html` and
  `project-details.html` verbatim.

### 2. Seeding mechanism (and why)

`Infrastructure/Database/DesignDataSeeder.cs`, invoked from `App.xaml.cs` via a new
`--seed-design-data` startup flag (`--seed-design-data=off` reverts), following the existing
`StartupNavigation.FromArguments` / `ResolveTheme` argument pattern.

Chosen over an ad-hoc external SQL script because it is reviewable, reproducible and goes through
the **normal repository layer** (`InsertSummaryAsync` → `UpsertDetailsAsync` → `IOwnerRepository`),
so the rows it writes are indistinguishable from pipeline-written rows (including FTS index
maintenance).

- **Idempotent**: each run calls the new `IProjectRepository.ClearAllAsync` first, so re-running
  produces identical rows.
- **Pipeline safety**: the flag latches a `design_parity_mode` preference; while it is set,
  `App` does not start the poll service or worker pool, so a later capture run (which only passes
  `--default-page`/`--theme`) cannot have the seeded rows buried by freshly scraped projects.

Seeded content:

| Row | Source | Notes |
|---|---|---|
| `1300001` | projects.html card 1 | تصميم موقع تعليمي تفاعلي, 4 skills, أحمد العتيبي, منذ 3 دقائق, 2500–5500, 20 days, 69 عرض, `Enriched`, unread |
| `1300002` | projects.html card 2 | كتابة محتوى تسويقي لمتجر إلكتروني, 3 skills, سارة المطيري, منذ 8 دقائق, 500–1000, 7 days, 69 عرض, `Pending`, read |
| `1300000` | project-details.html | تصميم وتطوير نظام SaaS لوكالات السياحة, full description, 8 skills, مشعل ا., `$1000.00 - $2500.00`, 60 days, 16 عرض, 2 attachments. Dated *yesterday* so it stays off the visible feed and out of "added today", but carries the newest `enriched_at`. |
| `1200000`–`1200143` | filler history | 144 read, older rows so the store legitimately reports **147 مشروع متتبَّع / 1 غير مقروء / 12 مشاريع مضافة اليوم**. They sort below the two design cards and are clipped by the status bar, so they never occupy the visible viewport. |

### 3. Formatting bugs fixed (defects, not data)

- **Budget** — new `Core/Formatting/BudgetFormatter.cs`. `ProjectCardViewModel.Budget` used to pass
  the raw scraped string straight through (`$250.00 - $500.00`). It now renders
  `2,500 - 5,500 ر.س`: thousands separator, no decimals, low value first, Saudi Riyal suffix.
  (The *details* page's بطاقة المشروع deliberately keeps the raw dollar string — that is what
  `project-details.html` itself shows.)
- **Relative timestamp** — root cause was a `NULL` `posted_relative` column on every scraped row,
  which the view model turned into a bare placeholder. Added
  `Core/Formatting/ArabicRelativeTime.cs`; `PostedRelative` now falls back to the same phrase
  rebuilt from the absolute `discovered_at` timestamp, so the slot is never empty.
- **Enrichment badge** — already driven by `Project.EnrichmentStatus`; the data was at fault (every
  row was `Enriched`). Card 2 is now seeded `Pending` and renders `قيد الإثراء` in the mockup's
  amber, card 1 `تم الإثراء` in green.
- **Day pluralisation** — `Delivery` said "7 يوم"; now "7 أيام" / "20 يوم" via `ArabicRelativeTime.Days`.
- **Avatar initials** — took the first two letters of the first word ("أح"); the mockup uses one
  initial per name part ("أع" for أحمد العتيبي, "سم" for سارة المطيري).
- **Status-bar totals** — counted only the loaded page of rows. New
  `IProjectRepository.CountTrackedAsync` makes them whole-store totals, which is the correct
  semantic for "مشروع متتبَّع".
- **Details route without an id** — `GetNewestProjectIdAsync` returned the newest *discovered* row,
  which can be an un-enriched stub with no description/budget/skills. It now prefers the most
  recently *enriched* row. `ProjectDetailsPage` is its only caller.

## Results — `overall_similarity`

| Page | Theme | Before | After | Δ |
|---|---|---|---|---|
| projects | light | 0.5932 | **0.6437** | +0.0505 |
| projects | dark | ~0.6070 | **0.6534** | +0.0464 |
| project-details | light | 0.5421 | **0.5877** | +0.0456 |
| project-details | dark | 0.5102 | **0.5551** | +0.0449 |
| settings | light | 0.5823 | **0.5859** | +0.0036 |
| settings | dark | 0.5250 | **0.5254** | +0.0004 |
| about | light | 0.6149 | **0.6149** | 0.0000 |
| about | dark | 0.6128 | **0.6128** | 0.0000 |

No page regressed. On projects/light the ORB `match_ratio` went from 0.088 to 0.322 and `ssim` from
0.734 to 0.754.

## How the data was confirmed to come from SQLite

1. The app was launched **only** with `--default-page`/`--theme` (no seeding flag) for every capture,
   and the poll service was disabled, so the only possible source of the rendered rows is the
   local database.
2. The database was dumped with `sqlite3` (stdlib, via `tools/.venv`) before and after seeding:
   147 rows / 1 unread / 12 added today, with the exact Arabic titles, `posted_relative`,
   `proposal_count`, `budget`, `delivery_days`, `enrichment_status`, `project_skills` and `owners`
   rows shown in the screenshots.
3. The rendered card text differs per card exactly as the stored rows differ (card 2 is `Pending`
   → amber badge and no unread marker; card 1 is `Enriched` + unread), which no hardcoded view text
   could produce.
4. `budget` is stored as `2500 - 5500` yet renders `2,500 - 5,500 ر.س`, proving the value flows
   through the repository → view-model formatter, not a literal in XAML.

## Files touched

- `Core/Formatting/BudgetFormatter.cs` (new)
- `Core/Formatting/ArabicRelativeTime.cs` (new)
- `Infrastructure/Database/DesignDataSeeder.cs` (new)
- `Infrastructure/Database/IProjectRepository.cs` (+`CountTrackedAsync`, `ClearAllAsync`)
- `Infrastructure/Database/ProjectRepository.cs` (implementations + enriched-first newest query)
- `Features/Projects/ViewModels/ProjectCardViewModel.cs` (budget, relative time, delivery, initials)
- `Features/Projects/ViewModels/ProjectFeedViewModel.cs` (store-wide tracked/unread totals)
- `App.xaml.cs` (`--seed-design-data` handling, pipeline gating)
- `MauiProgram.cs` (seeder registration)
- `UNITS.md` (new "Display formatters" and "Startup flags" sections)

## Verification

- `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -v:q` → **0 errors, 0 warnings**.
- `tools\parity_check.py --all` (all 8 combinations) after seeding; per-combination scores above.
- Feed rendered in the success state on every run — no empty state, no stuck shimmer, no error card.
- No colour, `FontFamily`, layout or sizing values were changed; no screenshots under `tools/temp`
  were overwritten (the harness writes new `_vN` files). Scratch files were deleted.

## Known remaining gaps

- The sidebar notifications badge still shows `0` (mockup `5`). There is no notifications table in
  the V1 schema, so there is no honest data source for it; binding it would require inventing a
  counter. Left alone deliberately.
- `ProjectCardViewModel.ClientMeta` ("السعودية • عضو منذ 2021") is still a constant — the `owners`
  table has no country/joined-at columns in the V1 schema, so card 2 shows 2021 where the mockup
  shows 2020. Fixing it properly needs a schema migration.
- `Execution` ("مدة التنفيذ") is derived from `delivery_days` because no execution-duration column
  exists: card 1 matches (60 يوما), card 2 shows 21 instead of 45.

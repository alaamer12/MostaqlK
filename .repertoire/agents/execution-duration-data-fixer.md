### Goal

Fix the fabricated execution-duration ("مدة التنفيذ") number on project cards
(`Features/Projects/ViewModels/ProjectCardViewModel.cs`), which previously rendered
`days * 3` instead of a real scraped value, and add automated coverage that proves
cards never show a fabricated number.

### Findings

- The core fix (root cause + repair) was **already committed** in this same session,
  in commit `b7fed9f` ("Fix fabricated execution duration, search empty-state/footer
  counts, toast notifications, and restore default title bar"), before this task began:
  - `ProjectCardViewModel.Execution` (line ~132) now binds to `Project.DeliveryDays`
    and renders `"{days} يوما"` or the placeholder `"—"` — no `days * 3` fabrication
    remains anywhere in the codebase (verified via full-repo grep).
  - `Project.DeliveryDays` is real data: `Infrastructure/Http/Parsers/DetailParser.cs`
    maps the Arabic label "مدة التنفيذ" -> field key `"duration"` and produces
    `ProjectDetails.DeliveryDays` from it.
  - The `delivery_days` column already exists in the `projects` table schema
    (`Infrastructure/Database/SqliteConnectionFactory.cs`), and
    `Infrastructure/Database/ProjectRepository.cs` upserts/reads it for both
    `ProjectSummary` (feed) and `ProjectDetails` (detail) rows — **no DB migration was
    needed**, the column already existed.
  - `Services/Pipeline/WorkerPool/EnrichmentWorker.cs` calls
    `_projectRepository.UpsertDetailsAsync(details, ...)` with the `ProjectDetails`
    produced by `DetailParser.Parse`, so the real duration is persisted once a project
    is enriched, and cards discovered-but-not-yet-enriched correctly show `"—"` until
    then.
  - The listing/summary scrape (`ListingParser.cs`) does **not** expose a duration
    field (only detail pages do), which matches the existing "—" placeholder design.

### What I did in this session

Since the production fix was already in place, my contribution focused on closing the
test-coverage gap requested by the task:

1. Added `AutomationId="ProjectCard_ExecutionLabel"` to the execution-duration `Label`
   in `Features/Projects/Views/ProjectCard.xaml` (line 164) so the value is reachable
   from Appium/WinAppDriver UI tests (it previously had no automation id).
2. Added a new regression test,
   `ProjectCard_ExecutionLabel_ShowsRealDurationOrPlaceholder_NeverFabricated`, to
   `MostaqlK.UITests/ProjectsPageTests.cs`. It reads every visible card's execution
   label text and asserts each one matches either the placeholder `"—"` or the real
   `"<days> يوما"` pattern (`^\d+\s*يوما$`) — i.e. it fails if any fabricated/garbled
   value is ever rendered.

### Files touched

- `Features/Projects/Views/ProjectCard.xaml` — added `AutomationId` to the execution
  label (no other changes).
- `MostaqlK.UITests/ProjectsPageTests.cs` — added the new regression test.
- No production C# logic was changed (the actual bug fix predates this session's work
  and was already committed in `b7fed9f`).
- Excluded files per instructions (`AppSidebar.*`, `SettingsPanel.xaml`,
  `ProjectDetailsPage.xaml`, `AboutPage.xaml`, `App.xaml.cs`, `MauiProgram.cs`,
  `Services/Diagnostics/*`, `DesignDataSeeder.cs`, `NotificationDispatcher.cs`,
  `NotificationGrouper.cs`, `WindowsToastSender.cs`) were **not** touched.

### DB migration

None added — the `delivery_days` column already existed in the schema and was
already wired end-to-end (parser -> repository -> view model) prior to this session.

### Verification

- `dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0` →
  **Build succeeded, 0 Warnings, 0 Errors.**
- `dotnet test MostaqlK.UITests --filter "FullyQualifiedName~ProjectCard_ExecutionLabel_ShowsRealDurationOrPlaceholder_NeverFabricated"`
  → ran against the real built `.exe` via WinAppDriver/Appium →
  **Passed (1/1).**

### Skill used

None of the listed `.cursor/skills/` entries applied to this repo (it's a MAUI/C#
Windows app, not the TypeScript/React stack the skills catalog targets); worked
directly per the issue's explicit numbered instructions instead.

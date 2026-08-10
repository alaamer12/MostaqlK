# ui-catalog-automationid-builder — Report

## Goal

Step 2 of `.junie/plans/appium-ui-test-catalog-and-fixes.md`: build `docs/ui-test-catalog.md`
enumerating every dynamic/interactive element across the 4 MVP pages + shared sidebar +
notifications flyout, add a stable `AutomationId` to each in XAML, and apply the existing
`[TraceInteraction]`/`TraceScope` diagnostic pattern to the key command/handler methods behind
them, without changing existing behavior.

## Actions taken

1. Read `.junie/plans/appium-ui-test-catalog-and-fixes.md` and `UNITS.md` in full to confirm the
   AutomationId naming convention (`<Page>_<Element>`) and the existing `TraceInteraction`/
   `TraceScope`/`InteractionLogger` mechanism (already implemented in Step 1 — not touched here).
2. Walked all 7 XAML views listed in the task and their code-behind/view-models to enumerate
   every clickable/inputtable/pannable/dynamic-data element.
3. Wrote `docs/ui-test-catalog.md` — one `###`-per-page markdown table
   (`Element | AutomationId | Interaction Kind(s) | Backend it calls | Source file:line | Planned
   Appium test name`), plus a "Counts" section.
4. Added `AutomationId` directly on the exact control owning the gesture/command in every XAML
   file listed below (never on a wrapping container).
5. Wrapped `TogglePolling`, `RefreshAsync` (backs `RefreshCommand`), `SelectProjectAsync` (backs
   `ProjectCard`'s `SelectCommand`), `SaveAsync` (Settings `SaveCommand`), `ResolveAsync`
   (attachment `ResolveCommand`), and all 5 `AppSidebar` nav click handlers with
   `[TraceInteraction("...")]` + `using var _ = TraceScope.Begin(...)` / `_.MarkFaulted(ex)` in a
   `catch` that rethrows — existing logic/behavior unchanged.
6. Ran `dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0` → **0 errors, 0
   warnings**.

## Files touched

- `docs/ui-test-catalog.md` (new)
- `UI/PlatformComponents/AppSidebar/AppSidebar.xaml` — 5 `AutomationId`s (`Sidebar_ProjectsButton`,
  `Sidebar_AdvancedSearchButton`, `Sidebar_NotificationsButton`, `Sidebar_SettingsButton`,
  `Sidebar_AboutButton`)
- `UI/PlatformComponents/AppSidebar/AppSidebar.cs` — `[TraceInteraction]`/`TraceScope` on all 5
  `On*Clicked` handlers
- `Features/Projects/Views/MainWindowPage.xaml` — `Projects_SearchInput`,
  `Projects_TogglePollingButton`, `Projects_SettingsGearButton`, `Projects_RetryButton`,
  `Projects_ProjectsCollectionView`, `Projects_MarkAllReadLabel`, `Projects_RefreshLabel`,
  `Notifications_Flyout`
- `Features/Projects/ViewModels/ProjectFeedViewModel.cs` — `TraceScope` on `TogglePolling`,
  `RefreshAsync`, `SelectProjectAsync`
- `Features/Projects/Views/ProjectCard.xaml` — `ProjectCard_Root`
- `Features/Projects/Views/ProjectDetailsPage.xaml` — `Details_BackButton`,
  `Details_ScrollView`, `Details_AttachmentsList`, `Details_AttachmentResolveButton`
- `Features/Projects/ViewModels/ProjectDetailsViewModel.cs` — `TraceScope` on
  `AttachmentItemViewModel.ResolveAsync`
- `Features/Projects/Views/AboutPage.xaml` — `About_ScrollView`, `About_MostaqlLink`
- `Features/Settings/Views/SettingsPanel.xaml` — `Settings_PollIntervalInput`,
  `Settings_RequestsPerMinuteInput`, `Settings_GroupingModePicker`,
  `Settings_GroupingThresholdInput`, `Settings_DarkModeToggle`, `Settings_SaveButton`
- `Features/Settings/ViewModels/SettingsViewModel.cs` — `TraceScope` on `SaveAsync`
- `Features/Notifications/Views/RecentNotificationsFlyout.xaml` — `Notifications_List`,
  `Notifications_Row`

`UNITS.md`, `App.xaml.cs`, and `Services/Diagnostics/*` were **not** modified (per task
constraints — the diagnostic mechanism they document already existed from Step 1 and needed no
changes; `UNITS.md`'s AutomationId naming-convention section already covered this step's
convention, so no new row was required).

## Decisions

- Elements with no gesture recognizer/command (e.g. sidebar `NotificationCount` badge, live
  status pill, validation message, version label) are listed in the catalog as dynamic-data-only
  rows without an `AutomationId`, since the task requires ids only on interactive/dynamic
  elements that Appium needs to target, and adding ids to every static label would violate
  "unique-within-page, on the exact control owning the interaction."
- Template-repeated controls (`ProjectCard_Root`, `Details_AttachmentResolveButton`,
  `Notifications_Row`) intentionally reuse the same `AutomationId` per row (one instance visible
  per feed/list item at a time from the test's perspective); this matches the task's own example
  naming (`ProjectCard_Root`).
- `SearchInputField`'s built-in clear ("x") button is documented in the catalog but not given a
  separate `AutomationId` — it's an internal part of the existing `SearchInputField` unit (see
  `UNITS.md`), out of this task's file-edit scope.

## Verification

- `dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0` → **Build succeeded, 0
  Warning(s), 0 Error(s)**.
- No XAML paths differed from the task's listed paths — all 7 files existed exactly where
  specified.
- Not run: Appium/WinAppDriver tests (out of scope for this step; Steps 3-6 own the actual test
  files per the plan).

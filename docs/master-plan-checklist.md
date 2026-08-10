# Master Plan Checklist

Owned by the **master** agent. Only the master ticks `- [x]`, and only after independently
re-verifying a slave's claimed fix. Slaves report honestly (fixed / still broken / unverifiable).

Plan reference: `.junie/plans/appium-ui-test-catalog-and-fixes.md`.

## Step 1 — Master checklist, shared diagnostics utility, design-parity/backend startup fix
- [x] Write this checklist file grouped by Delivery Step.
- [x] Add `Services/Diagnostics/InteractionLogger.cs` (structured log sink + `Mark(checkpoint, variant, data)` A/B helper) writing to `FileSystem.AppDataDirectory`.
- [x] Add `Services/Diagnostics/TraceInteractionAttribute.cs` (+ `TraceScope` helper) for command tracing.
- [x] Fix `App.xaml.cs` `ApplyDesignDataArgument`/pipeline-gating so a stale persisted `design_parity_mode` preference from a previous run can never permanently disable `IPollService`/`WorkerPool` — only the current launch's explicit `--seed-design-data` flag may do so.
- [x] Verify `dotnet build` succeeds after the diagnostics + startup fix (net10.0-windows10.0.19041.0).
- [ ] Master re-runs the built `.exe` fresh (no flags) and confirms via `interaction-log.txt` that `App.Startup.PipelineStarted` is logged and the real pipeline runs against non-seeded data. *(Code fix confirmed by inspection: `explicitlySeededThisLaunch` now gates purely on this run's argv, not the persisted preference — interactive exe launch to tail the log is still pending and tracked here.)*
- [ ] Inspect on-disk store `C:\Users\<user>\AppData\Local\User Name\com.companyname.mostaqlk\Data` and `Preferences` (`design_parity_mode`, `settings_*`) to confirm current machine state and reset/reseed if stuck.
- [x] Update `UNITS.md` with the `TraceInteraction`/`InteractionLogger`/`Mark` diagnostic mechanism.

## Step 2 — UI interaction catalog + AutomationIds
- [ ] Enumerate every dynamic-data/clickable/inputtable/pannable/animated/draggable/keyboard/focusable element across `.repertoire/design/mvp/{projects,project-details,settings,about}.html` vs their XAML views, including commonsense counterpart states.
- [ ] Write `docs/ui-test-catalog.md` (Element | AutomationId | Interaction Kind(s) | Backend | Source | Test).
- [ ] Add `AutomationId` to `AppSidebar.xaml`.
- [ ] Add `AutomationId` to `MainWindowPage.xaml`.
- [ ] Add `AutomationId` to `ProjectCard.xaml`.
- [ ] Add `AutomationId` to `SettingsPanel.xaml`.
- [ ] Add `AutomationId` to `ProjectDetailsPage.xaml`.
- [ ] Add `AutomationId` to `AboutPage.xaml`.
- [ ] Add `AutomationId` to `RecentNotificationsFlyout.xaml`.
- [ ] Apply `[TraceInteraction]`/`InteractionLogger` calls to `TogglePolling`, `RefreshCommand`, `SaveCommand`, `SelectCommand`, `ResolveCommand`, sidebar nav handlers.
- [ ] Update `UNITS.md` with the AutomationId naming convention.

## Step 3 — Appium/WinAppDriver debug tooling
- [ ] Add `MostaqlK.UITests/Utils/UiDebugger.cs` (`DumpPageSource`, `WaitAndFind`, `WaitAndClick`).
- [ ] Wire `AppiumSetup.cs` to expose the shared `Driver` to `UiDebugger` without duplicating session setup.
- [ ] Verify helper against one known-good element and one suspect element, confirming actionable output either way.

## Step 4 — Sidebar navigation tests + fixes
- [ ] Add `MostaqlK.UITests/SidebarNavigationTests.cs` covering all 5 nav rows from all 4 pages.
- [ ] Run suite, capture `UiDebugger` dumps for failing rows.
- [ ] Fix root cause in `AppSidebar.cs`/`AppSidebar.xaml`/page nav handlers until all pass.

## Step 5 — Projects page tests + fixes
- [ ] Add `MostaqlK.UITests/ProjectsPageTests.cs` (search+Enter, pause/resume, refresh, scroll, card tap→details).
- [ ] Diagnose non-responsive controls via `UiDebugger`.
- [ ] Apply minimal fix per failing control until all Projects-page tests pass.

## Step 6 — Settings / Project Details / About page tests + fixes
- [ ] Add `MostaqlK.UITests/SettingsPageTests.cs` (3 `AppEntry` inputs, `Picker`, `AppToggle`, Save).
- [ ] Add `MostaqlK.UITests/ProjectDetailsPageTests.cs` (back button, attachment download, scroll).
- [ ] Add `MostaqlK.UITests/AboutPageTests.cs` (footer link tap, roadmap scroll).
- [ ] Fix any remaining broken control found via `UiDebugger` until full catalog passes.

## Step 7 — Error-handling compliance checker + fixes
- [ ] Add `Core/ErrorOutcomeAttribute.cs` (`[ErrorOutcome(Handled|Ignored|Rethrown, Label)]`).
- [ ] Build `tools/ErrorHandlingAudit/Program.cs` (Roslyn console tool: bypassed factories, swallowed exceptions, unread `ExternalMessage`/`FixMessage`, missing `[ErrorCode]`/`[ErrorOutcome]`).
- [ ] Build `tools/ErrorHandlingAudit/Fixtures/*.cs` (compliant + violating snippets per rule).
- [ ] Tune checker against fixtures until zero false positives/negatives.
- [ ] Run checker against real codebase, generate `docs/error-handling-audit.md`, manually classify every flagged line, tune until zero known false positives/negatives.
- [ ] Fix every confirmed real violation.
- [ ] Update `UNITS.md` with `ErrorOutcomeAttribute`.
- [ ] Master performs final full-file review and ticks every remaining checkbox.

---
**Status as of this session:** Step 1's code changes (diagnostics utility + design-parity startup
fix) are implemented and compile cleanly. All remaining Step 1 verification items and Steps 2-7 are
**not yet done** — this is a large multi-day master/slave effort (Appium harness, per-page test
suites, a Roslyn-based audit tool) that could not be completed end-to-end in this session. Treat
unchecked boxes above as the authoritative remaining backlog for follow-up sessions/slaves.

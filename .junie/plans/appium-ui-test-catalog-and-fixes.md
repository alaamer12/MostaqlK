---
sessionId: session-260810-130243-1rzm
---

# Requirements

### Overview & Goals
The MVP's 6 pages (mirroring `.repertoire/design/mvp/*.html`: `projects.html`, `project-details.html`, `settings.html`, `about.html`, plus the notifications flyout) are visually complete but the running `.exe` is largely unnavigable/unclickable, and — critically — a lot of what looks like data/state is not actually backed by a working system. Investigation found a concrete root cause: `App.xaml.cs` persists a `design_parity_mode` preference (set by `DesignDataSeeder`/the `--seed-design-data` startup flag) that, once set, **permanently disables `IPollService`/`WorkerPool` startup** on every subsequent launch until someone explicitly passes `--seed-design-data=off` — so the shipped `.exe` can silently run forever against frozen, seeded fake data with the real scraping/worker pipeline never started. This explains "the db looks fake" and controls that "have state without even clicking" (e.g. `IsPollingActive` reflecting `_pollService.IsPaused` from a pipeline that was never started). The goal is now two-fold:
1. Produce one big markdown catalog (`docs/ui-test-catalog.md`) enumerating **every** interactive/dynamic surface across the 4 mockups' corresponding XAML pages, tagged by interaction kind (dynamic-data, clickable, inputtable, pannable/scrollable, animated, draggable, keyboard/aria, focusable), **plus** the backend/service each dynamic surface is supposed to be wired to (DB repository, `IPollService`, `WorkerPool`, `AssetDownloadService`, etc.).
2. Give every one of those elements a stable `AutomationId`, and add lightweight diagnostic instrumentation (a `[TraceInteraction]`-style attribute/logging helper) on the commands/services behind them, so both UI hit-testing *and* the real backend call underneath can be independently verified — not just "a click was registered".
3. Extend the existing `MostaqlK.UITests` Appium/WinAppDriver harness (`AppiumSetup.cs`) with element-dump/"duck debugging" helpers and one test class per page that exercises each catalog case end-to-end (UI action → backend effect → UI reflects the new state), including commonsense counterpart states the issue didn't explicitly name (e.g. testing "pause" must also test "resume"; testing "save settings" must also test that a reload reflects the saved value; testing "refresh" must also test that a second refresh doesn't double-fire).
4. Fix the root causes surfaced by those tests — starting with the `design_parity_mode` persistence trap, then any remaining fragile `Border+TapGestureRecognizer` hit-testing issues, then any backend/service wiring that turns out to be missing or short-circuited.
5. Build a static **error-handling compliance checker** (`tools/ErrorHandlingAudit/`) that walks every module's `Errors.cs`/`ErrorAttributes.cs` (per `.repertoire/.steering/base/tech/errors-handling.md`) and every call site, and answers three concrete questions per raise/throw site: (a) is the raised `DomainError`/exception actually caught and handled somewhere up the call chain (not silently swallowed), (b) when it reaches the UI/log boundary, is `ExternalMessage` (and `FixMessage`) actually surfaced (bound to `LabelWithSubText`/logged), or is it ignored/dropped, and (c) is the raise site annotated so its outcome/state is traceable (an `[ErrorCode]`/`[ErrorCategory]`/`[NeitherContract]` style attribute, or the new `[TraceInteraction]`) rather than a bare `throw`/`Result.Fail` with no module-registered code. The checker emits `docs/error-handling-audit.md`, a table of every violation found (unhandled, ignored `ExternalMessage`, unannotated) with file:line.

### Scope
**In scope:**
- All 4 MVP pages: `MainWindowPage.xaml` (projects.html), `ProjectDetailsPage.xaml` (project-details.html), `SettingsPanel.xaml` (settings.html), `AboutPage.xaml` (about.html), plus `AppSidebar` (shared nav, used on all 4) and `RecentNotificationsFlyout.xaml` (notifications flyout referenced from projects.html's sidebar).
- `AutomationId` additions on every catalogued interactive/data-bound element.
- Backend/service verification for every dynamic-data surface: `PollService`/`WorkerPool` actually running and progressing, `IProjectRepository`/SQLite actually persisting real (non-seeded) rows once `design_parity_mode` is off, `AssetDownloadService` actually downloading, Settings actually persisting to `Preferences` and being re-read on reload.
- A `[TraceInteraction]`-style diagnostic attribute/logging utility applied to the key commands (`TogglePolling`, `RefreshCommand`, `SaveCommand`, `SelectCommand`, `ResolveCommand`, nav events) so command entry/exit and any thrown exception are captured in a log the tests (and a human) can inspect — this is the "give you very details in order to capture all bugs" mechanism.
- Fixing the `design_parity_mode` persistence trap so the shipped app cannot get permanently stuck on frozen seed data.
- Commonsense counterpart states for every explicitly-mentioned control: pause ⇄ resume, save ⇄ reload-and-verify, refresh once ⇄ refresh-again (no double-fire/race), open ⇄ close (flyout), select ⇄ deselect where applicable.
- Appium/WinAppDriver test project (`MostaqlK.UITests`) additions: a debug-dump utility, and per-page test fixtures covering the catalog end-to-end (UI + backend assertions, not UI-only).
- Root-cause fixes for navigation/interactivity issues discovered while writing/running these tests (sidebar nav, refresh icon tap, settings save, project card tap, etc.).

**Out of scope:**
- `.repertoire/design/post-mvp/` mockups (v2/v3, per project rules — must be ignored).
- Non-Windows platforms (V1 is Windows-only).
- Adding new features not present in the mockups.

### User Stories
- As a QA engineer, I want a single markdown file listing every interactive element per page so I know exactly what to automate and what "done" looks like.
- As a developer, I want every button/row/input to expose a stable `AutomationId` so Appium (and any future test) can target it without brittle XPath/name lookups.
- As a user of the shipped `.exe`, I want to actually be able to click sidebar nav items, tap project cards, type in search/settings fields, toggle dark mode, and hit the refresh icon — none of which currently work reliably.

### Functional Requirements
- The catalog markdown must, for each `.html` mockup file, list every corresponding XAML element with: element name/AutomationId, interaction kind(s) from the 7 categories in the issue, source file:line, and the concrete Appium test method that will assert it.
- Every element in the catalog must carry a non-empty, unique-within-page `AutomationId`.
- Each Appium test must FAIL before the fix (if the underlying control is actually broken) and PASS after — this is the acceptance signal that navigation is restored.
- `AppiumSetup` must expose a reusable "dump UI tree" helper (page source / element inspector) that failing tests can call to log actionable diagnostics instead of a bare `NoSuchElementException`.

# Technical Design

### Current Implementation
- Zero `AutomationId` usage anywhere in the repo (`grep_search` for `AutomationId` in `*.xaml`/`*.cs` returns no matches).
- `MostaqlK.UITests/AppiumSetup.cs` already auto-starts `WinAppDriver.exe`, locates the built `MostaqlK.exe`, and opens a `WindowsDriver<WindowsElement>` session over `http://127.0.0.1:4723`. `AppLaunchTests.cs` only checks the session/window handle exists — no real interaction coverage.
- Interactive patterns found across the 4 pages + shared units:
  - `AppSidebar.cs` / `AppSidebar.xaml` (`UI/PlatformComponents/AppSidebar/`): 5 nav rows, each a tappable `Border` (`ProjectsButton`, etc.) wired via `TapGestureRecognizer` → C# events (`ProjectsClicked`, ...). No `AutomationId`.
  - `MainWindowPage.xaml`: `SearchInputField` (debounced `Entry`, inputtable + "aria" Enter via `DebouncedCommand`), pause/resume `Border` (`TogglePollingCommand`), gear `Border` (`OnGearTapped`), retry `AppButton` (`RefreshCommand`), `CollectionView` of `ProjectCard` (pannable/scrollable), footer "mark all read" `Label`+Tap, refresh `↻` `Label`+Tap (animated candidate), `NotificationsFlyout` toggle.
  - `ProjectCard.xaml`: whole-card `VerticalStackLayout` + `TapGestureRecognizer` → `SelectCommand` (dynamic data everywhere: title, description, skills, client, stats, unread dot).
  - `SettingsPanel.xaml`: 3 numeric `AppEntry` inputs, a `Picker` (grouping mode), an `AppToggle` (dark mode), a `Button` (`SaveCommand`), validation `Label`.
  - `ProjectDetailsPage.xaml`: back `Button` (`OnProjectsNavClicked`), `CollectionView` of attachments each with its own `AppButton` (`ResolveCommand`) — per-row dynamic download state.
  - `AboutPage.xaml`: footer `Label`+Tap (`OnMostaqlLinkTapped`), scrollable facts/roadmap lists (static, low priority).
  - `RecentNotificationsFlyout.xaml`: `CollectionView` rows, each a `VerticalStackLayout`+Tap → `OpenProjectCommand`.
  - `ShimmerBox` (`UI/DesignSystem/`) is the one confirmed *animated* unit (sweeping shimmer during `IsLoading`).
- `UNITS.md` documents known Windows-unpackaged-build gotchas (font/icon loading silently failing despite "correct" code) — the same class of bug (control renders but doesn't behave) is the leading suspect for the reported click/nav failures, so tests must capture concrete evidence (dumped tree + exception) rather than guessing.
- The live SQLite store lives on disk at `C:\Users\<user>\AppData\Local\User Name\com.companyname.mostaqlk\Data` (MAUI's `FileSystem.AppDataDirectory` for this app id) — this is the actual file the fix must inspect/reset, not a guess: before any code change, the plan inspects this file directly (and the `design_parity_mode`/`settings_*` entries in the app's `Preferences` store) to confirm whether the currently-installed build is stuck in seeded design-parity data, and clears/reseeds it as part of verifying the fix.

### Key Decisions
- **AutomationId placement**: set `AutomationId` directly in XAML (`AutomationId="Sidebar_ProjectsButton"` style) on the exact `Border`/`Button`/`Entry`/`CollectionView`/`Label` that currently owns the `TapGestureRecognizer`/`Command`/`GestureRecognizers`, not on a wrapping container — so `WindowsDriver.FindElementByAccessibilityId` maps 1:1 to the actual hit-test target. Naming convention: `<Page>_<Element>` (e.g. `Projects_SearchInput`, `Sidebar_SettingsButton`, `Settings_SaveButton`).
- **Catalog file location**: `docs/ui-test-catalog.md` (new), one `###` section per mockup/page, a markdown table per section with columns `Element | AutomationId | Interaction Kind(s) | Source | Appium Test`.
- **Appium debugging helper**: add `MostaqlK.UITests/Utils/UiDebugger.cs` with `DumpPageSource(WindowsDriver<WindowsElement> driver, string label)` (writes `driver.PageSource` to `TestContext` + a timestamped file under `TestContext.CurrentContext.WorkDirectory`) and `WaitAndClick`/`WaitAndFind` wrappers with retry + on-failure dump, so every test failure is self-documenting ("duck debugging").
- **Fix strategy for broken taps**: where a test proves a `Border+TapGestureRecognizer` row is not reachable/clickable via UI Automation (WinAppDriver walks the UIA tree, and a plain `Border` may not expose itself as invokable), convert that element to `AppButton`-based or wrap with `InputTransparent="False"` + explicit `SemanticProperties`/`AutomationId` plus verifying `IsEnabled`/hit-test size — decision made per-failure, driven by actual WinAppDriver output, not speculative.
- **Error-handling audit mechanism**: implement the checker as a small Roslyn-based analyzer/script (`tools/ErrorHandlingAudit/Program.cs`, a standalone console tool run via `dotnet run`, not a shipped app dependency) rather than hand-grepping, because "is this exception actually caught" and "is `ExternalMessage` actually read anywhere" both require walking the syntax tree (catch blocks, `switch` on `Result<T>.Err`, property-read call sites), not just regexing for the word `ExternalMessage`. Each module's existing `[ErrorCode]`/`[ErrorCategory]`/`[NeitherContract]` attributes (already defined per `errors-handling.md §6`) become the checker's ground truth for "this raise site is registered"; any `DomainError`/`throw` construction that bypasses a module's `Errors.cs` factory (already forbidden per `errors-handling.md §1.1`) is flagged as a violation, not a new rule.
- **Per-call outcome/state tagging**: rather than inventing a parallel new attribute system, extend the existing per-module `Errors.cs` convention with one new small attribute, `[ErrorOutcome(ErrorOutcome.Handled|Ignored|Rethrown, Label = "...")]`, applied at the call site (catch block or `Result.Err` arm) — this is the "set(Label).ToState(dbError)" mechanism the issue asked for, scoped per module (e.g. `DB-003` outcome tagged in `Infrastructure.Database`, `HTTP-002` outcome tagged in `Infrastructure.Http`) instead of one global mechanism, matching the existing per-module `Errors.cs` boundary. The checker cross-references `[ErrorOutcome]` tags against actual control flow to catch mismatches (e.g. tagged `Handled` but the catch block is empty).

### Proposed Changes
1. Fix the `design_parity_mode` persistence trap in `App.xaml.cs`/`DesignDataSeeder.cs`: `ApplyDesignDataArgument` currently returns the *persisted* preference whenever no `--seed-design-data` argument is passed, which means once seeded it silently stays seeded (and the pipeline stays off) forever. Add a `[TraceInteraction]`/log line here so this state is visible at startup, and make sure a normal launch (no flag) can never permanently strand the app in design-parity mode with the pipeline off — confirm/decide the exact fix with the user in a follow-up if the intended behavior is ambiguous.
2. Add `MostaqlK.UITests/Utils/UiDebugger.cs` and a small `Services/Diagnostics/TraceInteractionAttribute.cs` (+ a tiny interceptor/manual logging helper called from each traced command) that logs command name, timestamp, params, and exceptions to a rolling file under `FileSystem.AppDataDirectory` — this is the "give you very details to capture all bugs" mechanism, usable both by Appium tests and manually.
3. Apply `[TraceInteraction]`/the logging helper to `TogglePolling`, `RefreshCommand`, `SaveCommand` (Settings), `SelectCommand` (ProjectCard), `ResolveCommand` (attachments), and the sidebar nav handlers.
4. Add `AutomationId` to every catalogued element across `AppSidebar.xaml`, `MainWindowPage.xaml`, `ProjectCard.xaml`, `SettingsPanel.xaml`, `ProjectDetailsPage.xaml`, `AboutPage.xaml`, `RecentNotificationsFlyout.xaml`.
5. Write `docs/ui-test-catalog.md` cross-referencing every added `AutomationId` to its interaction kind(s), the backend it's wired to, and its Appium test.
6. Add one Appium test class per page (`ProjectsPageTests.cs`, `ProjectDetailsPageTests.cs`, `SettingsPageTests.cs`, `AboutPageTests.cs`, `SidebarNavigationTests.cs`) covering the catalog's cases for that page, each asserting both the UI change *and* the backend effect (DB row, log entry, persisted preference), plus the commonsense counterpart states (pause↔resume, save↔reload, refresh↔refresh-again).
7. Run the suite, capture concrete WinAppDriver + `TraceInteraction` log failures, and fix the underlying XAML/code-behind/service bug per failure until the full catalog passes.
8. Build `tools/ErrorHandlingAudit/` (standalone Roslyn console tool) that scans every `Errors.cs`/module for: raise sites bypassing the module's factory, caught-but-swallowed exceptions (empty/log-only catch with no `ExternalMessage` propagation), `Result<T>.Err` arms whose `ExternalMessage`/`FixMessage` is never read by any UI binding or `InteractionLogger` call, and raise sites missing `[ErrorCode]`/`[ErrorOutcome]` tagging. Emit `docs/error-handling-audit.md` and fix every flagged violation (add the missing `[ErrorOutcome]` tag, wire the dropped `ExternalMessage` to `LabelWithSubText`/`ValidationMessage`, or route a swallowed exception through the module's `Errors.cs` factory).

### File Structure
```
docs/ui-test-catalog.md                                  (new)
MostaqlK.UITests/Utils/UiDebugger.cs                      (new)
MostaqlK.UITests/ProjectsPageTests.cs                     (new)
MostaqlK.UITests/ProjectDetailsPageTests.cs               (new)
MostaqlK.UITests/SettingsPageTests.cs                     (new)
MostaqlK.UITests/AboutPageTests.cs                        (new)
MostaqlK.UITests/SidebarNavigationTests.cs                (new)
Services/Diagnostics/TraceInteractionAttribute.cs         (new)
Services/Diagnostics/InteractionLogger.cs                 (new)
Core/ErrorOutcomeAttribute.cs                             (new — extends existing ErrorAttributes.cs convention)
tools/ErrorHandlingAudit/ErrorHandlingAudit.csproj         (new)
tools/ErrorHandlingAudit/Program.cs                        (new)
tools/ErrorHandlingAudit/Fixtures/*.cs                     (new — compliant/violating regression fixtures)
docs/error-handling-audit.md                              (new — generated + fixed)
App.xaml.cs                                               (design_parity_mode trap fixed)
Infrastructure/Database/DesignDataSeeder.cs               (logging added)
UI/PlatformComponents/AppSidebar/AppSidebar.xaml          (AutomationId added)
Features/Projects/Views/MainWindowPage.xaml               (AutomationId added)
Features/Projects/ViewModels/ProjectFeedViewModel.cs       (TraceInteraction added)
Features/Projects/Views/ProjectCard.xaml                  (AutomationId added)
Features/Projects/Views/ProjectDetailsPage.xaml            (AutomationId added)
Features/Projects/Views/AboutPage.xaml                     (AutomationId added)
Features/Settings/Views/SettingsPanel.xaml                 (AutomationId added)
Features/Notifications/Views/RecentNotificationsFlyout.xaml (AutomationId added)
UNITS.md                                                    (AutomationId + TraceInteraction conventions documented)
```

### Risks
- WinAppDriver requires Developer Mode + the WinAppDriver service installed on the machine running tests; if unavailable, tests must degrade to a clear skipped/explained failure rather than hanging.
- Some "broken interactivity" may stem from app-level exceptions (DI, DB) rather than UI hit-testing; the catalog/tests will surface this via `UiDebugger`/`TraceInteraction` logs even if the root cause turns out to be non-UI.
- Changing a `Border+Tap` row to a different control shape must preserve the exact mockup visuals (padding/border/colors) already coded to match `.repertoire/design/mvp/*.html`.
- If the on-disk store at `...\com.companyname.mostaqlk\Data` is already stuck in seeded design-parity data on the target machine, tests need an explicit reset step (delete/`--seed-design-data=off`) before asserting "real" backend behavior, or they will mistake stale seeded rows for live data.
- A pure-regex/text scan cannot reliably prove "this exception is actually handled" (that requires real control-flow/data-flow analysis); the audit tool must use Roslyn's syntax/semantic model, not string matching, or it will produce false negatives/positives that undermine trust in the audit.

# Testing

### Validation Approach
Each Appium test targets a real built `.exe` via WinAppDriver, so "passing" means the actual interaction works end-to-end in the shipped app, not just that a command exists in the ViewModel.

### Key Scenarios
- Startup: launch the built `.exe` fresh, confirm via the `InteractionLogger` log (and the on-disk `Data` store) that `design_parity_mode` is off and `PollService`/`WorkerPool` actually started.
- Sidebar: click each of the 5 nav rows from every page and assert the destination page's known `AutomationId` becomes present (dynamic navigation).
- Projects page: type in `Projects_SearchInput`, press Enter, assert filtered `CollectionView` count changes (input + aria/Enter); tap pause pill and assert `IsPollingActive`/`PollService.IsPaused` actually flips (not just the label), then tap it again (resume) and assert polling resumes; tap the `↻` refresh element and assert `LastScanText` updates and a real poll cycle ran (clickable + animated feedback + backend); scroll the project `CollectionView` (pannable); tap a `ProjectCard` and assert navigation to details (clickable + dynamic data).
- Settings page: type into each `AppEntry`, tab/focus between them (focusable), change the `Picker`, toggle `AppToggle`, click Save, assert `ValidationMessage`/persisted value (inputtable + clickable).
- Project details: click an attachment's download `AppButton` and assert `StatusMessage` changes (clickable + dynamic data); scroll the details `ScrollView` (pannable).
- About page: tap the Mostaql footer link (clickable); scroll roadmap list (pannable).
- Notifications flyout: open from sidebar, tap a notification row, assert navigation (clickable + dynamic data).

### Edge Cases
- Empty project list (`IsEmpty` state) — sidebar/search still interactive.
- Error state (`HasError`) — retry button must be clickable and AutomationId-addressable.
- Rapid double-clicks on the refresh/pause controls should not throw or freeze the UI thread.

### Test Changes
- New Appium test files listed in File Structure; existing `AppLaunchTests.cs` stays as the smoke test and is not modified beyond ensuring it still passes.
- `tools/ErrorHandlingAudit/` is fine-tuned iteratively: for every run, the master (or the owning slave) manually classifies every single flagged line as true-positive or false-positive, and manually samples known-compliant call sites and known-violating call sites (planted or found) to check for false negatives, adjusting the Roslyn rules each round until a full pass over the codebase yields **zero known false positives and zero known false negatives** before the audit's fixes are trusted and applied.
- A small fixture set of intentionally-compliant and intentionally-violating C# snippets (`tools/ErrorHandlingAudit/Fixtures/`) is maintained specifically to regression-test the checker itself across fine-tuning rounds, so a rule change that fixes one false positive cannot silently reintroduce a false negative elsewhere.
- `tools/` conventions are followed: the checker's supporting scripts (if any Python helper is needed, e.g. to batch-render/summarize the audit report) live under `tools/`; `tools/snip_tool.py` (existing visual-capture utility) is reused, not duplicated, if any step needs a screenshot/visual artifact for a UI-adjacent error surface (e.g. capturing the actual `ValidationMessage`/`LabelWithSubText` rendering a previously-dropped `ExternalMessage`).

# Orchestration Methodology

### Master/Slave Execution Model
Execution follows a **master/slave** model, not a single linear pass:
- **Master** (the orchestrating agent) owns `docs/master-plan-checklist.md` — a single markdown file with a `- [ ]` checkbox line per concrete task derived from every bullet in every Delivery Step below (nothing summarized away; every fix, every test file, every `AutomationId`, every audit item gets its own line, grouped by the Delivery Step it belongs to).
- The master is the **only** one allowed to tick a box `- [x]`, and only after independently reviewing the slave's report and re-verifying the claimed fix (re-running the relevant Appium test / re-reading the diff / re-running the audit tool) — a slave's self-report alone never flips a box.
- **Slaves** are subagents dispatched per checklist section; each slave must be honest in its report: explicitly state what passed, what still fails, and what it could not verify, rather than claiming success to close the loop.
Work is not considered done until the master has ticked every checkbox in `docs/master-plan-checklist.md` and completed a final full-file review pass.

### Shared Slave Utility (built first, used by all slaves)
Before any slave starts fixing anything, one dedicated slave builds the shared diagnostics package so every other slave instruments consistently instead of inventing ad-hoc logging:
- `Services/Diagnostics/InteractionLogger.cs` — structured logging sink (already planned) extended with a simple **A/B marker** helper: `InteractionLogger.Mark(string checkpoint, string variant, object? data = null)`, so a slave can bracket a suspect code path with `Mark("TogglePolling.enter", "A")` / `Mark("TogglePolling.exit", "B")` and diff the resulting log to prove whether a given branch actually executed.
- `Services/Diagnostics/TraceInteractionAttribute.cs` — the tracing attribute (already planned), reused by every slave instead of each slave rolling its own.
- This utility slave's checklist section is the **first** section any other slave depends on; no other slave starts instrumentation until this lands, to avoid duplicate/conflicting logging helpers.

### Non-Overlap Rule
Each dispatched slave is scoped to exactly one Delivery Step (or one clearly-bounded sub-area within a step, e.g. "Sidebar" vs "Projects page") and touches only the files listed under that step's checklist section — file ownership is assigned up front from the File Structure table so two slaves never edit the same file in the same pass. If a fix requires touching a file outside a slave's assigned section, that slave reports it back to the master instead of editing it, and the master reassigns.

### Escalation Rule
Slaves and the master proceed autonomously through all normal failures (test failures, missing `AutomationId`s, audit violations, ambiguous XAML behavior) using the diagnostic tooling above to resolve them without external input. The user is only asked a direct question when a slave/master hits an **ultimate-critical, externally-blocked** case — e.g. WinAppDriver/Developer Mode cannot be enabled on the machine, a required credential/service is unavailable, or a fix would require a decision that changes shipped user-facing behavior in a way the docs don't resolve. These are the only cases surfaced upward; everything else is resolved and reported, not asked about.

# Delivery Steps

### ✓ Step 1: Write the master checklist, build the shared slave diagnostics utility, and fix the design-parity/backend startup trap
`docs/master-plan-checklist.md` exists with one checkbox per concrete task from every step below, the shared logging/tracing/A-B utility is ready, and a fresh launch of the built `.exe` runs the real `PollService`/`WorkerPool` against real data.
- As master, write `docs/master-plan-checklist.md`: one `- [ ]` line per bullet across Steps 1-7, grouped under a heading per step, with the assigned slave name and owned file list per group so no two slaves overlap.
- Dispatch the diagnostics-utility slave to add `Services/Diagnostics/TraceInteractionAttribute.cs` and `Services/Diagnostics/InteractionLogger.cs` (including the `InteractionLogger.Mark(checkpoint, variant, data)` A/B helper), writing timestamped entries to a file under `FileSystem.AppDataDirectory`; master verifies it compiles/logs correctly before ticking.
- Dispatch a second slave scoped to `App.xaml.cs`/`Infrastructure/Database/DesignDataSeeder.cs` only, using the new logging utility to inspect the on-disk store at `C:\Users\<user>\AppData\Local\User Name\com.companyname.mostaqlk\Data` and `Preferences` (`design_parity_mode`, `settings_*`), fix `ApplyDesignDataArgument` so a normal launch can never get permanently stuck with the pipeline disabled, and report honestly (fixed / still broken / unverifiable).
- Master re-runs a fresh launch independently, confirms the log shows the real pipeline starting, and only then ticks the corresponding checklist items.
- Update `UNITS.md` with the new `TraceInteraction`/`InteractionLogger`/`Mark` diagnostic mechanism.

### ✓ Step 2: Dispatch a slave to build the UI interaction catalog and add AutomationIds
docs/ui-test-catalog.md exists and every interactive/dynamic element across the 4 MVP pages has a stable AutomationId in XAML, cross-referenced to its backend.
- Walk `.repertoire/design/mvp/{projects,project-details,settings,about}.html` against their corresponding XAML views and enumerate every element that is dynamic-data-bound, clickable, inputtable, pannable, animated, draggable, keyboard/Enter-triggered, or focusable, including commonsense counterpart states (pause/resume, save/reload, open/close) not explicitly named in the mockups.
- Write `docs/ui-test-catalog.md` with one section per page and a table of Element | AutomationId | Interaction Kind(s) | Backend it calls | Source file:line | planned test name.
- Add `AutomationId` to every catalogued element in `AppSidebar.xaml`, `MainWindowPage.xaml`, `ProjectCard.xaml`, `SettingsPanel.xaml`, `ProjectDetailsPage.xaml`, `AboutPage.xaml`, and `RecentNotificationsFlyout.xaml`, following the `<Page>_<Element>` naming convention.
- Apply `[TraceInteraction]`/the logging helper from Step 1 to `TogglePolling`, `RefreshCommand`, `SaveCommand`, `SelectCommand`, `ResolveCommand`, and the sidebar nav handlers.
- Update `UNITS.md` with the AutomationId naming convention; slave reports completion, master reviews the catalog and diffs before ticking the checklist.

### ✓ Step 3: Dispatch a slave to extend the Appium/WinAppDriver harness with debug tooling
MostaqlK.UITests can dump the live UI Automation tree and retry-with-diagnostics on any failed find/click.
- Add `MostaqlK.UITests/Utils/UiDebugger.cs` with `DumpPageSource`, `WaitAndFind`, and `WaitAndClick` helpers that log `driver.PageSource` and element state to `TestContext`/a file on failure.
- Wire `AppiumSetup.cs` to expose the shared `Driver` to the new helper class without duplicating session setup.
- Verify the helper works against the current build by pointing it at one known-good element (e.g. the app window) and one currently-suspect element (e.g. a sidebar row), confirming it produces actionable output either way.

### ✓ Step 4: Dispatch a slave to write and run sidebar navigation tests, fix broken navigation
SidebarNavigationTests.cs covers all 5 nav rows from all 4 pages, and sidebar navigation actually works in the built .exe.
- Add `MostaqlK.UITests/SidebarNavigationTests.cs` clicking each `AppSidebar` row (`Sidebar_ProjectsButton`, `Sidebar_AdvancedSearchButton`, `Sidebar_NotificationsButton`, `Sidebar_SettingsButton`, `Sidebar_AboutButton`) from each page and asserting the target page's marker `AutomationId` appears.
- Run the suite against the current build, capture `UiDebugger` dumps for any row that fails to navigate.
- Fix the root cause in `AppSidebar.cs`/`AppSidebar.xaml` (or the page-level `On*NavClicked` handlers) for each failing row until all navigation tests pass.

### ✓ Step 5: Dispatch a slave to write and run Projects page interaction tests, fix broken controls
ProjectsPageTests.cs exercises search input, pause/resume, refresh, and card tap/scroll, and each fixed control works in the .exe.
- Add `MostaqlK.UITests/ProjectsPageTests.cs` covering: typing + Enter in `Projects_SearchInput`, tapping the pause/resume pill, tapping the refresh `↻` element, scrolling the `CollectionView`, and tapping a `ProjectCard` to navigate to details.
- Run against the build, use `UiDebugger` dumps to diagnose any control that doesn't respond (e.g. `Border+TapGestureRecognizer` not exposing as invokable to UI Automation).
- Apply the minimal fix per failing control (e.g. swap to an `AppButton`-based hit target, adjust `InputTransparent`/hit-test size) until all Projects-page tests pass.

### ✓ Step 6: Dispatch parallel slaves to write and run Settings, Project Details, and About page tests, fix remaining controls (each slave owns one page's files only)
Remaining three pages have full Appium coverage and their inputs/buttons/scroll views work correctly.
- Add `MostaqlK.UITests/SettingsPageTests.cs` covering the three `AppEntry` inputs, the `Picker`, the `AppToggle`, and the Save `Button`.
- Add `MostaqlK.UITests/ProjectDetailsPageTests.cs` covering the back button, an attachment's download `AppButton`, and scrolling.
- Add `MostaqlK.UITests/AboutPageTests.cs` covering the footer link tap and roadmap-list scrolling.
- Diagnose and fix any remaining broken control found via `UiDebugger` output, iterating until the full `docs/ui-test-catalog.md` set of tests passes.

### ✓ Step 7: Dispatch a slave to build the error-handling compliance checker, fix every flagged violation, and master closes out the checklist
tools/ErrorHandlingAudit exists, docs/error-handling-audit.md lists every non-compliant error path, and every listed violation is fixed.
- Add `Core/ErrorOutcomeAttribute.cs` extending the existing `Core/ErrorAttributes.cs` (`[ErrorCode]`/`[ErrorCategory]`/`[NeitherContract]`) convention with `[ErrorOutcome(ErrorOutcome.Handled|Ignored|Rethrown, Label)]`, applied at call sites that catch/consume a `DomainError`.
- Build `tools/ErrorHandlingAudit/Program.cs` as a Roslyn-based console tool that loads the solution, walks each module's `Errors.cs` factories, and flags: raise sites bypassing the module factory, caught-and-swallowed exceptions with no `ExternalMessage` propagation, `Result<T>.Err` arms whose `ExternalMessage`/`FixMessage` is never read by any binding/logger, and raise/catch sites missing `[ErrorCode]`/`[ErrorOutcome]` tagging.
- Build `tools/ErrorHandlingAudit/Fixtures/` with intentionally-compliant and intentionally-violating C# snippets covering every rule (bypassed factory, swallowed exception, dropped `ExternalMessage`, missing `[ErrorCode]`/`[ErrorOutcome]`) to regression-test the checker itself.
- Run the tool against the fixtures first; for every fixture that is misclassified, tune the Roslyn rule and re-run until the fixture suite reports zero false positives and zero false negatives.
- Run the tool against the real codebase, generate `docs/error-handling-audit.md`, and manually classify every flagged line as true/false positive, plus manually sample known-compliant call sites to check for false negatives; repeat tuning and re-running until a full pass yields zero known false positives and zero known false negatives.
- Fix each confirmed real violation: add missing `[ErrorOutcome]` tags, wire dropped `ExternalMessage`/`FixMessage` values into `LabelWithSubText`/`ValidationMessage` bindings or the `InteractionLogger`, and route any exception that bypassed a module's `Errors.cs` factory through it instead — reusing `tools/snip_tool.py` to capture a visual before/after of any UI surface that now shows a previously-dropped message, if useful for the master's review.
- Re-run the checker until `docs/error-handling-audit.md` reports zero unresolved violations, and update `UNITS.md` with the `ErrorOutcomeAttribute` addition.
- Master performs a final full-file review of `docs/master-plan-checklist.md`, independently re-verifying each slave's claimed fix, and only then ticks every remaining checkbox — work is not complete until every box is checked and reviewed.

### ✓ Step 8: Dispatch a slave to verify search/filter data-sync consistency and DB-backed dynamic counts
Filtering (search box, unread/notification counts, "X new projects today" stat, live scan-status text) actually reflects the real underlying DB/pipeline state end-to-end, not a frozen/mocked snapshot.
- Add Appium coverage (extend `ProjectsPageTests.cs` or add `DataSyncTests.cs`) asserting: typing a search term updates the visible `CollectionView` count to match a direct `IProjectRepository` query for the same term (UI count == DB count, not just "changed"); the sidebar's unread/notification badge count matches the DB's actual unread row count; the "مشاريع مضافة اليوم"/today stat card matches a DB query for today's rows; the live scan-status text (e.g. "يتم الفحص كل 30 ثانية"/requests-per-minute shown in the mockups) reflects `IPollService`'s actual configured interval/rate, not a hardcoded string.
- Cross-reference against the two attached screenshots (`a.PNG`, `z.PNG`) showing a live "مباشر"/scan-interval indicator and a project card's read/unread + stats row — these are the concrete UI surfaces that must be proven DB/pipeline-backed, not just visually present.
- Fix any surface found to be reading a stale/cached/hardcoded value instead of the live DB/service state.

### ✓ Step 9: Dispatch a DB-engineer slave to verify the database layer is genuinely functional and free of leftover fake/seeded data
A dedicated `scratch/`-based DB smoke test proves `IProjectRepository`/SQLite reads and writes real rows end-to-end, and the production store is confirmed free of stale `DesignDataSeeder` rows.
- Write a throwaway `scratch/db_smoke_test` (console script or xunit-style test, per repo convention) that opens the same SQLite file the app uses, inserts a uniquely-tagged row through the real repository layer (not raw SQL) that the app should read via `IProjectRepository`, and confirms the app-facing query returns it — proving the DB service is not a mock/no-op.
- Inspect the actual production store at `C:\Users\<user>\AppData\Local\User Name\com.companyname.mostaqlk\Data` (path confirmed by the user) via `sqlite3` CLI: check for rows whose shape matches `DesignDataSeeder`'s known seed dataset (fixed IDs/titles/timestamps) still present after real polling should have superseded them, and check the `Preferences`/`design_parity_mode` flag's current on-disk value.
- If fake/seeded rows are still present in what should be a live store, clear them (respecting the `--seed-design-data=off` path already fixed in Step 1) and document the finding.
- Delete the scratch test file when done per repo cleanup policy; report findings (DB genuinely functional: yes/no, fake data found: yes/no + evidence) to the master checklist.

### Step 10 (Master-only): Full-repo final review — all test paths, clean build, no warnings, real (non-mocked) production code
The master personally re-verifies the entire body of work end-to-end before any remaining checkbox is ticked — this step is never delegated to a slave.
- Re-run `dotnet build MostaqlK.csproj -c Debug -f net10.0-windows10.0.19041.0` AND the Release configuration, confirming zero errors and zero warnings (not just "build succeeded") across the whole solution, including `MostaqlK.UITests` and any `tools/` projects added.
- Enumerate every test file added across Steps 3-9 (`MostaqlK.UITests/*.cs`) and confirm each one actually ran against the real built `.exe` at least once in this session (not merely compiled) — re-run the full Appium suite end-to-end and record the final pass/fail tally.
- Use `git diff`/`git status` to review every production file touched across all steps (`App.xaml.cs`, all XAML views, view-models, `Services/Diagnostics/*`, `Infrastructure/Database/*`, etc.) line-by-line, confirming each change is genuinely wired to real backend logic (DB/`IPollService`/`WorkerPool`/`Preferences`) and not a stub, hardcoded value, or leftover mock/seed shortcut.
- Cross-check `docs/master-plan-checklist.md` against this review: only the master ticks a box, and only for items independently re-verified here — unresolved or unverifiable items must stay unchecked with an explicit note of why.
- Produce a final short findings note appended to `docs/master-plan-checklist.md` summarizing overall completion status honestly (including anything still not done).
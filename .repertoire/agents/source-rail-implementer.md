# source-rail-implementer — W1 (v3 shell: 64px SourceRail replaces AppSidebar on the projects page)

## Goal
Replace the 256px labelled `AppSidebar` shell on `MainWindowWindowsLayout` with the v3 mockups'
64px icon-only `SourceRail` (`.repertoire/design/postmvp/v3-multisource/projects.html`
`aside.rail`), matching the mockup's width/tile/indicator/cluster specs, routing all source
switching through `SourceMorph.Apply`, using one shared rail-width token, preserving asserted
AutomationIds, adding zero animations. Skill used: `impeccable` (Operate mode, mockup = pinned
brief; craft-floor honored — no invented visuals).

## Actions
1. **New unit `UI/PlatformComponents/SourceRail/`** (all created this wave):
   - `SourceRail.xaml(.cs)` — 64px rail, `FlowDirection="LeftToRight"` (contract §11.3 approved
     physical-left LTR wrapper; page content stays RTL). Top→bottom exactly per mockup: 40px brand
     mark (`DynamicResource Brand`/`OnBrand`) · separator · source tiles · separator · section
     buttons (Projects / AdvancedSearch / Settings / About — icon-only 44px rail-btns, Arabic
     labels kept via `ToolTipProperties.Text` + `SemanticProperties.Description`) · contributed
     pages · star spacer · bell (unread pill) · theme toggle (moon button).
     Active section = mockup treatment: `DynamicResource BrandSoft` wash + 3×22 `DynamicResource
     Brand` inline-start bar flush to the rail edge; raster icons take the pre-baked `_active`
     variant through the AppSidebar-proven `LinkLight` match. `ActivePage` (`RailPage` enum) +
     `UnreadCount` bindables; events Projects/AdvancedSearch/Settings/About/NotificationsClicked +
     SourceChanged. Theme toggle = behavior-identical relocation of the sidebar's dark-mode row
     (`SettingsKeys.IsDarkMode` persist → `UserAppTheme`, same trace labels). Event-lifetime rule:
     `RequestedThemeChanged` + `BrandPalette.PaletteChanged` subscribed Loaded → released Unloaded.
     Zero animations; no reveal (deferred wave).
   - `RailWidths.cs` — the single rail-width token (64 / 44 / 40 / 3×22) consumed by the rail
     (`x:Static` in XAML + code cells) and by `MainWindowWindowsLayout.OnRootSizeChanged`'s
     splitter clamp (old `const double sidebarWidth = 256` deleted).
   - `SourceSwitchTiles.cs` — **moved from `AppSidebar/`** and extended (switching logic kept
     intact, `SourceMorph.Apply` triple untouched): new `IconOnly` bindable renders the mockup's
     rail form (vertical 44px glyph squares, indicator at rail edge, tooltip "مستقل — mostaql.com"
     composed from descriptor name+domain, inactive 45% opacity). Default form = the legacy chip,
     still used by `AppSidebar`'s nav column. Same AutomationIds (`Sidebar_SourceTile_{id}`).
   - `SourceRailContributionItem.cs` — **moved from `AppSidebar/`** (record + projection unchanged,
     namespace now `…PlatformComponents.SourceRail`). Rail renders `SourceContributionRegistry.PagesFor(active)`
     generically after the standard sections; rebuild on Loaded + on `SourceSwitchTiles.SourceChanged`;
     empty list = nothing rendered (headless growth: a dev-fake contribution appears with zero
     shared-code edits — same data path the B2 ParserTests already cover).
2. **`MainWindowWindowsLayout.xaml(.cs)`** — shell rewiring only:
   - `sidebar:AppSidebar` → `rail:SourceRail` (events + `SourceChanged` preserved;
     `UnreadCount="{Binding GlobalStatus.UnreadNotificationCount}"`; `ActivePage="Projects"`).
   - Column order re-mirrored to the mockup's visual L→R **rail | feed | splitter | panel**
     (columns `Auto,Auto,*,Auto` in the RTL grid): panel col0, splitter col1, feed col2, rail col3.
     This places the rail on the **physical LEFT** (owner-locked §3/Q1) and the pipeline panel on
     the physical right per mockup §5. `SplitterHandle.DragSign` flipped `-1` → `1` (panel moved to
     the handle's other side). Notifications flyout follows the feed column (col2).
   - Removed the sidebar's underlay `BoxView`; `OnRootSizeChanged` uses `RailWidths.Width`.
   - New `OnRailNotificationsClicked` reuses `SetNotificationsFlyoutVisible` toggle semantics.
3. **AppSidebar** — de-wired from MainWindow only; files kept (Settings/About/ProjectDetails
   Windows layouts still reference it — grep-verified; mobile layouts never used it). Its XAML/cs
   updated for the moved types (xmlns + using); no behavior change.
4. **UNITS.md** — added `SourceRail` + `RailWidths` rows; AppSidebar row marked superseded for the
   projects shell (still live on 3 pages); SourceSwitchTiles row relocated/rail form;
   MainWindowWindowsLayout row updated; event-lifetime paragraph now names `SourceRail`.

## Files touched
- Created: `UI/PlatformComponents/SourceRail/SourceRail.xaml`, `SourceRail.xaml.cs`,
  `RailWidths.cs`, `SourceSwitchTiles.cs` (moved), `SourceRailContributionItem.cs` (moved)
- Deleted: `UI/PlatformComponents/AppSidebar/SourceSwitchTiles.cs`,
  `UI/PlatformComponents/AppSidebar/SourceRailContributionItem.cs` (both moved into SourceRail/)
- Edited: `UI/PlatformComponents/AppSidebar/AppSidebar.xaml`, `AppSidebar.cs` (reference fixes only),
  `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml`, `.xaml.cs`, `UNITS.md`
- Boundary compliance: did NOT touch App.xaml.cs, DesignDataSeeder.cs, tools/, mockups,
  Core/Sources contracts, SourceMorph logic, Settings/Projects content layouts.

## Decisions
- **Rail visual truth = mockup CSS** (64/44/12-radius, 3px+22 bar at −10px gutter, .45 inactive
  opacity, tooltips 56px offset → MAUI native `ToolTipProperties`). MAUI cannot desaturate a solid
  fill; the mockup's `saturate(.4)` is expressed by the 45% opacity alone (documented deviation).
- **Theme toggle = mockup's 44px moon rail-btn**, not the old `AppToggle` row (mockup wins for
  visuals; behavior identical). Sun raster does not exist yet and §11.12 forbids runtime
  ImageSource swaps — the button keeps the moon glyph until pre-baked sun art ships (surfaced).
- **AutomationIds deliberately kept with the `Sidebar_` prefix** (no test churn): nav rows,
  `Sidebar_DarkModeToggle`, `Sidebar_SourceTile_*`, `Sidebar_Contribution_*`, and
  `Sidebar_NotificationsButton` (UITests asserted that id but the current AppSidebar no longer had
  the row — the rail **restores** it).
- **`Sidebar_StatValueLabel` migrated deliberately**: the "مشاريع مضافة اليوم" stat leaves the
  shell for the feed header per §5/D9 (today-counter is a W2+ feed-page element, out of this
  wave's boundary). It still exists inside AppSidebar on the Settings/About pages. Until the
  header wave ships: `SidebarPersistenceTests.SidebarStat_PersistsAcrossNavigation` (projects-page
  leg) and `DataSyncTests` stat XPath (`//*[@Name='مشاريع مضافة اليوم']` on the projects page)
  will fail — the parent must carry this for test retargeting.
- Contributed pages land after the 4 standard sections (registry `Order` respected), same position
  rule AppSidebar used.
- Bell + header bell coexist for now (removing the header bell belongs to the feed-header wave).

## Verification (exact gate results)
1. `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` → **Build succeeded. 0 Errors.**
   Full compile showed **2 warnings, both pre-existing CS8604** in unmodified
   `Infrastructure/Notifications/WinAppSdkVariation.Windows.cs(80,25)` (file untouched in the tree);
   my files contribute **zero** warnings. Delta: none. (One interim failure was MSB3026/3027 exe
   file-lock from the concurrent agent's running instance; retry succeeded.)
2. `dotnet build MostaqlK.csproj -f net10.0-android` → **Build succeeded. 0 Warning(s) 0 Error(s)**
   with `-p:JavaSdkDirectory="D:\Program Files\Java\jdk-17.0.12"`. NOTE: bare `-f net10.0-android`
   fails with **XA0030 (machine JDK issue, pre-existing)**: `JAVA_HOME` points at jdk-22, Android
   SDK 36.1 wants JDK 21 — no JDK 21 installed. Not caused by, and not masking, any code problem.
3. `dotnet run --project tools/ParserTests` → **TOTAL: 297 passed, 0 failed** (unaffected).
4. `dotnet run --project tools/DbMigrationTests` → **ALL DB MIGRATION CHECKS PASSED**.
5. Grep gates: no brand hex in any new/edited XAML (`#3B82F6` hit exists only in AppSidebar's
   pre-existing logo tile — untouched); no `source == "<id>"` branches in touched shared UI;
   no `#if` outside the two exempt files.
6. Launch smoke: `MostaqlK.exe --seed-design-data` → first instance crashed inside
   `DesignDataSeeder.ParseSeedMode` line 167 (`Substring` start-range, `App.xaml.cs:268` path) —
   **both files are owned by the concurrent agent and were mid-edit; not a rail defect** (stack is
   100% seed-arg parsing). The watchdog-relaunched instance (same freshly built exe) rendered the
   full shell: `AppShell.Ctor → MainWindowPage.Ctor`, window to-foreground, heartbeat steady, no
   unhandled exceptions after the rail realized.

## For the parent
- Commit as dev22 (I did not bump versions; no commits made).
- `Sidebar_StatValueLabel` is gone from the Projects page ONLY by design (§5/D9) — two UI tests
  need retargeting when the W2 today-counter header ships; everything else keeps its ids.
- Settings/About/Details pages still show the old 256px sidebar until their waves → mixed shell by
  plan; mobile unchanged (AppSidebar was never referenced by mobile layouts).
- Concurrent-agent issues observed: exe file-lock (resolved by retry), `--seed-design-data`
  substring crash in DesignDataSeeder.cs (their file — flag it to them).
- Deferred/surfaced: sun icon raster (§11.12), radial-reveal `SourceMorphTransition` (later wave),
  disabled-source gray tile state (§3) awaits an enable/disable settings surface (registry is the
  enabled set today).

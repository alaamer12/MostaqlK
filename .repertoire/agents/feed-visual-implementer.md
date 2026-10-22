# feed-visual-implementer

## Goal

Last page wave of the v3 UI: restyle the **Windows Projects feed** to the mockup design truth
`.repertoire/design/postmvp/v3-multisource/projects.html`. Write code only (no commit, no version
bump — parent commits dev31). Reuse existing units; keep the dev29 `SourceFeedStore` renderer
semantics sacred (display props/commands only, no re-coupling to DB awaits). Two harness-found
bugs fixed first (A state-leak, B dark modal surface).

## Skill used

`tray-inspection` / harness gate workflow was not applicable; this was direct implementation work.
Binding docs read: `base/structure.md`, `base/tech/README.md`, `v3/product/multi-source-requirements.md`,
`v3/animation-safety.md`, `UNITS.md`, and `projects.html` (markup + CSS) in full.

## Files changed (7, all in the owned set)

| File | Nature |
|------|--------|
| `Features/Projects/Views/Layouts/ProjectCardWindowsLayout.xaml` | Full rewrite: continuous row, band-by-band |
| `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml` | Header/feed/footer region restyle |
| `Features/Projects/ViewModels/ProjectCardViewModel.cs` | Additive display members only |
| `Features/Projects/ViewModels/ProjectFeedViewModel.cs` | Additive display props only |
| `Features/Projects/Views/MainWindowPage.xaml.cs` | Modal-leak open path made idempotent (A) |
| `Services/ParityLaunchFlags.cs` | Explicit allowlist `ShouldOpenFiltersModal` (A) |
| `UI/PlatformComponents/FilterModal/FilterModal.cs` | Theme re-resolution (B) |

`App.xaml.cs` was **not** touched — the leak was fully addressed in `ParityLaunchFlags` + the page
open path, so the "App.xaml.cs only for the leak (minimal diff)" allowance went unused.

## Band-by-band mapping (ProjectCardWindowsLayout)

Root is now a continuous `section-row` (`Grid` `4,*`, surface bg, bottom hairline), **not** an
`AppCard`. `AutomationId=ProjectCard_Root` + tap → `SelectCommand` preserved.

- **Unread edge / wash** — 4px inline-start bar (read → `line` `#E2E8F0`/`#334155`; unread →
  `{DynamicResource Brand}` overlay) + a `{DynamicResource BrandSoft}` wash layer visible only when
  unread (source+theme aware, re-tints on morph).
- **band1** — title `19`/TajawalBold ink-1 + enrichment pill (right). Pill now uses a soft
  **state-tinted** background (`EnrichmentBadgeSoftBackground` = fg hex at ~12% alpha, dark-safe)
  instead of the flat light-only fill; icon/text stay the state fg (`EnrichmentBadgeForeground`).
- **band2** — description `14` ink-2 (`#475569`/`#CBD5E1`), `MaxLines=2` TailTruncation.
- **band3** — skill chips `FlexLayout` wrap, `BrandSoft`/`BrandInk` `rounded-md px-3 py-1`, gated on
  the new `HasSkills` bool (was `SkillItems.Count`, which is not a valid bool binding).
- **band4 (border-top)** — 44px brand avatar + `OnBrand` initials, `ClientName`/`ClientMeta`,
  relative `PublishTimeText` + `Clock` glyph at far inline-end.
- **band5 (border-top)** — source-native fact minis: الميزانية (accent green `#2E9E6B`/`#4FBF8C`,
  LTR-isolated only when `BudgetIsForeignCurrency` i.e. contains `$`), مدة التسليم, عدد المقترحات,
  plus حالة المشروع when `HasStatusText` (presence-by-data, nafezly). Unread indicator at inline-end:
  8px brand dot + `BrandInk` "غير مقروء"; read → nothing.
- **RowState visuals** — `IsProvisional` → `ShimmerBox` skeleton mirroring band geometry;
  `IsStale` → content `Opacity 0.55` (DataTrigger); `IsBroken` → single-line red-ghost `Border` with
  `BrokenMessage` + `RetryCommand` `AppButton`.

### Removed from the card

`AppCard` wrapper, `NewRibbonBadge` (`ProjectCard_NewRibbon`), `EnrichmentShimmerOverlay`
(`ProjectCard_EnrichmentShimmer`), `SourceLinkButton` (`ProjectCard_OpenOnSourceButton`), the
`تاريخ النشر` mini, the `مدة التنفيذ` mini (see id migration below), and the 6-column stats grid.

## Header / feed / footer mapping (MainWindowWindowsLayout)

Root restructured to `RowDefinitions="Auto,*,Auto"` so the title band and footer span the panel+feed
columns (0..2) full-width like the mockup, while the rail stays col3 (rowspan 3):

- **Header (row0, full-width surface band, bottom hairline)** — CENTERED `المشاريع` title +
  cadence caption beneath rendered as **`PollIntervalText · RateLimitText`** ("يتم الفحص كل {n}
  ثانية · {rpm} طلب/دقيقة") so the `يتم الفحص كل` test string stays rendered and data-driven; the
  today stat card at top-right (`أُضيفت اليوم` over the big `GlobalStatus.ProjectsAddedTodayText`,
  now carrying `AutomationId=Sidebar_StatValueLabel`). NO search-in-titlebar, NO direct/pause/live/
  rate chips, NO bell (lives in the rail).
- **Feed column (row1, col2)** — search row inside the feed: full-width `SearchInputField`
  (`Placeholder={Binding SearchPlaceholder}` = "بحث متقدم في مشاريع {descriptor}…") + `الفلاتر`
  outline button at its left (`Projects_FiltersButton`, `HasFilterFacets`-gated). Then the
  CollectionView (`Projects_ProjectsCollectionView`) + loading/error/empty states preserved.
- **Footer (row2, full-width surface band, top hairline)** — inline-start cluster
  `Projects_TrackedCountLabel` (`FooterTrackedText` = "{n} مشروعًا من {source}") · dot ·
  `Projects_UnreadCountLabel` · `Projects_MarkAllReadLabel`; center `الاستعلام النشط: {q} · تعديل`
  (`تعديل` opens the FilterModal); inline-end `LastScanStatus` (kept `Projects_LastScanStatus`/
  `Projects_LastScanLabel`/`Projects_RefreshLabel`) + the **poll pill** outline button
  (`Projects_TogglePollingButton`, `TogglePollingCommand`, `{DynamicResource Brand/BrandSoft/BrandInk}`,
  paused → accent-green variant via plain-value DataTriggers, glyph/label from `PollToggleIcon`/
  `PollPillLabel`).
- **Pipeline panel** — reused unchanged (`PipelineDashboardPanel` is shell-wave-owned; already carries
  `خط المعالجة` + collapse toggle `Pipeline_PanelToggle`/`Pipeline_RailToggle` + radar + worker rows +
  backlog). Splitter + `OnRootSizeChanged` clamp + RailWidths untouched. The mockup's header source
  chip was NOT added (would require editing the shell-owned panel unit).
- **Removed** — the old bordered header card and its `PollIntervalText`/rate/live-status/pause/bell
  chip row; the separate active-query card; `Projects_ConnectionLabel` + the hard connection dots.

## Fix A — filter-modal state leak (root cause)

The open path keyed off parity state but only ever **opened** the modal, and it read
`Environment.GetCommandLineArgs()` which is the process-lifetime first-launch latch. Under the app's
single-instance redirect (`Platforms/Windows/Program.cs` `OnActivated` → `TryRestoreExistingWindow`),
a running `filters-open` primary handed a later `default` activation keeps the dialog open and never
re-evaluates — so a default capture can show the modal open. Fix: (1) `ParityLaunchFlags.ShouldOpenFiltersModal`
is an explicit allowlist (only exact `filters-open` → true; `default`/`panel-collapsed`/null → false);
(2) `MainWindowPage.ApplyParityFiltersState()` is idempotent — opens only when requested, otherwise
**explicitly closes** a visible modal. Every fresh launch (and every redirect re-entry) now forces the
default state closed.

## Fix B — FilterModal white surface in dark mode (root cause)

The modal chrome (card bg/stroke, title, note, close icon, section dividers, clear/cancel buttons) is
built once in the constructor, resolving the neutral `Surface()/Line()/Ink()` slate values via a static
`IsDarkTheme` read at construction time — which happens during window inflation, **before** the startup
theme is applied. `Open()` rebuilt only the facet tree (chips/entries, which read theme live), leaving
the frozen white chrome. Fix: persistent chrome pieces are stored in fields and re-resolved by
`ApplyTheme()`, called at construction, on every `Open()` (covers construct-before-theme), and on a live
`Application.RequestedThemeChanged` (subscribed on `Loaded`, released on `Unloaded` — no app-lifetime
leak, per the unit's event-lifetime rule). AutomationIds are unchanged.

## AutomationId migration / preservation

**Preserved (still present):** `ProjectCard_Root`, `Projects_SearchInput`, `Projects_ProjectsCollectionView`,
`Projects_RetryButton`, `Projects_EmptyStateLabel`, `Projects_TrackedCountLabel`, `Projects_UnreadCountLabel`,
`Projects_MarkAllReadLabel`, `Projects_TogglePollingButton`, `Projects_LastScanStatus`/`_LastScanLabel`/
`_RefreshLabel`, `Projects_FiltersButton`, `Projects_FiltersModal`, all `Pipeline_*` and `Notifications_Flyout`.

**Moved:** `Sidebar_StatValueLabel` → now on the feed header today-card number (was the sidebar stat;
dev22 note anticipates the DataSync stat xpath retargeting to the feed page). `ProjectCard_ExecutionLabel`
→ placed on the `مدة التسليم` value (identical underlying `ArabicRelativeTime.Days` data; `Execution`
and `Delivery` resolve the same way), so `ProjectsPageTests.ProjectCard_ExecutionLabel_ShowsRealDurationOrPlaceholder_NeverFabricated`
still finds the id.

**Dropped (retired elements — test-side follow-up):** `ProjectCard_NewRibbon`, `ProjectCard_EnrichmentShimmer`,
`ProjectCard_OpenOnSourceButton`, `Projects_ConnectionLabel`. **Text change to note:** `Projects_TrackedCountLabel`
now renders "{n} مشروعًا من {source}" (mockup) instead of "{n} مشروع متتبَّع"; the
`DataSyncTests.FooterTrackedAndUnreadCounts_MatchLiveDbCounts` assertion and the
`DataSyncTests.ProjectsAddedTodayStat` caption-xpath ("مشاريع مضافة اليوم") need test-side retargeting
to the new strings/placement (per the dev22 note). Unread text "{n} غير مقروء" is unchanged.

## Units

No new reusable unit introduced — this wave restyles existing views reusing AppButton/AppIcon/
AppToggle-adjacent pills/SearchInputField/FilterModal/LastScanStatus/PipelineDashboardPanel/
PipelineRadar/SplitterHandle/ShimmerBox/ContentSettle/BrandPalette. The today card is page-inline
header content, not a reusable unit (and deliberately static — no new animation; `ContentSettle`
already owns the mount). `UNITS.md` therefore unchanged, consistent with the repo rule.

## Verification / gates

| # | Gate | Result |
|---|------|--------|
| 1 | Windows build 0 errors / 0 new warnings | **PASS (compile)** — full build compiled with only the 2 baseline `CS8604` warnings in `WinAppSdkVariation.Windows.cs`. `bin/.../MostaqlK.exe` copy is blocked by the `aswbIDSAgent` AV (name-specific: a test-named copy succeeds, `MostaqlK.exe` is quarantined). Clean `0 Warning(s) 0 Error(s)` reproduced via `-p:OutDir=scratch/wout/` build. |
| 2 | Android build 0 errors (`-p:JavaSdkDirectory=...jdk-17.0.12`) | **PASS** — `0 Error(s)`, 1 pre-existing `CS1574` in `ContentSettle.cs` (not a touched file). |
| 3 | ParserTests / DbMigrationTests | **PASS** — `TOTAL: 333 passed, 0 failed`; `ALL DB MIGRATION CHECKS PASSED`. |
| 4 | Launch smoke + screenshot | **BLOCKED BY ENVIRONMENT** — app launched from scratch reached `App() created successfully` + first heartbeat with the only FAULT being the unrelated `ToastAumidRegistrar.EnsureRegistered` `UnauthorizedAccessException` (packaged-AUMID permission, pre-existing, swallowed). The AV (`aswbIDSAgent`) quarantines `MostaqlK.exe` after launch, so no window persisted long enough to screenshot; the binary literally disappears. Manual A/B visual proof could not be captured here. Recommend re-running the smoke on a host without the AV file-quarantine. |
| 5 | Grep: no inline brand hex / no new DB awaits | **PASS** — no `#2386C8/#5CA8DE/#1A6DA6/#EFF6FB/#FFA200/#FFB84D/#9A6100/#FFF4DE/#3B82F6` in either XAML; `git diff` of the two VMs adds no `await …Repository` / `Projects.Clear` / `_projectRepository.` lines. Store semantics intact. |

## Screenshot paths

`scratch/w3_shots/` was created but is empty — gate-4 screenshots could not be produced due to the AV
quarantine of `MostaqlK.exe` (see gate 4).

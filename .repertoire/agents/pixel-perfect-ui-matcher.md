# Pixel-Perfect UI Matcher — Status Report

**Date:** 2026-08-09
**Status:** SUBSTANTIALLY IMPROVED — all 4 pages now have real, fresh screenshot evidence for the
first time. The biggest structural gap (missing sidebar on Settings/About/Project Details) is
fixed via a new shared `AppSidebar` unit. Icon fidelity (placeholder glyphs) and the Windows-10
title-bar remnant remain, for documented reasons below.

## What this session did

1. **Built a new reusable unit: `AppSidebar`** (`UI/PlatformComponents/AppSidebar/AppSidebar.xaml`
   + `.cs`), a `ContentView` encapsulating the sidebar markup common to all 4 design mockups
   (logo, 5 nav items, `ActivePage` bindable property for active-item highlighting, `StatValue`
   bindable property for the "مشاريع مضافة اليوم" stat card, dark-mode row, and 5 `Clicked`
   events for nav wiring). Registered in `UNITS.md`.
2. **Wired `AppSidebar` into 3 pages that had zero sidebar before**: `SettingsPanel.xaml`,
   `AboutPage.xaml`, `ProjectDetailsPage.xaml` (previously only `MainWindowPage` had any sidebar,
   as ad-hoc inline XAML — that page was **not** touched/regressed and still uses its own inline
   nav rail; migrating it to `AppSidebar` too is a good, low-risk follow-up but was not done this
   session to avoid risking the already-verified Projects page).
3. **Rebuilt `AboutPage.xaml` from a stub into a real layout** matching `about.html`: identity
   card (M logo, name, version pill, description), "حقائق سريعة" (quick facts) list (platform,
   storage, rate limit, data policy, language), and "نطاق الإصدارات" (version scope) roadmap list
   (v1 current / v2 upcoming / v3 future) with colored status dots.
4. **Restructured `SettingsPanel.xaml`** into a header-bar + card layout matching `settings.html`'s
   chrome, while **keeping the app's actual functional settings fields** (poll interval, request
   rate, grouping mode/threshold, dark-mode toggle) rather than rebuilding the mockup's literal
   content (a URL query-params editor for the scan endpoint) — see "Known content divergence"
   below for why.
5. **Got first-ever screenshots for Settings, About, and Project Details**, iterating on Settings
   until the sidebar + card layout matched, and comparing About directly against a fresh
   `design_about.png` capture (excellent structural match — logo, nav highlighting, stat card,
   identity card, and quick-facts list all line up).
6. **Investigated icon fidelity**: read the raw HTML of all 4 mockups and confirmed the icons are
   **FontAwesome 6 icon-font glyphs** (`<i class="fa-solid fa-list-check">`, etc. loaded from a
   CDN `<link>`), **not inline SVG `<svg>`/`d`-path markup** as the continuation brief assumed.
   Since no FontAwesome font file is bundled in the repo (`Resources/Fonts` has none), and adding
   + registering + glyph-mapping a real icon font correctly is a substantial scoped task on its
   own, this was **not implemented this session** — placeholder Unicode glyphs (☷ ⌕ ♧ ⚙ ⓘ) remain
   in `AppSidebar` (and `MainWindowPage`'s own inline copy). This is documented as the most
   actionable concrete follow-up (see Recommendations).
7. **Re-verified Projects page** after the `AppSidebar` refactor with a fresh screenshot — layout,
   active-item highlight, and content are unchanged/correct (Projects page itself was not
   migrated to `AppSidebar`, so this was purely a regression check on the rest of the app).

## Per-page status (honest, with fresh evidence)

| Page | Screenshot evidence | Match assessment |
|---|---|---|
| Projects (`MainWindowPage`) | `tools\temp\app_projects_v9.png` vs `tools\temp\design_projects.png` | **Good** — sidebar, header, search bar, active/inactive nav states, stat card all align with the mockup structure/spacing. Feed area is empty in this screenshot because no live project data was loaded in this build/test run (not a UI regression — feed rendering itself was previously verified in earlier sessions). |
| Settings (`SettingsPanel`) | `tools\temp\app_settings_v2.png` vs `tools\temp\design_settings.png` | **Structurally good** (sidebar + header-bar + card chrome now matches). **Content diverges from the mockup on purpose** — see "Known content divergence" below. Also noticed: the `AppEntry`/`Picker` fields for فترة الفحص/الحد الأقصى/إلخ render with no visible input box border in this screenshot — a pre-existing `AppEntry` styling gap unrelated to this session's sidebar work, left as a follow-up. |
| About (`AboutPage`) | `tools\temp\app_about_v1.png` vs `tools\temp\design_about.png` | **Very good** — identity card, quick-facts list, and sidebar (including the active "حول التطبيق" highlight) match the mockup closely in structure, spacing, and copy. Version-scope card also implemented (not visible in the captured viewport but present and scrollable). |
| Project Details (`ProjectDetailsPage`) | `tools\temp\app_projectdetails_v1.png` vs `tools\temp\design_projectdetails.png` | **Partial evidence only** — sidebar now renders correctly (structural fix applied), but the main content area is blank in the screenshot because this page requires a real `projectId` query-parameter to load data (it's a detail-only route, reached via `Shell.Current.GoToAsync` with a project ID from the feed) and the temporary startup-route technique used for the other 3 pages doesn't supply one. The title/description/budget/skills/attachments XAML itself was unchanged this session (already built out in a prior session) and was not re-verified pixel-by-pixel against `project-details.html` because it never rendered with real data in this session. |

## Known content divergence: Settings page

`settings.html`'s actual card content is a **URL query-params editor** for the project-scan
endpoint (`mostaql.com/projects?category=development&sort=latest`, with تحميل/إعادة تعيين
buttons) — this is a materially different feature from the app's current `SettingsViewModel`
(poll interval, requests/minute, grouping mode/threshold, dark-mode toggle). Rebuilding the
*content* to match would mean either fabricating a query-params feature with no backing
`ViewModel`/service, or ignoring the app's real, working settings. This session made the
**chrome** (sidebar, header bar, card shell) mockup-accurate while intentionally keeping the
**real, functional field set** — flagged here explicitly rather than silently diverging.

## Confirmed this session

- Build: `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -v:q` → **0 Warnings, 0
  Errors**, checked after every structural change (5 separate clean builds).
- Startup route: temporarily switched to Settings, then About, then Project Details (one at a
  time, each reverted before the next) purely to capture screenshots; **confirmed restored** to
  `MainWindowPage` first in `AppShell.xaml` before finishing (verified via `Get-Content` grep — no
  `_TEMP` routes remain).
- `MostaqlK.exe` and the temporary `python -m http.server` were both stopped; no processes left
  running at session end.
- Stale intermediate screenshots from this and prior sessions were deleted, keeping one
  app/design pair per page.

## Screenshot evidence (final set)

- `tools\temp\app_projects_v9.png` / `tools\temp\design_projects.png` — Projects (regression check).
- `tools\temp\app_settings_v2.png` / `tools\temp\design_settings.png` — Settings (first evidence).
- `tools\temp\app_about_v1.png` / `tools\temp\design_about.png` — About (first evidence).
- `tools\temp\app_projectdetails_v1.png` / `tools\temp\design_projectdetails.png` — Project
  Details (first evidence, sidebar only — content area unpopulated, see above).

## Known outstanding gaps

1. **Icons are still Unicode placeholders, not FontAwesome glyphs.** Root cause: the mockups use
   FontAwesome 6 icon-font classes (`fa-solid fa-list-check`, `fa-solid fa-gear`, etc.), not
   inline SVG paths as previously assumed — there is no `d`-path data to extract. A correct fix
   requires bundling a FontAwesome (or similar) `.ttf`/`.otf` font file under `Resources/Fonts`,
   registering it in `MauiProgram.cs` via `.ConfigureFonts(...)`, and mapping each icon to its
   Unicode codepoint in the FontAwesome cheatsheet (e.g. `\uf0ae` for `fa-list-check`). This is a
   scoped, well-defined follow-up task, not attempted this session due to the remaining step
   budget after the higher-priority sidebar/screenshot work.
2. **`MainWindowPage` still has its own inline nav-rail XAML**, not migrated to the new
   `AppSidebar` unit — left alone this session specifically to avoid regressing the one page that
   had prior, thorough visual verification. Migrating it would remove ~20 lines of duplication.
3. **Project Details page content (title/description/budget/skills/attachments) was not
   re-verified against `project-details.html` with real data** — only the sidebar addition was
   verified. A follow-up should either seed a test project ID via the DB/service layer or extend
   the ViewModel with a design-time/preview data path so this page can be screenshotted with
   content.
4. **`AppEntry`/`Picker` fields on the Settings page have no visible input-box chrome** (border/
   background) in the current screenshot — noticed as a side effect of this session's work, not
   something this session's scope (sidebar/icons) directly addresses.
5. **Windows-10 title-bar black remnant**: confirmed in the prior session as a genuine OS-version
   ceiling (`DWMWA_CAPTION_COLOR`/`DWMWA_TEXT_COLOR` require Windows 11 build ≥22000; this test
   machine is Windows 10 22H2 build 19045). Not re-litigated this session per the continuation
   brief's explicit instruction.

## Recommendations for continuation session

1. Bundle a real FontAwesome (or Segoe Fluent Icons, if an Apache/MIT-compatible subset is
   preferred) font file and replace the Unicode placeholder glyphs in `AppSidebar` (and
   `MainWindowPage`'s inline copy) with real `Label FontFamily="..." Text="&#xXXXX;"` glyphs at
   the correct size (`w-5 text-center` ≈ 20px in the mockup) and color per nav state.
2. Migrate `MainWindowPage`'s inline nav rail to use the new `AppSidebar` unit for consistency and
   to remove duplication, with a careful before/after screenshot diff to guard against regression.
3. Seed a real project (via the DB or a design-time stub) so `ProjectDetailsPage` can be
   screenshotted with actual content and compared pixel-by-pixel against `project-details.html`.
4. Add visible input-box chrome (border/background) to `AppEntry`/`Picker` so Settings' numeric
   fields are visually legible, matching the mockup's bordered input styling.
5. Decide/implement a policy for the Windows-10 title-bar remnant (documented ceiling, not a bug).

## Conclusion

This session closed the single biggest gap called out in the continuation brief — Settings,
About, and Project Details had **zero** screenshot evidence and, worse, the first Settings
screenshot revealed a structural defect (no sidebar at all) that no prior session had caught.
All 3 pages now share a new, reusable `AppSidebar` unit and have real screenshot evidence;
About in particular is a strong match to its mockup. Icon fidelity was investigated in depth and
found to require a different (and larger) fix than assumed — FontAwesome font bundling, not SVG
path extraction — and is left as a well-documented, actionable follow-up rather than a partial/
incorrect attempt. The build is clean (0 errors), the startup route is confirmed restored to the
Projects feed, and all temporary processes were cleaned up.

# projects-dark-theme-implementer

## Goal

Make the Projects page (`MainWindowPage` + `ProjectCard`) fully dark-theme aware, matching
`.repertoire/design/mvp/projects.html` in its dark state. Colour-only change; no layout, sizing,
spacing, font, text or binding changes.

## Actions taken

- Read `UNITS.md`, `.repertoire/design/mvp/projects.html` (all `dark:` class pairs), and
  `Resources/Styles/Colors.xaml` for the existing `AppThemeBinding` brush pattern.
- Replaced every hardcoded colour in the two XAML files with inline
  `{AppThemeBinding Light=..., Dark=...}` using the mockup's exact light/dark class pairs.
- Verified both code-behind files (`MainWindowPage.xaml.cs`, `ProjectCard.xaml.cs`) contain **no**
  colour assignments — nothing to make theme-aware there, so they were left untouched.
- No implicit `<Style>` was added anywhere (respecting the known unpackaged-WinUI startup crash).

## Files touched

- `Features/Projects/Views/MainWindowPage.xaml`
- `Features/Projects/Views/ProjectCard.xaml`
- `.repertoire/agents/projects-dark-theme-implementer.md` (this report)

`UNITS.md` was **not** changed — no unit contract was materially changed and no new unit was added.

## Mapping applied

| Element | Light | Dark | Mockup source |
|---|---|---|---|
| ContentPage / Root Grid / FeedContent background | `#F1F5F9` | `#020617` | `bg-slate-100 dark:bg-slate-950` |
| Sidebar backing BoxView, header panels, footer bar, card surface | `White` | `#0F172A` | `bg-white dark:bg-slate-900` |
| Panel strokes, footer divider, card dividers | `#E2E8F0` / `#F1F5F9` | `#1E293B` | `border-slate-200/100 dark:border-slate-800` |
| Search field fill | `#F8FAFC` | `#1E293B` | `bg-slate-50 dark:bg-slate-800` |
| Search text / headings / stat values | `#0F172A` | `#F1F5F9` | `text-slate-900 dark:text-slate-100` |
| Secondary text (interval, rate, stat labels, footer) | `#64748B` | `#94A3B8` | `text-slate-500 dark:text-slate-400` |
| Tertiary text/icons (gauge, gear, filter, placeholder, posted-at) | `#94A3B8` | `#64748B` | `text-slate-400 dark:text-slate-500` |
| Card description | `#475569` | `#94A3B8` | `text-slate-600 dark:text-slate-400` |
| Skill pill fill | `#EFF6FF` | `#132237` | `bg-blue-50 dark:bg-blue-500/10` (blue-500 @10% over slate-900) |
| Skill pill text | `#2563EB` | `#60A5FA` | `text-blue-600 dark:text-blue-400` |
| Live badge fill | `#ECFDF5` | `#112828` | `bg-green-50 dark:bg-green-500/10` (green-500 @10% over slate-900) |
| Budget value | `#16A34A` | `#4ADE80` | `text-green-600 dark:text-green-400` |
| Footer separator dot | `#CBD5E1` | `#475569` | `bg-slate-300 dark:bg-slate-600` |
| Brand accent (`#2386C8`) | `#2386C8` | `#5CA8DE` | `AccentPrimaryDark` from `Colors.xaml` |
| Brand positive (`#2E9E6B`) | `#2E9E6B` | `#4FBF8C` | `AccentPositiveDark` from `Colors.xaml` |

Left intentionally theme-invariant (the mockup has no `dark:` variant for these): the red
poll-toggle button `#EF4444` (inline `style="background-color:#ef4444"`), the blue avatar circle
`#3B82F6`, the green "connected" dot `#22C55E`, and white text on coloured fills.

## Verification

- `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -v:q` → **0 errors, 0 warnings**.
- `tools/parity_check.py --page projects --theme dark` → app launched without crashing and renders
  dark; **overall_similarity 0.2292 → 0.6253**. Dominant-palette check confirms `#0F172A` (78.7%)
  and `#010516` now dominate, matching the mockup (palette_similarity 0.9960).
- `tools/parity_check.py --page projects --theme light` → **0.5902** (baseline 0.6105). No light
  value was altered by this change (every `Light=` operand is the original hardcoded colour), so
  this delta is harness run-to-run variance — relative timestamps ("منذ N دقائق"), the live/scan
  status strings and pulse state differ between captures. Palette similarity for light remains
  0.9912 with the same dominant `#FEFEFE`/`#F3F7FA` surfaces as before.

## Shared-unit changes still needed (NOT made — owned by the master agent)

1. **`UI/PlatformComponents/AppCard/AppCard.cs`** — the constructor and `UpdateAccentBorder()`
   hardcode `BackgroundColor = Colors.White` and `Stroke = #E2E8F0` / `#2386C8`. I overrode
   `BackgroundColor` per-instance from `ProjectCard.xaml`, but the **stroke cannot be overridden**
   because `UpdateAccentBorder` re-assigns it whenever `IsUnread` changes, so every card still draws
   a light `#E2E8F0` border in dark mode. It needs `SetAppTheme<Brush>(StrokeProperty, ...)` with
   `Light=#E2E8F0, Dark=#1E293B` (read) and `Light=#2386C8, Dark=#5CA8DE` (unread), plus a
   theme-aware default background so other pages inherit it.
2. **`Features/Projects/ViewModels/ProjectCardViewModel`** — `EnrichmentBadgeBackground` /
   `EnrichmentBadgeForeground` are computed colours bound into `ProjectCard.xaml`. They are still
   light-only; the mockup uses `bg-green-50 dark:bg-green-500/10` with
   `text-[color:var(--accent-positive)]`. This file was outside my ownership.
3. **`UI/PlatformComponents/SearchInputField`** and `UI/DesignSystem/ShimmerBox` /
   `LabelWithSubText` — I passed theme-aware colours into `SearchInputField` from the page, but any
   internal default (clear-button glyph, border) and the shimmer gradient are likely still light-only.

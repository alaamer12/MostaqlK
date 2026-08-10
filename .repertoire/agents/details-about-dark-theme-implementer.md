# details-about-dark-theme-implementer

## Goal

Make the Project Details page and the About page fully dark-theme aware, matching
`.repertoire/design/mvp/project-details.html` and `about.html` in their dark states, using the
exact light->dark colour mapping derived from the mockups' Tailwind `dark:` classes.

## Actions taken

- Read `UNITS.md`, `Features/Projects/Views/MainWindowPage.xaml` (reference conversion) and both
  mockups (`project-details.html`, `about.html`) to confirm every `dark:` class value.
- Replaced every hardcoded colour in the two pages with `{AppThemeBinding Light=..., Dark=...}`
  following the supplied mapping. No layout, sizing, spacing, font, text or binding changes.
- Verified the code-behind files contain no colour assignments, so they needed no edits.

## Files touched

- `Features/Projects/Views/ProjectDetailsPage.xaml`
- `Features/Projects/Views/AboutPage.xaml`
- `.repertoire/agents/details-about-dark-theme-implementer.md` (this report)

`ProjectDetailsPage.xaml.cs` and `AboutPage.xaml.cs` were inspected but contain no colours, so
they were left unchanged. No shared unit under `UI/` was modified.

## Decisions made

- Page/root backgrounds (`ContentPage.BackgroundColor` + outermost `Grid`) -> `Light=#F1F5F9,
  Dark=#020617`; card/panel surfaces -> `Light=White, Dark=#0F172A`; strokes -> `Light=#E2E8F0,
  Dark=#1E293B`, matching `MainWindowPage.xaml`.
- `BackgroundColor` was added to both `ContentPage` roots (colour-only) so the window surface
  behind the content also flips, exactly as `MainWindowPage.xaml` does.
- Chips use the mockup's translucent fills composited over `slate-900`:
  `bg-blue-500/10` -> `#132239` and `bg-green-500/10` -> `#112828` (the latter matching the value
  already used in `MainWindowPage.xaml`).
- About divider `BoxView`s and the version pill were treated as inner muted fills
  (`#F1F5F9` -> `#1E293B`), not page background, per the disambiguation rule.
- Brand accents mapped as instructed: `#2E9E6B` -> `#4FBF8C`.
- The v2/v3 roadmap markers use `bg-slate-300 dark:bg-slate-600` -> `Light=#CBD5E1, Dark=#475569`;
  their titles keep `#94A3B8` in both themes (mockup `text-slate-500 dark:text-slate-400`), so the
  light rendering is unchanged.
- No implicit `<Style TargetType="Label">` was added anywhere (known startup-crash constraint).

## Verification

- `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -v:q` -> **0 errors, 0 warnings**.
- Runtime parity harness (`tools/parity_check.py`), overall_similarity before -> after:

| page | theme | before | after |
|------|-------|--------|-------|
| project-details | dark  | 0.2685 | **0.4885** |
| project-details | light | 0.5301 | **0.5305** |
| about           | dark  | 0.2477 | **0.6031** |
| about           | light | 0.6072 | **0.6071** |

Dark rose to (project-details) or above (about) light-theme parity; light did not regress.
Leftover processes cleaned up (`taskkill /IM MostaqlK.exe /F` reported none running).

## Shared-unit changes still needed but NOT made

- `UI/PlatformComponents/AppSidebar` — the sidebar column is a large share of both screenshots and
  still contributes to the residual diff; it must be converted by its owner
  (`bg-background dark:bg-slate-900`, `border-slate-200 dark:border-slate-800`, nav text
  `text-slate-600 dark:text-slate-400`, active item `bg-blue-50 dark:bg-blue-500/10` with
  `text-blue-600 dark:text-blue-400`, stat card `bg-slate-100 dark:bg-slate-800`).
- `UI/PlatformComponents/AppButton` — the attachments "تحميل" button and the retry button keep
  light-only colours.
- `UI/DesignSystem/ShimmerBox` and `LabelWithSubText` — loading/error states of Project Details are
  still light-only (not visible in the captured success state, but will show in those states).

## UNITS.md

No new reusable unit was introduced (inline `AppThemeBinding` only), so `UNITS.md` needed no new
entry.

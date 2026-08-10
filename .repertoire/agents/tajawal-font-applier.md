# tajawal-font-applier

## Goal

Apply the mockups' Tajawal typeface to every text-rendering element in the MostaqlK app so the
app's Arabic glyphs match `.repertoire/design/mvp/*.html`, raising the ssim / perceptual-hash / ORB
components of the design-parity score. Typography only — no colour, layout, sizing, spacing, text
or binding changes.

## What was done

1. Read `UNITS.md` in full (including the "Windows platform constraints" section), the four MVP
   mockups' `<style>` blocks and per-element Tailwind classes, `Resources/Styles/Styles.xaml`
   (implicit-Label-style crash warning) and `MauiProgram.cs` (fonts already registered as
   `Tajawal` / `TajawalMedium` / `TajawalBold`).
2. Mapped every text element to a face using the mockup weight classes:
   - no weight class / `font-normal` → `Tajawal`
   - `font-medium` → `TajawalMedium`
   - `font-semibold` / `font-bold` / `font-extrabold` → `TajawalBold`
3. Applied the faces **per element** (inline `FontFamily=`), converting every existing
   `FontAttributes="Bold"` into `FontFamily="TajawalBold"` so MAUI does not synthetically
   double-bold an already-bold face. The two pre-existing **explicitly keyed** styles
   (`AppButtonBase`, `AppEntryBase`) had their `FontFamily` setter switched from `OpenSansRegular`
   to `TajawalMedium` / `Tajawal` respectively.
4. Hand-tuned the elements whose mockup class is `font-medium` but which the app had rendered as
   bold: sidebar active nav row, the "مباشر" live-status pill, the poll-toggle label, the
   "تعديل" edit link, notification/settings section sub-labels, project-card badges/pills and
   skill chips, and `LabelWithSubText`'s primary line.
5. Left every icon element untouched (`AppIcon` renders artwork, not a glyph font).
6. Documented the convention in `UNITS.md` under a new "Typography convention (Tajawal)" section.

## Approach: inline attributes, not a global style

Inline per-element `FontFamily=` was used, plus the two already-keyed shared styles. An implicit
`<Style TargetType="Label">` is impossible here — `UNITS.md` and the comment in `Styles.xaml`
record (verified by bisection) that any implicit Label style kills this unpackaged WinUI build at
startup. Adding a new keyed style per size+weight+colour combination would not have shrunk the diff
much, because the weight varies element-by-element across the pages; the per-element attribute keeps
the mapping to each mockup element explicit and auditable.

## Files touched

- `Features/Projects/Views/MainWindowPage.xaml`
- `Features/Projects/Views/ProjectCard.xaml`
- `Features/Projects/Views/ProjectDetailsPage.xaml`
- `Features/Projects/Views/AboutPage.xaml`
- `Features/Settings/Views/SettingsPanel.xaml`
- `Features/Notifications/Views/RecentNotificationsFlyout.xaml`
- `UI/PlatformComponents/AppSidebar/AppSidebar.xaml`
- `UI/PlatformComponents/AppSidebar/AppSidebar.cs` (active/inactive nav row now swaps
  `TajawalMedium` / `Tajawal`)
- `UI/DesignSystem/LabelWithSubText.cs`
- `Resources/Styles/AppButtonStyle.xaml`, `Resources/Styles/AppEntryStyle.xaml`
- `UNITS.md`

No `.cs` code-behind of the pages needed changes (none of them create text at runtime);
`TruncatingLabel` / `ShimmerBox` set no font and inherit from their call sites.

## Verification

- `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -v:q` → **0 errors, 0 warnings**.
- Runtime smoke test: `parity_check.py --page projects --theme light` produced a real, fully
  rendered screenshot and a score — **the app starts without crashing**, and the captures visibly
  show the Tajawal glyph shapes replacing the previous system fallback face.
- Full `parity_check.py --all` (`overall_similarity`):

| Page | Theme | Before | After | Δ |
|---|---|---|---|---|
| projects | light | 0.5902 | 0.5933 | +0.0031 |
| projects | dark | 0.6253 | 0.6078 | −0.0175 * |
| project-details | light | 0.5305 | 0.5421 | +0.0116 |
| project-details | dark | 0.4885 | 0.5102 | +0.0217 |
| settings | light | 0.5837 | 0.5823 | −0.0014 |
| settings | dark | 0.5214 | 0.5250 | +0.0036 |
| about | light | 0.6071 | 0.6149 | +0.0078 |
| about | dark | 0.6031 | 0.6128 | +0.0097 |

\* projects/dark investigated: re-runs of the same build gave 0.6078 / 0.6016 / 0.6114, i.e. a
±0.01 spread, because the projects page renders **live feed content that differs on every launch**
(different project titles, tags and counts), so its score is inherently noisy. A clean pre-change
control could not be captured: with the change stashed, three `--theme dark` runs of the projects
page all rendered in **light** theme (score ≈ 0.10), a pre-existing dark-theme-application flake on
that page that is unrelated to typography. No wrong weight or changed font size was found on that
page — the same markup improves in light theme.

## Cleanup

- `taskkill /IM MostaqlK.exe /F` run; the scratch helper script used for the bulk XAML rewrite was
  deleted.

## Skill used

`terminal-ops` (verified repository/CLI operations: build, parity harness runs, controlled
`git stash` bisection of the projects/dark measurement, process cleanup).

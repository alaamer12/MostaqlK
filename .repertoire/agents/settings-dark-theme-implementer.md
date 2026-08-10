# settings-dark-theme-implementer

## Goal

Make the Settings page (`Features/Settings/`) fully dark-theme aware so it matches
`.repertoire/design/mvp/settings.html` in its dark state, without changing layout, sizing, text or
bindings. Baselines: dark `0.1348`, light `0.5827`.

## Result

| Page / theme | Before | After |
|---|---|---|
| settings / dark | 0.1348 | **0.5214** (palette similarity 0.9935) |
| settings / light | 0.5827 | **0.5837** (no regression) |

Build: `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` → **0 errors, 0 warnings**.

## Files touched

- `Features/Settings/Views/SettingsPanel.xaml` — every hardcoded colour → `{AppThemeBinding …}`.
- `Features/Settings/ViewModels/SettingsViewModel.cs` — theme resolution fix (see below).
- `.repertoire/agents/settings-dark-theme-implementer.md` (this report).

`Features/Settings/Views/SettingsPanel.xaml.cs` contains no colours — untouched.

## Colour mapping applied (cross-checked against settings.html)

| Element | Light | Dark | Mockup class |
|---|---|---|---|
| Page / content grid background | `#F1F5F9` | `#020617` | `bg-slate-100 dark:bg-slate-950` |
| Card / panel surfaces | `White` | `#0F172A` | `bg-white dark:bg-slate-900` |
| Card strokes | `#E2E8F0` | `#1E293B` | `border-slate-200 dark:border-slate-800` |
| Input/picker border | `#E2E8F0` | `#334155` | `border-slate-200 dark:border-slate-700` |
| Input/picker fill | `White` | `#1E293B` | `bg-slate-50 dark:bg-slate-800` |
| Headings / row labels | `#0F172A` | `#F1F5F9` | `text-slate-900 dark:text-slate-100` |
| Secondary text | `#64748B` | `#94A3B8` | `text-slate-500 dark:text-slate-400` |
| Gear glyph | `#2563EB` | `#60A5FA` | `text-blue-600 dark:text-blue-400` |
| MVP note fill | `#EFF6FF` | `#132239` | `bg-blue-50 dark:bg-blue-500/10` |
| MVP note text | `#1D4ED8` | `#93C5FD` | `text-blue-700 dark:text-blue-300` |
| Save button fill | `#2563EB` | `#3B82F6` | `bg-blue-600 dark:bg-blue-500` (text stays `White`) |
| Validation text | `#DC2626` | `#F87171` | red-600 / red-400 (was bare `Red`) |

`Picker.TextColor`/`TitleColor` and `AppEntry.TextColor`/`BackgroundColor` were added because their
platform defaults render near-black text on the dark input fill.

## Key decision — the real reason dark theme was broken

Converting the XAML alone changed **nothing** (score stayed at exactly `0.1348`): the first dark
capture still rendered a fully light page. Root cause: `SettingsViewModel.LoadFromPreferences()`
called `ApplyTheme()` during construction, setting `Application.Current.UserAppTheme` from the
stored `settings_is_dark_mode` preference (`false`). That silently overwrote the theme already
resolved at startup by `App.xaml.cs` / `StartupNavigation.ResolveTheme`, which honours the
`--theme=dark` argument. So the Settings page always forced itself back to light the moment it was
constructed.

Fix (in the one file I own that computes theme): seed `IsDarkMode` from the app's already-resolved
`UserAppTheme` and fall back to the preference only when it is `Unspecified`, and drop the
`ApplyTheme()` call from the load path. The user toggle still applies + persists via
`OnIsDarkModeChanged`. This is the only non-colour change made, and it was required — dark theme
could not be exercised at all without it.

Also verified: no implicit `<Style TargetType="Label">` was added anywhere (per the
UNITS.md Windows platform constraint).

## Functional correctness observed in the dark screenshot

- Values load from Preferences/SQLite correctly — poll interval `60`, requests/minute `2`,
  grouping threshold `5`, "مشاريع مضافة اليوم" `32` in both the card and the sidebar stat.
  No empty or stuck-loading state; no validation error banner.
- The sidebar dark-mode toggle correctly reads as **on** in dark theme (it now mirrors the
  effective app theme rather than the stale preference).
- **Pre-existing defect, not fixed (out of colour-only scope):** the "طريقة تجميع الإشعارات" Picker
  renders blank. Its `ItemsSource` is an `x:Array` of `x:String` while `SelectedItem` binds to the
  `NotificationGroupingMode` enum, so the selected value never matches an item. Needs a real
  `ItemsSource` of enum values (or a converter) — recommend a follow-up task.

## Shared-unit changes still needed (NOT made — master agent owns these)

- `MainWindowPage` paints a `BoxView` behind the sidebar column as a surface backstop;
  `SettingsPanel` does not, so the sidebar relies entirely on `AppSidebar`'s own background. It
  looks correct in the capture, but a shared fix in `AppSidebar` would be cleaner than repeating
  the `BoxView` per page.
- `AppToggle` and `AppEntry`/`Picker` have no built-in theme-aware defaults; every page has to set
  their colours by hand. Worth centralising in the units.
- The Settings mockup's actual MVP content is a **query_params** card
  (`mostaql.com/projects` prefix + input + preview + reset/save footer). The app page still shows
  the older poll-interval/rate/grouping form. That structural gap — not colour — is the largest
  remaining driver of the residual dark/light score gap, and is outside this task's scope.

## Verification

- `cmd /c "dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -v:q"` → 0/0.
- `cmd /c "tools\.venv\Scripts\python.exe tools\parity_check.py --page settings --theme dark"`
- `cmd /c "tools\.venv\Scripts\python.exe tools\parity_check.py --page settings --theme light"`
- No leftover `MostaqlK.exe` processes (the harness kills them itself).

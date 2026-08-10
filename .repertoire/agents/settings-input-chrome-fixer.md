# Settings Input Chrome Fixer — Report

## Task
Fix `AppEntry`/`Picker` fields on `Features/Settings/Views/SettingsPanel.xaml` rendering
without any visible border/background chrome (outstanding gap #4 from
`pixel-perfect-ui-matcher.md`).

## Investigation
- `UI/PlatformComponents/AppEntry/AppEntry.cs` is a thin `Entry` subclass; its base style
  (`Resources/Styles/AppEntryStyle.xaml`, key `AppEntryBase`) sets colors/fonts/min-size but
  has no `TODO` note that a shared border style hasn't landed yet — MAUI's plain `Entry`/
  `Picker` controls have no native border chrome on Windows, matching the reported symptom.
- The project's established convention for bordered inputs is wrapping the control in a
  `Border` at the call site — confirmed via `Features/Projects/Views/MainWindowPage.xaml`
  (the search bar wraps `components:SearchInputField` in
  `<Border StrokeThickness="1" Stroke="#E2E8F0" BackgroundColor="White" ...>` with
  `RoundRectangle CornerRadius="12"`). `SettingsPanel.xaml` itself already uses this same
  `Border` pattern for its card sections (`Stroke="#E2E8F0"`, `BackgroundColor="White"`,
  `RoundRectangle CornerRadius="12"`).
- `settings.html`'s only true input reference (`#query-params-input`, a post-MVP query-params
  field, not part of these settings rows) uses Tailwind `rounded-lg border border-slate-200
  bg-slate-50`. Since this exact card/row styling isn't part of the MVP settings mockup HTML,
  I matched the project's own already-established `#E2E8F0` / white-background / rounded
  border convention (used consistently across `MainWindowPage.xaml` and `SettingsPanel.xaml`
  itself) rather than introducing a new slate-50 background, for visual consistency across
  the app.

## Approach chosen
**Border-wrap at call site** (Option A from the brief), not an `AppEntry` internal refactor:
- Matches the existing convention already used for `SearchInputField` in
  `MainWindowPage.xaml` and for card sections within `SettingsPanel.xaml` itself.
- Less invasive: doesn't touch `AppEntry.cs`/`AppEntry.Windows.cs`/`AppEntryStyle.xaml`, so
  zero risk of regressing other `AppEntry` usages elsewhere in the app.

## Changes made
`Features/Settings/Views/SettingsPanel.xaml`:
- Wrapped the "فترة الفحص", "الحد الأقصى للطلبات", and "حد التجميع" `platform:AppEntry`
  fields each in a `Border` (`StrokeThickness="1"`, `Stroke="#E2E8F0"`,
  `BackgroundColor="White"`, `Padding="12,8"`, `RoundRectangle CornerRadius="8"`), moving
  `WidthRequest="100"` from the `AppEntry` to the outer `Border` and keeping bindings
  (`Text`, `Keyboard`) on the inner `AppEntry` unchanged.
- Wrapped the "طريقة تجميع الإشعارات" `Picker` in the same `Border` pattern
  (`Padding="12,4"`, `WidthRequest="160"` on the `Border`), keeping `SelectedItem` binding
  and `ItemsSource` on the inner `Picker` unchanged.
- `Grid.Column="1"` was moved from each inner control to its wrapping `Border` so the
  existing two-column row layout (label + input) is preserved exactly.

No other files were modified for the fix itself (see below for the temporary
verification-only change to `AppShell.xaml`, which was reverted).

## Values used and source
- `Stroke="#E2E8F0"`, `BackgroundColor="White"`, `RoundRectangle CornerRadius` — taken from
  the project's own existing `Border` convention already present in
  `MainWindowPage.xaml`'s search bar and in `SettingsPanel.xaml`'s own card sections (not
  from `settings.html`, since the mockup doesn't specify these exact input rows — see
  Investigation above for why this was preferred over guessing a `settings.html`-specific
  slate-50/border-slate-200 value).
- `CornerRadius="8"` used (smaller than the `12` used for outer cards) since these are
  compact inline inputs, consistent with the `8`/`12` suggestion in the task brief.
- `Padding="12,8"` (text entries) / `12,4` (picker, since `Picker` has taller intrinsic
  content) chosen for readable spacing around the numeric text without inflating row height.

## Verification
1. `Stop-Process -Name MostaqlK -Force -ErrorAction SilentlyContinue` then
   `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -v:q` → **Build succeeded,
   0 Warning(s), 0 Error(s)**.
2. Temporarily reordered `AppShell.xaml` so a `SettingsPanel` `ShellContent` was declared
   first (default Shell tab), rebuilt, launched
   `bin\Debug\net10.0-windows10.0.19041.0\win-x64\MostaqlK.exe`, waited, and captured
   `tools\snip_tool.py --pid <pid> --output tools\temp\app_settings_inputchrome_v1.png`.
3. Opened the screenshot and visually confirmed all three numeric fields and the picker
   now render with a clearly visible bordered box (light gray outline, white background,
   rounded corners) against the white card, matching the design intent — issue resolved.
4. **Reverted `AppShell.xaml`** back to its original ordering (`MainWindowPage` first/
   default route again) and ran a final clean `dotnet build` — confirmed **0 errors** with
   the startup route restored.
5. Checked all other `platform:AppEntry` / `Picker` / `SearchInputField` usages in the
   project (`MainWindowPage.xaml`'s `SearchInputField` uses its own pre-existing `Border`
   wrapper, unaffected by this change) — no other page uses bare `AppEntry`/`Picker`
   outside `SettingsPanel.xaml`, so no regressions possible elsewhere.
6. Did not touch `Services/Pipeline/`, `Infrastructure/Database/`, `Infrastructure/Http/`,
   or `Infrastructure/Notifications/`, per the brief.

## UNITS.md
No update needed: this change extends the *usage* of the existing `AppEntry` unit (adding a
`Border` wrapper at the call site in `SettingsPanel.xaml`) without changing `AppEntry`'s own
implementation, default style, or public shape — its entry in `UNITS.md` remains accurate.

## Screenshot evidence
`tools\temp\app_settings_inputchrome_v1.png`

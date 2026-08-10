# mainwindow-sidebar-migrator

## Goal
Migrate `Features/Projects/Views/MainWindowPage.xaml`/`.xaml.cs` off its own hand-written inline nav-rail markup to reuse the shared `AppSidebar` unit (`UI/PlatformComponents/AppSidebar/`), matching the pattern already used by `SettingsPanel.xaml`, without any visual/behavioral regression.

## What was done

### `Features/Projects/Views/MainWindowPage.xaml`
- Removed: the entire inline `<VerticalStackLayout x:Name="NavRail" ...>` block (logo, 5 `components:AppButton` nav items, stat card bound to `UnreadCount`, dark-mode row), and the "Temporary single root ... NavigationControl.Build" comment/setup.
- Removed: the `Grid.Resources`/empty `ResourceDictionary` placeholder that existed only to support the old recomposition trick.
- Added: `xmlns:sidebar="clr-namespace:MostaqlK.UI.PlatformComponents.AppSidebar"` namespace import.
- Added: `<sidebar:AppSidebar Grid.Column="0" ActivePage="Projects" StatValue="{Binding UnreadCount}" ProjectsClicked="OnProjectsNavClicked" AdvancedSearchClicked="OnAdvancedSearchNavClicked" NotificationsClicked="OnNotificationsNavClicked" SettingsClicked="OnSettingsNavClicked" AboutClicked="OnAboutNavClicked" />` — preserves the exact same `UnreadCount` binding source the old inline stat card used.
- Changed: root `Grid x:Name="Root"` now declares `ColumnDefinitions="Auto,*"` directly in XAML (mirrors `SettingsPanel.xaml`'s pattern) instead of being an empty shell recomposed at runtime via `NavigationControl.Build`.
- Changed: `FeedContent` grid now has `Grid.Column="1"` set directly in XAML (previously it was reparented into column 1 by `NavigationControl.BuildSidePanel` in code-behind).
- Changed: `NotificationsFlyout` now has `Grid.Column="1"` explicitly set, since it now lives directly in the 2-column `Root` grid (it previously worked because it was added on top of the already-recomposed content, always visually right-aligned via `HorizontalOptions="End"`, but explicit column placement keeps it correctly bounded to the content area, not spanning behind the sidebar).

### `Features/Projects/Views/MainWindowPage.xaml.cs`
- Removed: `using MostaqlK.UI.PlatformConcepts;` (no longer needed).
- Removed: the entire `NavigationControl.Build(NavRail, FeedContent)` / `Root.Children.Clear()` / `Root.Children.Add(...)` / `Content = Root` recomposition block from the constructor — the XAML-declared `Root` grid now lays itself out declaratively, so `InitializeComponent()` is sufficient.
- Unchanged (verified they already exactly match `AppSidebar`'s expected event signatures, so no event handler signature changes were needed):
  - `OnProjectsNavClicked` — no-op (already on Projects feed).
  - `OnAdvancedSearchNavClicked` — `// TODO: navigate to the advanced search route once implemented.` (same TODO state as `SettingsPanel.xaml.cs`).
  - `OnNotificationsNavClicked` — toggles `NotificationsFlyout.IsVisible`. This is the ONLY notifications affordance on this page (no separate bell icon exists elsewhere in the XAML), so it now routes through `AppSidebar`'s `NotificationsClicked` event instead of the old inline `components:AppButton`'s `Clicked` — behavior is identical, just wired through the shared unit's event instead of a page-local button.
  - `OnSettingsNavClicked` — `await Shell.Current.GoToAsync("SettingsPanel")` (route string verified against `AppShell.xaml`/`SettingsPanel.xaml.cs` usage — matches exactly).
  - `OnAboutNavClicked` — `await Shell.Current.GoToAsync("AboutPage")` (matches `SettingsPanel.xaml.cs`'s own About handler).
  - `OpenNotificationsFlyout()` public method (used by the tray icon's "Recent notifications" menu action) — untouched, still just sets `NotificationsFlyout.IsVisible = true`.

## Units
- No new unit created. Reused the existing `AppSidebar` unit exactly as documented in `UNITS.md` — no API extension was needed; `ActivePage`, `StatValue`, and the 5 click events already covered every requirement of `MainWindowPage`'s sidebar. `UNITS.md` was not modified.

## Behavioral differences found (old vs new)
- None found. The old inline `AppButton`s had `Clicked` handlers with identical names/behavior to what `AppSidebar` now expects; the only structural difference is that active-state styling (`BackgroundColor="#EFF6FF"` / `TextColor="#2563EB"` for the current page) is now handled internally by `AppSidebar.ApplyActiveState()` via the `ActivePage="Projects"` bindable property instead of being hardcoded per-button in the page's XAML — visually identical output.
- The old `NavigationControl.Build` runtime recomposition (via `UI/PlatformConcepts/NavigationControl.cs`) is no longer used by this page; that helper is still present in the codebase (not deleted, since it's a shared static utility) but `MainWindowPage` no longer depends on it, consistent with how `SettingsPanel` never used it either.

## Verification
- Build: `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -v:q` → **Build succeeded, 0 Warning(s), 0 Error(s)**. (One earlier build attempt failed only due to a leftover locked `MostaqlK.exe`/xaml-compiler process from a prior session holding file locks — resolved by `Stop-Process -Name MostaqlK -Force` before rebuilding, unrelated to the code change itself.)
- Visual regression: launched `bin\Debug\net10.0-windows10.0.19041.0\win-x64\MostaqlK.exe`, screenshotted via `tools\snip_tool.py`, saved to `tools\temp\app_projects_migrated5.png`. Compared against the pre-migration baseline `tools\temp\app_projects_v9.png`.
  - Sidebar width (256px), background (`#F8FAFC`), logo/wordmark, all 5 nav items (with "المشاريع" showing the active blue highlight `#EFF6FF`/`#2563EB`), the "مشاريع مضافة اليوم" stat card, and the dark-mode toggle row are all pixel-identical in placement/styling between the two screenshots.
  - Stat value differs (45 in baseline vs 53 in the new screenshot) — this is expected, it's live data (`UnreadCount` from `ProjectFeedViewModel`) that changes between app runs/polls, not a regression.
  - Content area (search bar, active-query row, project list, status bar) is unaffected — confirms `FeedContent` still occupies column 1 correctly.
  - No regression found.
- Process was stopped after verification (`Stop-Process -Name MostaqlK -Force`).
- Did not touch `Services/Pipeline/`, `Infrastructure/Database/`, `Infrastructure/Http/`, or `Infrastructure/Notifications/`, per instructions.

## Files touched
- `Features/Projects/Views/MainWindowPage.xaml`
- `Features/Projects/Views/MainWindowPage.xaml.cs`
- `.repertoire/agents/mainwindow-sidebar-migrator.md` (this report)

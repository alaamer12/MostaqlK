# design-system-theming-builder

## Goal
Scaffold the shared Design System / theming layer for MostaqlK (brand colors, spacing/corner-radius tokens, shared style dictionaries for AppButton/AppCard/AppEntry/AppToggle, a Windows-only style override, DesignSystem stub components, and placeholder folders for IconSystem/Letterbox/Stickers), wired into `App.xaml`/`App.xaml.cs`, based on the MVP mockups (`projects.html`, `project-details.html`, `settings.html`, `about.html`).

## What was done
- Read all mandatory context docs (`structure.md`, `system-components.md`, `cross-platform-ui-conventions.md`) plus the existing template `App.xaml`, `Colors.xaml`, `Styles.xaml`, and `MostaqlK.csproj` before making changes, to extend rather than duplicate.
- No `.cursor/skills/` directory exists in this repository, so the "use at least one skill" mandate could not be literally fulfilled; proceeded directly per the task's explicit scaffold-only workflow instructions (which take precedence per the guidelines' conflict rule).

## Files created
- `UI/DesignSystem/DesignTokens.cs` — static `Colors` (AccentPrimary/Dark, AccentPositive/Dark, Background/Surface Light/Dark, ReadBorder Light/Dark), `Spacing` (XS..XL), `CornerRadius` token classes using `Microsoft.Maui.Graphics.Color`.
- `UI/DesignSystem/ShimmerBox.cs`, `TruncatingLabel.cs`, `LabelWithSubText.cs` — stub `ContentView`/`Label` classes with TODOs, matching system-components.md §13.3 responsibilities.
- `UI/DesignSystem/IconSystem/README.md`, `UI/DesignSystem/Letterbox/README.md`, `UI/DesignSystem/Stickers/README.md` — one-line placeholder docs to track folders without premature empty scaffolding.
- `Resources/Styles/AppButtonStyle.xaml`, `AppCardStyle.xaml`, `AppEntryStyle.xaml`, `AppToggleStyle.xaml` — shared base styles (`AppButtonBase`, `AppCardBase`, `AppEntryBase`, `AppToggleBase`) per Mechanism 1.
- `Platforms/Windows/Styles/AppButtonStyle.Windows.xaml` — `AppButtonWindows` style, `BasedOn="{StaticResource AppButtonBase}"`.

## Files modified
- `Resources/Styles/Colors.xaml` — appended brand/neutral palette (`AccentPrimary(Dark)`, `AccentPositive(Dark)`, `AppBackground/SurfaceLight/Dark`, `ReadBorderLight/Dark`) plus matching brushes, without touching existing template resources.
- `App.xaml` — merged the four new `Resources/Styles/App*Style.xaml` dictionaries alongside the existing `Colors.xaml`/`Styles.xaml` merges.
- `App.xaml.cs` — in the `App()` constructor, merges `Platforms/Windows/Styles/AppButtonStyle.Windows.xaml` at runtime under `#if WINDOWS`; added a one-line `TODO(RTL)` comment noting where `FlowDirection` switching hooks in.
- `MostaqlK.csproj` — added a Windows-only `ItemGroup` (`Page Remove` + `MauiXaml Include`) for `Platforms/Windows/Styles/AppButtonStyle.Windows.xaml` (see Key finding below).

## Key finding (important for future Windows-XAML work)
`Platforms/Windows/*.xaml` is auto-globbed by the WindowsAppSDK/WinUI build tooling as a native WinUI `Page` item, not a MAUI resource. Adding a MAUI-namespaced `ResourceDictionary` there without excluding it from `Page` and re-including it as `MauiXaml` causes a silent `XamlCompiler.exe` failure (`MSB3073`, no diagnostic text). Fixed via the `csproj` `ItemGroup` noted above. Any future `Platforms/Windows/Styles/*.xaml` files need the same treatment (or a helper `ItemGroup` glob) unless a first-party MAUI convention supersedes this.

## Verification
- `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` — succeeded (exit code 0) after the `csproj` fix. Remaining warnings (`NU1903` SQLitePCLRaw advisory, `MVVMTK0045` AOT warnings in `Features/*ViewModel.cs`) are pre-existing / owned by other in-progress agents, not introduced by this scaffold.
- Note: while iterating, a parallel agent overwrote `MostaqlK.csproj` concurrently (adding `CommunityToolkit.Mvvm`/`Microsoft.Data.Sqlite` package references); the Windows-XAML `csproj` fix was reapplied on top of their version without removing their additions.

## Open questions / TODOs left for the user
- `ShimmerBox`, `TruncatingLabel`, `LabelWithSubText` are stubs only (compile, but no real shimmer animation / truncation / sub-text layout logic yet) — intentional per the scaffold-only scope.
- `IconSystem/`, `Letterbox/`, `Stickers/` contain only placeholder `README.md` files, no actual components yet.
- RTL `FlowDirection` switching is not implemented, only a comment hook left in `App.xaml.cs`.
- Did not touch `Features/`, `Services/`, `Infrastructure/`, `Core/`, `Models/`, `UI/PlatformComponents/`, `UI/PlatformConcepts/`, `UI/TrayIcon/` per task scope boundaries.

# Units

This document catalogs the app's **units** — the named, reusable building blocks that make up the
UI layer (per `.repertoire/.steering/v1/tech/cross-platform-ui-conventions.md`). Each unit has one
conventional name used everywhere in the codebase, regardless of how it's actually implemented per
platform.

Units are grouped by mechanism:

- **Platform Components** (`UI/PlatformComponents/`) — same shape/API on every platform, only
  native tweaks differ per OS (partial classes: `<Name>.cs` shared + `<Name>.<OS>.cs` override).
- **Platform Concepts** (`UI/PlatformConcepts/`) — same concept, a genuinely different control
  shape per platform, resolved once via `PlatformSelect.For<T>()`. Names here are intentionally
  **neutral/abstract**, never a platform-native widget name.
- **Design System** (`UI/DesignSystem/`) — pure shared primitives with no per-platform partials.
- **Tray Icon** (`UI/TrayIcon/`) — Windows desktop system-tray integration.

Status legend: `Scaffold` = stub only, no real logic yet. `Implemented` = real logic in place.

---

## Platform Components

Location: `UI/PlatformComponents/<Unit>/<Unit>.cs` (+ `<Unit>.Windows.cs`)

| Unit | Base MAUI type | Purpose | Status |
|---|---|---|---|
| `AppButton` | `Button` | Shared button used across all features. | Implemented |
| `AppCard` | `Border` | Project-feed card surface; carries `IsUnread`/`IsRead` bindable state for the unread accent-border treatment. | Implemented |
| `AppEntry` | `Entry` | Shared text input (search box, settings forms). Base of the `DebouncedEntry`/`SearchInputField` inheritance chain. | Implemented |
| `DebouncedEntry` | `AppEntry` | Adds keystroke debouncing (`DebounceMilliseconds`, `DebouncedTextChanged`/`DebouncedCommand`) via a `CancellationTokenSource` restart-on-keystroke pattern. | Implemented |
| `SearchInputField` | `DebouncedEntry` | Concrete search box (search icon + clear/"x" button via `ClearCommand`) bound to `ProjectFeedViewModel.SearchQuery`. | Implemented |
| `AppToggle` | `Switch` | Shared toggle switch (e.g. dark-mode switch, grouping enabled). Wired into `SettingsPanel.xaml` for the live dark-mode toggle. | Implemented |
| `PlatformSelect` | static helper | Not a UI unit itself — the compile-time `#if ANDROID/IOS/WINDOWS/MACCATALYST` selector every other unit above/below is built on. | Implemented |
| `AppSidebar` | `ContentView` (`UI/PlatformComponents/AppSidebar/`) | Shared sidebar nav rail (logo, 5 nav items with `ActivePage` highlight, real `AppIcon` glyphs, "مشاريع مضافة اليوم" stat card via `StatValue`, dark-mode row) matching the sidebar markup common to all 4 design mockups. Used by all 4 pages (`MainWindowPage`, `SettingsPanel`, `AboutPage`, `ProjectDetailsPage`) — `MainWindowPage`'s previous inline duplicate nav rail was migrated to this unit. | Implemented |
| `AppIcon` | `ContentView` (wraps `Image`) | Shared icon unit (`Icon` bindable property, `AppIconGlyph` enum). Renders a pre-rasterized PNG icon (originally FontAwesome SVGs, baked to PNG at build time via `MauiImage`, with a pre-colored "_active" blue variant for the 5 sidebar nav icons) loaded via `ImageSource.FromFile` against an absolute path under `AppContext.BaseDirectory`. Used by `AppSidebar`; not yet applied to `ProjectCard`/`ProjectDetailsPage`/`SearchInputField` (only 6 of the enum's icons have real artwork today — the rest fall back to the "info" icon). **History:** originally implemented as a FontAwesome icon *font* (`Label` + codepoints), but that approach hit a genuine, unresolvable platform limitation — WinUI never loads runtime-referenced custom font files on this app's unpackaged Windows build (confirmed via debug logging that 3 independent font-loading fixes all executed correctly yet still rendered empty "tofu" boxes; a standalone browser test proved the `.ttf` files themselves were valid). Switched to real SVG-derived images instead; even then, MAUI's plain resource-name `Image.Source` string (`"icon_bell"`/`"icon_bell.svg"`) silently failed to resolve on this unpackaged build (same root-cause class as the font issue) — `ImageSource.FromFile` with an absolute path is the one approach confirmed to work end-to-end. | Implemented |

Only `.Windows.cs` partials exist today (V1 = Windows-only). `.Android.cs` / `.iOS.cs` /
`.MacCatalyst.cs` partials are added per-unit only when V3 mobile work actually starts.

## Platform Concepts

Location: `UI/PlatformConcepts/<Unit>.cs`

| Unit | Mobile shape (future) | Windows shape (current) | Purpose | Status |
|---|---|---|---|---|
| `NavigationControl` | Bottom tabs | `Grid`-based side panel (nav rail + content), composed via `NavigationControl.Build(navRail, content)` from real page content/commands (see `MainWindowPage`). | Primary app navigation surface. | Implemented |
| `ModalPresenter` | Bottom sheet | Dialog (modal `ContentPage` stand-in) | Overlay/modal presentation. | Scaffold |
| `Drawer` | Swipe drawer | Flyout (`FlyoutPage` stand-in) | Secondary/contextual side panel. | Scaffold |
| `ActionMenu` | Action sheet | Context menu (`MenuFlyout` stand-in) | Contextual list of actions. | Scaffold |

Naming rule: names must stay neutral/abstract (e.g. `NavigationControl`, not `SidePanel` or
`BottomTabs`) so call sites never need renaming when mobile platforms ship in V3.

## Design System

Location: `UI/DesignSystem/`

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `DesignTokens` | static class | Brand colors, spacing scale, corner-radius tokens (Mostaql blue, Slate palette, light/dark). | Scaffold |
| `ShimmerBox` | `ContentView` | Skeleton-loading placeholder; sweeping shimmer animation. | Implemented |
| `TruncatingLabel` | `Label` | Text truncation with `MaxChars` cap + `…` ellipsis. | Scaffold |
| `LabelWithSubText` | `ContentView`/`Label` | Canonical error display: `ExternalMessage` + `FixMessage`. Used for the feed's empty/error states and the details page's error state. | Implemented |

Planned (folder placeholders exist, no units yet): `IconSystem/`, `Letterbox/`, `Stickers/`.

## Tray Icon

Location: `UI/TrayIcon/`

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `TrayIconService` | class | Windows system-tray icon: `TrayIconState` mirrored live from `IPollService.StatusChanged`/`DiscoveryQueue.Count` + right-click menu wired to real commands (Open, Pause/Resume, Check now, Recent notifications, Settings, Quit). Native icon hosting via `Platforms/Windows/TrayIconNativeHost.cs` (`Shell_NotifyIcon`). | Implemented |

---

## Adding a new unit

1. Decide the mechanism: same shape everywhere → **Platform Component**; different shape per
   platform → **Platform Concept**; pure shared/no platform variance → **Design System**.
2. Follow the folder/naming conventions in
   `.repertoire/.steering/v1/tech/cross-platform-ui-conventions.md`.
3. Add a row to the matching table above in this file.

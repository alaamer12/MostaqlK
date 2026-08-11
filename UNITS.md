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
| `SearchInputField` | `DebouncedEntry` | Concrete search box (search icon + clear/"x" button via `ClearCommand`) bound to `ProjectFeedViewModel.SearchQuery`. The unit is the bare `Entry`: MAUI's `Entry` has no leading-icon slot and no `Padding`, so the mockups' `bg-slate-50 border border-slate-200 rounded-lg` box, the `left-4` magnifier `AppIcon` and the `pl-10 pr-4` insets are composed **around** it at the call site (see the header in `MainWindowPage.xaml`). The wrapper grid is forced `FlowDirection="LeftToRight"` because Tailwind's `left-4` is a physical edge while the page is RTL. | Implemented |
| `AppToggle` | `Switch` | Shared toggle switch (e.g. dark-mode switch, grouping enabled). Wired into `SettingsPanel.xaml` for the live dark-mode toggle. | Implemented |
| `PlatformSelect` | static helper | Not a UI unit itself — the compile-time `#if ANDROID/IOS/WINDOWS/MACCATALYST` selector every other unit above/below is built on. | Implemented |
| `AppSidebar` | `ContentView` (`UI/PlatformComponents/AppSidebar/`) | Shared sidebar nav rail (logo, 5 nav items with `ActivePage` highlight, real `AppIcon` glyphs, unread badge via `NotificationCount`, "مشاريع مضافة اليوم" stat card via `StatValue`, dark-mode row) matching the sidebar markup common to all 4 design mockups. Used by all 4 pages (`MainWindowPage`, `SettingsPanel`, `AboutPage`, `ProjectDetailsPage`) — `MainWindowPage`'s previous inline duplicate nav rail was migrated to this unit. Laid out as a `Grid RowDefinitions="80,*,Auto"` mirroring the mockup's `flex flex-col` + `nav flex-1`, so the stat card and dark-mode row stay pinned to the bottom of the column; it was previously a `VerticalStackLayout` with a `VerticalOptions="Fill"` spacer, which a stack layout ignores, so those two floated up under the nav items. This is currently the **only** unit with full light/dark parity (`AppThemeBinding` for every surface/text colour plus theme-aware active-row colours in `AppSidebar.cs`, refreshed on `Application.RequestedThemeChanged`). | Implemented |
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

Planned (folder placeholders exist, no C# units yet): `IconSystem/`, `Letterbox/`, `Stickers/`. The
`Letterbox` visual language (dark navy canvas, centered icon scene, sparkle accents, feature pill,
white bold headline with one green-accented phrase) has been designed and validated in HTML at
`.repertoire/design/mvp/onboarding.html` (5-step first-run onboarding carousel: background polling,
notifications, local archive, search, final CTA) — no auth/login screens exist or are planned, per
`.repertoire/.steering/v2/tech/identity-and-auth.md`. The MAUI `Letterbox` unit itself is still not
implemented in `UI/DesignSystem/`.

## Display formatters

Location: `Core/Formatting/`

Not UI units (no visual surface of their own), but shared presentation helpers every view-model
must reuse instead of hand-rolling its own string interpolation.

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `BudgetFormatter` | static class | Turns the raw scraped `projects.budget` text into the mockup's presentation form — `2,500 - 5,500 ر.س` (thousands separator, no decimals, low value first, Saudi Riyal suffix). Storage keeps the source string untouched. Used by `ProjectCardViewModel.Budget`. | Implemented |
| `ArabicRelativeTime` | static class | Arabic relative-time wording (`منذ 3 دقائق`, `منذ 8 ساعات`) and day-count pluralisation (`يوم واحد`/`يومان`/`7 أيام`/`20 يوم`). Used by `ProjectCardViewModel.PostedRelative` (fallback when `posted_relative` is empty) and `.Delivery`. | Implemented |

## Startup flags

`AppShell.xaml.cs` (`StartupNavigation`) and `App.xaml.cs` parse the process arguments. Existing
flags: `--default-page=projects|project-details|settings|about`, `--project-id=<id>`,
`--theme=light|dark`, and:

| Flag | Effect |
|---|---|
| `--seed-design-data` | Replaces the local SQLite store with the dataset the MVP mockups are drawn against (`Infrastructure/Database/DesignDataSeeder.cs`), writing through the normal repository layer, and latches the `design_parity_mode` preference so the poll service and worker pool stay offline — otherwise freshly scraped projects would bury the seeded rows between parity captures. Idempotent: each run clears the project tables first. |
| `--seed-design-data=off` | Clears the latch and restores live polling. The seeded rows stay until the next reseed or a real poll. |

## Diagnostics

Location: `Services/Diagnostics/`

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `InteractionLogger` | static class | Structured rolling log sink under `FileSystem.AppDataDirectory/interaction-log.txt`. `Enter`/`Exit`/`Fault` bracket a traced command; `Mark(checkpoint, variant, data)` is the A/B checkpoint helper ("A"=branch taken/enter, "B"=other branch/skip) used to prove which code path actually executed instead of guessing from UI behaviour alone. All writes are best-effort and never throw. | Implemented |
| `TraceInteractionAttribute` / `TraceScope` | attribute + `IDisposable` scope | `[TraceInteraction("Name")]` documents that a command/handler (`TogglePolling`, `RefreshCommand`, `SaveCommand`, `SelectCommand`, `ResolveCommand`, sidebar nav handlers, ...) is traced; the method body wraps itself in `using var _ = TraceScope.Begin("Name", parameters)` (calling `MarkFaulted` on catch) so entry/exit/exceptions land in `InteractionLogger` for both humans and Appium tests to inspect. No IL weaving — the attribute is documentation, the `TraceScope` call is what actually logs. | Implemented |
| `ErrorOutcomeAttribute` (`Core/ErrorOutcomeAttribute.cs`) | attribute + `ErrorOutcome` enum | Companion to `[ErrorCode]`/`[ErrorCategory]`/`[NeitherContract]` in `Core/ErrorAttributes.cs` (see `.repertoire/.steering/base/tech/errors-handling.md`). `[ErrorOutcome(ErrorOutcome.Handled\|Ignored\|Rethrown, Label = "...")]` is applied to the method enclosing a catch block/`Result<T>.Err` arm to document what happens to the captured `DomainError`/exception — surfaced, deliberately swallowed (best-effort path), or rethrown/propagated. Purely documentation/tooling metadata, consumed by the static checker in `tools/ErrorHandlingAudit` (see `docs/error-handling-audit.md`). | Implemented |

Naming convention for AutomationIds (added incrementally as pages are catalogued in
`docs/ui-test-catalog.md`): `<Page>_<Element>`, e.g. `Sidebar_ProjectsButton`,
`Projects_SearchInput`, `Settings_SaveButton` — set directly on the exact control that owns the
`TapGestureRecognizer`/`Command`, never on a wrapping container, so `WindowsDriver.FindElementByAccessibilityId` maps 1:1 to the real hit-test target.

## Notifications

Location: `Infrastructure/Notifications/`, `Services/NotificationDispatcher.cs`, `Services/NotificationGrouper.cs`

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `ToastAumidRegistrar` | static class | Fixes real toasts never appearing on this unpackaged (`WindowsPackageType=None`) build: `AppNotificationManager.Register()` alone only registers the COM activation server, it does not give the process an identity, so without an explicit AUMID + a Start Menu shortcut carrying that AUMID, Windows silently drops the toast instead of showing it. Idempotently calls `SetCurrentProcessExplicitAppUserModelID` and creates/repairs `%AppData%\Microsoft\Windows\Start Menu\Programs\MostaqlK.lnk` with the `System.AppUserModel.ID` property set to the constant `Aumid` ("MostaqlK.App"), via raw `IShellLinkW`/`IPropertyStore` COM interop. Called once from `WindowsToastSender.EnsureRegistered()` before `AppNotificationManager.Default.Register()`. Best-effort/never throws — logged via `InteractionLogger`. | Implemented |
| `WindowsToastSender` | class | Sends the actual Windows toast via `Microsoft.Windows.AppNotifications.AppNotificationManager` (individual vs grouped builder per project batch size). Toast failures are never silently swallowed: every send outcome (success or exception) is logged via `InteractionLogger.Mark`/`Fault`, and `NotificationDispatcher.HandleFlush` double-checks the returned `Result<bool>` on top of that. | Implemented |
| `NotificationGrouper` | class | Buffers newly discovered projects and decides when to flush a batch to `WindowsToastSender` (immediate single-item bypass, end-of-minute, after-N-minutes, or after-N-count), instrumented with `InteractionLogger.Mark` checkpoints on every timer schedule/flush so a real run can be traced to confirm flushing actually happens. Verified live: `NotificationGrouper.Flush` → `NotificationDispatcher.HandleFlush` → `WindowsToastSender.SendAsync` all fired for real newly-discovered projects with no `FAULT` entries, and Windows' own notification-sources settings list registered `MostaqlK` as a toast sender, confirming the AUMID fix took effect. | Implemented |

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

## Windows platform constraints (verified, do not re-litigate)

- **Never add an implicit `<Style TargetType="Label">`** to `Resources/Styles/Styles.xaml`. Verified
  experimentally: any implicit Label style — even one that only sets the already-working
  `OpenSansRegular` font — makes this unpackaged WinUI build terminate during startup before a
  window ever appears. Set `FontFamily` on the individual label/unit instead. This is the same class
  of limitation as the runtime-loaded custom font failure documented for `AppIcon` above.
- `Tajawal-Regular/Medium/Bold.ttf` (the mockups' typeface) are bundled in `Resources/Fonts` and
  registered in `MauiProgram` as `Tajawal`/`TajawalMedium`/`TajawalBold`, but must be applied
  per-unit for the reason above.

## Typography convention (Tajawal)

Every mockup (`.repertoire/design/mvp/projects.html`, `project-details.html`, `settings.html`,
`about.html`, `onboarding.html`) sets `font-family: 'Tajawal'` on `body`, so **every text-rendering
element in the app uses a Tajawal face**. The registered alias is picked from the matching
element's Tailwind weight class in the mockup:

| Mockup weight class | `FontFamily` | Typical use |
|---|---|---|
| none / `font-normal` | `Tajawal` | body copy, descriptions, metadata, entries, placeholders |
| `font-medium` | `TajawalMedium` | buttons, active sidebar nav row, badges/pills, section sub-labels |
| `font-semibold` / `font-bold` / `font-extrabold` | `TajawalBold` | page and card titles, stat values, emphasised inline counts |

Rules:

- Apply it **per element** (inline `FontFamily="…"`) or through an **explicitly keyed**
  `<Style x:Key="…">` that elements opt into. Implicit `TargetType="Label"` styles crash startup —
  see the Windows platform constraints above.
- `TajawalMedium`/`TajawalBold` must **not** also carry `FontAttributes="Bold"`; MAUI would
  synthesise a second bold pass on an already-bold face and the glyphs come out heavier than the
  design. The bold XAML elements were converted from `FontAttributes="Bold"` to
  `FontFamily="TajawalBold"`.
- Text created in C# inside a unit sets `FontFamily` on the `Label` it builds (see
  `LabelWithSubText`, and `AppSidebar.SetRowState`, which swaps `TajawalMedium`/`Tajawal` with the
  active state).
- Shared keyed styles already carry the right face: `AppButtonBase` → `TajawalMedium`,
  `AppEntryBase` → `Tajawal`.
- **Never** change the `FontFamily` of an icon element — `AppIcon` renders artwork, not text, and
  any glyph-font element must keep its icon font.
- Contrary to the runtime-font limitation documented for `AppIcon`, the bundled Tajawal faces *do*
  load on the unpackaged Windows build (confirmed visually against the parity screenshots), because
  they are referenced statically as `FontFamily` values on XAML elements.

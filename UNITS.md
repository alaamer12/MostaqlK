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
- **Platform Infrastructure** (`Core/Platform/`) — platform-neutral helpers for reading the
  running platform and mapping any capability to its per-platform answer, including "not
  available on this platform at all".

Status legend: `Scaffold` = stub only, no real logic yet. `Implemented` = real logic in place.

---

## Block Components & Layout Barrels

Location: `Features/<Feature>/Views/` & `Features/<Feature>/Views/Layouts/`

| Unit | Base MAUI type | Purpose | Status |
|---|---|---|---|
| `ProjectCard` | `ContentView` (`Features/Projects/Views/ProjectCard.xaml`) | Platform-agnostic shell delegating layout instantiation to `ProjectCardWindowsLayout` (desktop 4-column rich card) or `ProjectCardMobileLayout` (streamlined touch card) via `PlatformSelect.For<Func<View>>()`. | Implemented |
| `ProjectCardWindowsLayout` | `ContentView` (`Features/Projects/Views/Layouts/ProjectCardWindowsLayout.xaml`) | Rich desktop card layout containing client ratings, 4 stats columns (`Budget`, `Proposals`, `AvgBid`, `Execution`), full skill pills, and direct external action button. | Implemented |
| `ProjectCardMobileLayout` | `ContentView` (`Features/Projects/Views/Layouts/ProjectCardMobileLayout.xaml`) | Streamlined mobile project card with vertical hierarchy (Card Type 3 in mobile spec: Title, status pill, excerpt, skills flex wrap, proposals/time/budget footer, swipe gestures). | Implemented |
| `DashboardProjectCard` | `ContentView` (`Features/Projects/Views/Layouts/DashboardProjectCard.xaml`) | Card Type 1 in mobile spec: compact matched project card with Title, green "جديد" badge, 2-line description, skill tags, time ago, and prominent green budget. | Implemented |
| `RecentScanRow` | `ContentView` (`Features/Projects/Views/Layouts/RecentScanRow.xaml`) | Card Type 2 in mobile spec: compact scan history row with 38px circular category/client avatar, single-line bold title, relative time, and trailing green budget. | Implemented |
| `ScraperPowerButton` | `ContentView` (`Features/Dashboard/Views/ScraperPowerButton.xaml`) | 148px circular central control toggling scraper state with dynamic radial elevation shadow, pulsing status dot, and emerald (running) vs crimson (stopped) gradients. | Implemented |
| `DashboardDailyStats` | `ContentView` (`Features/Dashboard/Views/DashboardDailyStats.xaml`) | 4-column real-time metric counter grid (`فحص`, `مشاريع`, `مطابقة`, `تنبيهات`) for the mobile dashboard. | Implemented |
| `MainWindowPage` | `ContentPage` (`Features/Projects/Views/MainWindowPage.xaml`) | Host page delegating to `MainWindowWindowsLayout` (4-column desktop layout with sidebar, feed, splitter, pipeline panel) or `MainWindowMobileLayout` (single-column feed). | Implemented |
| `ProjectDetailsPage` | `ContentPage` (`Features/Projects/Views/ProjectDetailsPage.xaml`) | Host page delegating to `ProjectDetailsWindowsLayout` (2-column desktop layout with sidebar, main content, and owner sidebar card) or `ProjectDetailsMobileLayout` (single-column mobile layout with owner stats card and wrapped skills). | Implemented |
| `ProjectDetailsWindowsLayout` | `ContentView` (`Features/Projects/Views/Layouts/ProjectDetailsWindowsLayout.xaml`) | 2-column desktop details view with navigation sidebar, project description, wrapped skills, attachments, and sidebar project card + owner statistics. | Implemented |
| `ProjectDetailsMobileLayout` | `ContentView` (`Features/Projects/Views/Layouts/ProjectDetailsMobileLayout.xaml`) | Single-column mobile details view with back app bar, description, project metadata, mobile-adapted owner statistics card, wrapped skills, and attachments. | Implemented |
| `SettingsPanel` | `ContentPage` (`Features/Settings/Views/SettingsPanel.xaml`) | Host page delegating to `SettingsPanelWindowsLayout` or `SettingsPanelMobileLayout`. | Implemented |
| `AboutPage` | `ContentPage` (`Features/Projects/Views/AboutPage.xaml`) | Host page delegating to `AboutPageWindowsLayout` or `AboutPageMobileLayout`. | Implemented |

## Platform Infrastructure

Location: `Core/Platform/`

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `CurrentPlatform` | static class (`Core/Platform/CurrentPlatform.cs`) | One of the two canonical exempt files (the other being `PlatformSelect`) containing a compile-time `#if` ladder to resolve the running platform (`AppPlatform.Windows/Android/iOS/MacCatalyst`), exposed as `CurrentPlatform.Current`/`IsWindows`/`IsMobile`. Never re-detected at runtime — the running platform is fixed for the process's lifetime. Every capability-mapping site and future cross-platform audit should read the platform from here instead of ad hoc `#if WINDOWS` checks. | Implemented |
| `PlatformCapability<T>` | generic static class (`Core/Platform/PlatformCapability.cs`) | Declares, once per capability, what it resolves to per platform via `Resolve(windows:, android:, ios:, macCatalyst:)`, including an explicit `null` for platforms with no answer (e.g. the tray icon has no mobile equivalent at all). `WindowsOnly(windows)` is a convenience overload for exactly that no-mobile-equivalent case. Replaces ad hoc `#if WINDOWS`/null-check per call site. | Implemented |
| `AppWindowMetrics` | internal static class (`Platforms/Windows/AppWindowMetrics.cs`) | Windows-only window-chrome constants (`CaptionHeight`, `FrameInset`, `ChromeHeight`) and the `AppButtonBase`→`AppButtonWindows` style-injection workaround, extracted out of the shared `App.xaml.cs` (mobile windows are always fullscreen, so neither concept applies there). Called only under `#if WINDOWS` from `App.xaml.cs`. | Implemented |
| `PlatformServiceRegistration` | internal static class (`Platforms/Windows/PlatformServiceRegistration.cs`) | Windows-only startup wiring extracted out of the shared `MauiProgram.cs`: native title-bar chrome (`RestoreNativeTitleBar`/`ApplyTitleBarTheme`) and `HideCollectionViewScrollBars` (reaches into the WinUI `ListViewBase`'s `ScrollViewer` to hide the always-visible scrollbar chrome). Neither has a mobile equivalent. | Implemented |
| `AppPaths` / `IAppDirectoryProvider` | static class & interface (`Core/Platform/AppPaths.cs`) | Cross-platform directory provider standardizing application root (`AppDirectory`), data (`Data/`), diagnostic logs (`log/`), settings (`Settings/`), caches (`Cache/`), attachments (`Cache/attachments`), database (`Data/mostaqlk.db`), and preferences (`Settings/preferences.dat`) across Windows (`%LocalAppData%\MostaqlK`), macOS (`~/Library/Application Support/MostaqlK`), Linux (`~/.local/share/mostaqlk`), Android, and iOS. Cleans up legacy/buggy folders (`User Name/`, `com.companyname.mostaqlk`). | Implemented |
| `FilePreferences` | sealed class (`Core/Platform/FilePreferences.cs`) | Cross-platform, durable file-backed `IPreferences` implementation storing preferences as JSON in `Settings/preferences.dat`. Replaces default WinUI `ApplicationData.Current` storage to ensure full persistence across restarts for unpackaged and portable single-file executions. | Implemented |
| `NativeSplashScreen` | static class (`Platforms/Windows/NativeSplashScreen.cs`) | Instant native Win32 splash window running on a dedicated STA background thread with double-buffered GDI rendering displayed immediately upon launch (<20ms) until the main WinUI/MAUI window is created and activated. | Implemented |
| `SecretProtector` | static partial class (`Infrastructure/Security/SecretProtector.cs`) | Encrypted secret store abstraction with per-platform partial implementations: `SecretProtector.Windows.cs` using DPAPI (`DataProtectionScope.CurrentUser`) and `_SecretProtector.Mobile.cs` using AES-GCM and hardware-backed storage for Android/iOS. | Implemented |

## Core helpers

Location: `Core/`

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `Debouncer` | sealed class (`Core/Debouncer.cs`) | Shared cancel-and-restart debounce helper: `Debouncer(TimeSpan delay)` + `Schedule(Func<CancellationToken, Task>)` cancels any pending call and restarts the delay so only the most recent schedule fires. Used by `DebouncedEntry` (keystroke debounce) and `ProjectFeedViewModel` (pipeline auto-reload debounce) so neither hand-rolls its own `CancellationTokenSource` restart mechanic. Optional `SetDelay` keeps bindable `DebounceMilliseconds` in sync. | Implemented |
| `StringNormalization` | static class (`Core/Utilities/StringNormalization.cs`) | Single authoritative ground for Arabic string normalization: ASCII digit conversion (`ToAsciiDigits`), diacritics stripping (`StripDiacritics`), orthographic label folding (`NormalizeLabel`), and HTML cleaning (`CleanHtml`). Used across parsers, search, and linguistic formatters. | Implemented |
| `ArabicNameFormatter` | static class (`Core/Formatting/ArabicNameFormatter.cs`) | Authoritative single ground for client name formatting, Arabic definite article ("ال") prefix stripping (`StripArticle`), and client avatar initials extraction (`GetInitials`, `GetFirstLetter`). | Implemented |
| `ArabicProposalParser` | static class (`Core/Formatting/ArabicProposalParser.cs`) | Single ground for Arabic proposal parsing and canonical pluralization (`Format`: "0 عرض", "عرض واحد", "عرضان", "3-10 عروض", "11+ عرضاً"). | Implemented |
| `ArabicRelativeTime` | static class (`Core/Formatting/ArabicRelativeTime.cs`) | Single ground for relative time calculation (`Since`), day counts (`Days`), and scraper relative time parsing (`ParseRelativeNumber`). | Implemented |
| `BudgetFormatter` | static class (`Core/Formatting/BudgetFormatter.cs`) | Single ground for rendering raw scraped budget ranges into Arabic currency presentation format (`"2,500 - 5,500 ر.س"`). | Implemented |
| `PipelineTelemetryFormatter` | static class (`Core/Formatting/PipelineTelemetryFormatter.cs`) | Single ground for formatting pipeline worker states (`FormatWorkerState`), elapsed times (`FormatSeconds`), and telemetry duration suffixes (`FormatSecondsSuffix`). | Implemented |
| `TextTruncator` | static class (`Core/Formatting/TextTruncator.cs`) | Single ground for string truncation and ellipsis formatting (`Truncate`, `TruncateWords`). | Implemented |
| `AppRoute` / `AppRoutes` | readonly record struct & static class (`Core/Navigation/AppRoutes.cs`) | Branded value object and authoritative single ground for typed Shell navigation routes (`MainWindow`, `Settings`, `About`, `ProjectDetails(id)`), route names, query parameter keys, and thread-safe dispatchers (`NavigateAsync`). | Implemented |

## Platform Components

Location: `UI/PlatformComponents/<Unit>/<Unit>.cs` (+ `<Unit>.Windows.cs`)

| Unit | Base MAUI type | Purpose | Status |
|---|---|---|---|
| `AppButton` | `Button` | Shared button used across all features. | Implemented |
| `AppCard` | `Border` | Project-feed card surface; carries `IsUnread`/`IsRead` bindable state for the unread accent-border treatment. Colors (surface background, accent/read border) are set in code from `UpdateThemeColors()` and re-applied on `Application.RequestedThemeChanged`, so both the read and unread states have real light/dark parity instead of one hardcoded color pair. | Implemented |
| `AppEntry` | `Entry` | Shared text input (search box, settings forms). Base of the `DebouncedEntry`/`SearchInputField` inheritance chain. | Implemented |
| `DebouncedEntry` | `AppEntry` | Adds keystroke debouncing (`DebounceMilliseconds`, `DebouncedTextChanged`/`DebouncedCommand`) via the shared `Core/Debouncer` cancel-and-restart helper. | Implemented |
| `SearchInputField` | `DebouncedEntry` | Concrete search box (search icon + clear/"x" button via `ClearCommand`) bound to `ProjectFeedViewModel.SearchQuery`. The unit is the bare `Entry`: MAUI's `Entry` has no leading-icon slot and no `Padding`, so the mockups' `bg-slate-50 border border-slate-200 rounded-lg` box, the `left-4` magnifier `AppIcon` and the `pl-10 pr-4` insets are composed **around** it at the call site (see the header in `MainWindowPage.xaml`). The wrapper grid is forced `FlowDirection="LeftToRight"` because Tailwind's `left-4` is a physical edge while the page is RTL. | Implemented |
| `AppToggle` | `Switch` | Shared toggle switch (e.g. dark-mode switch, grouping enabled). Wired into `SettingsPanel.xaml` for the live dark-mode toggle, and reused by `AppSidebar's own dark-mode row so the toggle is functional (and stays in sync) from every page, not just Settings. | Implemented |
| `PressableEffect` | `Behavior`, split `PressableEffect.cs` + `PressableEffect.Windows.cs` + `_PressableEffect.Mobile.cs` (family-shared) + `PressableEffect.Android.cs`/`PressableEffect.iOS.cs` | Global Design System behavior adding scale (0.98)/opacity (0.8) press feedback to any interactive view (cross-platform, works for touch and mouse alike) plus, Windows-only, a theme-aware hover highlight + cursor. Android/iOS deliberately have NO hover stand-in (hover has no touch equivalent); instead both export a shared mobile-OS-family haptic "click" tick from `_PressableEffect.Mobile.cs` on touch-down, giving each platform family its own genuine native feel per `cross-platform-ui-conventions.md`'s "Native feel" rule. | Implemented |
| `PlatformSelect` | static helper | Not a UI unit itself — one of the two canonical exempt files (the other being `CurrentPlatform`) that hosts a compile-time `#if` ladder every other unit above/below is built on. | Implemented |
| `MotionPreferences` | static partial helper (`MotionPreferences.cs` + `MotionPreferences.Windows.cs`) | Reads the OS "reduce animations" accessibility preference. Shared shell exposes `IsReducedMotionRequested` and a `partial void ResolveReducedMotion(ref bool)` hook (default: motion allowed); the Windows partial reads `Windows.UI.ViewManagement.UISettings.AnimationsEnabled` — same partial-class convention as `SplitterHandle`/`AppButton`. Any unit with continuous/ambient animation must honour it — `PipelineRadar` drops its scanner rotation, worker breathing and pulses and replaces travel with fades, while still communicating every state change. | Implemented |
| `AppSidebar` | `ContentView` (`UI/PlatformComponents/AppSidebar/`) | Shared sidebar nav rail (logo, 5 nav items with `ActivePage` highlight, real `AppIcon` glyphs, unread badge via `NotificationCount`, "مشاريع مضافة اليوم" stat card via `StatValue`, dark-mode row) matching the sidebar markup common to all 4 design mockups. Used by all 4 pages (`MainWindowPage`, `SettingsPanel`, `AboutPage`, `ProjectDetailsPage`) — `MainWindowPage`'s previous inline duplicate nav rail was migrated to this unit. Laid out as a `Grid RowDefinitions="80,*,Auto"` mirroring the mockup's `flex flex-col` + `nav flex-1`, so the stat card and dark-mode row stay pinned to the bottom of the column; it was previously a `VerticalStackLayout` with a `VerticalOptions="Fill"` spacer, which a stack layout ignores, so those two floated up under the nav items. This is currently the **only** unit with full light/dark parity (`AppThemeBinding` for every surface/text colour plus theme-aware active-row colours in `AppSidebar.cs`, refreshed on `Application.RequestedThemeChanged`). | Implemented |
| `PipelineRadar` | `ContentView` (`UI/PlatformComponents/PipelineRadar/`) | "Lighthouse Radar": a single **state-driven** visualisation of the Discovery → Queue → Enrichment → Completion pipeline, not a bundle of independent ring animations. Three parts: `RadarPipelineState` (pure, MAUI-free model — project tokens with pipeline stages, smooth-damped queue/worker/hover values, pooled detection & completion pulses, 50ms stagger for bursts, reduced-motion fallbacks), `PipelineRadarDrawable` (`IDrawable`; paints that state only, no per-frame allocations), and `PipelineRadar.xaml(.cs)` (one frame ticker — a single committed `Animation` that advances the state and calls `Invalidate()`, parking itself once everything settles). Pipeline events (`GlobalAppStatusService.ProjectDiscovered` / `ProjectAssignedToWorker` / `ProjectRemovedFromQueue` / `WorkerStateChanged`) only move *targets*, so an update arriving mid-flight redirects the motion instead of resetting the radar. Interaction: per-ring pointer hit-testing with an overflowing hover data panel (discovery/queue/worker tooltips with interpolated numbers, 180ms fade + translate, interruptible via `CancellationTokenSource`) and click-to-focus a worker (others quieten, connector drawn toward its project). | Implemented |
| `AppIcon` | `ContentView` (wraps `Image`) | Shared icon unit (`Icon` bindable property, `AppIconGlyph` enum). Renders a pre-rasterized PNG icon (originally FontAwesome SVGs, baked to PNG at build time via `MauiImage`, with a pre-colored "_active" blue variant for the 5 sidebar nav icons) loaded via `ImageSource.FromFile` against an absolute path under `AppContext.BaseDirectory`. Used by `AppSidebar`; not yet applied to `ProjectCard`/`ProjectDetailsPage`/`SearchInputField` (only 6 of the enum's icons have real artwork today — the rest fall back to the "info" icon). **History:** originally implemented as a FontAwesome icon *font* (`Label` + codepoints), but that approach hit a genuine, unresolvable platform limitation — WinUI never loads runtime-referenced custom font files on this app's unpackaged Windows build (confirmed via debug logging that 3 independent font-loading fixes all executed correctly yet still rendered empty "tofu" boxes; a standalone browser test proved the `.ttf` files themselves were valid). Switched to real SVG-derived images instead; even then, MAUI's plain resource-name `Image.Source` string (`"icon_bell"`/`"icon_bell.svg"`) silently failed to resolve on this unpackaged build (same root-cause class as the font issue) — `ImageSource.FromFile` with an absolute path is the one approach confirmed to work end-to-end. **Onboarding follow-up:** even that resolved fine for static icons, but the onboarding page's Next button used to swap its icon at runtime on every step transition (AppIconGlyph.ChevronLeft <-> CircleCheck) via this same ImageSource.FromFile path, and doing that synchronously alongside several other bound properties updating at once caused the crash in docs/reports/onboarding-icon-crash-report.md. Fixed by pre-baking both icon variants as static images (scripts/generate_onboarding_icons.py) and toggling two static buttons by visibility (OnboardingPage.xaml) instead of re-resolving ImageSource at runtime. A second, unrelated gap surfaced next: both the Save and Next loading spinners use `Icon="Refresh" TextColor="White"`, which resolves to `icon_refresh_white.scale-200.png`, but only the base grey `icon_refresh.svg` ever existed - the white variant was never baked, so `ImageSource.FromFile` silently failed and the spinner rotation animation ran on an invisible image regardless of z-order. Fixed by extending `scripts/generate_onboarding_icons.py` to also bake `icon_refresh_white.svg`. A third gap surfaced next: even with the white icon PNGs in place, the chevron/checkmark icons set directly via `Button.ImageSource` stayed invisible on first paint on Windows (WinUI) and only appeared after some other layout pass was forced (e.g. clicking the button) - a known WinUI `Button.ImageSource` first-paint rendering gap, unrelated to missing files. Fixed by dropping `Button.ImageSource`/`ContentLayout` entirely and overlaying separate `InputTransparent` `Image` elements on top of plain text-only buttons, mirroring the `NextSpinnerIcon`/`SaveSpinnerIcon` overlay pattern that has always rendered correctly on first paint. A fourth gap surfaced next: the overlay Image elements also stayed invisible on first paint on Windows - the WinUI first-paint bug is not specific to Button.ImageSource, it affects plain XAML Image elements too. Fixed by replacing those two Image overlays with AppIcon itself (Icon=ChevronLeft/CircleCheck), since AppIcon sets its inner Image.Source from C# code-behind (its constructor) rather than XAML markup, which is the one approach confirmed to render correctly on first paint every time. A fifth, unrelated gap then surfaced: the two Next buttons still carried Padding="24,44,24,12" (44px top padding) left over from an earlier attempt, which pushed the centered button text down while the overlaid AppIcon centered on the full (taller) button cell, making the icon appear above the text instead of beside it. Fixed by changing Padding to "24,12,54,12" (normal vertical padding, extra right-side padding to make room for the icon). | Implemented |
| `PipelineDashboardPanel` | `ContentView` (`UI/PlatformComponents/PipelineDashboard/`) | The pipeline dashboard column in `MainWindowPage`: `PipelineRadar` at a readable size (140–260dp via its `Diameter`), then the discovery and queue summary cards, the three worker rows, and a drill-in block. **Why it exists:** the radar previously sat as a 56dp dial in the footer status bar, where it was too small to notice and — because the radar deliberately parks its ticker and fades its scanner once the pipeline settles — appeared to vanish entirely after a while. The panel gives every figure a permanent home, so pipeline state stays readable with no motion at all, which also makes the reduced-motion story honest. Laid out `RowDefinitions="Auto,*"` (header + `ScrollView` body) with the collapsed rail sharing the same cell, so collapsing animates only the panel width (240ms `CubicOut`, snapped under `MotionPreferences`) and swaps visibility instead of rebuilding layout. Collapsed state is a ~40dp **status rail**, never a full hide: it keeps the three worker dots and the backlog percentage on screen. `IsExpanded`/`ExpandedWidth` are two-way bindable (the latter driven live by `SplitterHandle`) and persisted under `pipeline_panel_is_expanded` / `pipeline_panel_width` — open on first run, remembered afterwards. Selection is shared with the dial: the panel turns the radar's own tooltip off (`ShowTooltip="False"`) and consumes `HoverChanged`/`FocusedWorkerChanged` instead, while clicking a worker row calls back into `PipelineRadar.FocusWorker` so row and segment always agree (unfocused rows quieten to 0.55 opacity, mirroring the dial's focus mode). Figures come from the radar's already-interpolated `DisplayedQueueCount`/`DisplayedUtilisation`, refreshed by one 250ms dispatcher timer that only touches text — all *motion* still belongs to the radar's single ticker. | Implemented |
| `SplitterHandle` | `ContentView` (`UI/PlatformComponents/SplitterHandle/`) | Drag-to-resize divider between the project feed and the pipeline dashboard: an 8dp grab strip around a 1dp rule (the same hairline as every other divider), with a `PanGestureRecognizer` driving a two-way `Value` and a `PointerGestureRecognizer` for the 140ms hover highlight. `Minimum`/`Maximum` clamp the drag so **each section keeps a minimum width and panning simply stops** instead of squeezing content — `MainWindowPage.OnRootSizeChanged` recomputes `Maximum` from the window width so the feed never drops below 520dp. `DragSign` exists because the resized section can sit on either side of the handle and the page is RTL, so "drag left grows the panel" is a call-site fact, not a global one. `SplitterHandle.Windows.cs` supplies the `SizeWestEast` cursor: WinUI's `UIElement.ProtectedCursor` is `protected` and MAUI's platform view is not ours to subclass, so it is set by reflection once the handler exists (best-effort — a failed lookup must never break the resize itself). First cursor manipulation and first drag-resize in the codebase. | Implemented |
| `PlatformImage` | `ContentView` (`UI/PlatformComponents/PlatformImage/PlatformImage.cs`) | Base unit generalizing the `PlatformSelect`/`_X.{Family}.cs` pattern to images/icons: bindable `WindowsSource`/`MobileSource`/`AndroidSource`/`IOSSource`/`MacCatalystSource`/`DefaultSource` (+ `Aspect`) resolved once via `PlatformSelect.For<ImageSource>()` and **memoized** in a private field, only re-resolved when a source property actually changes (never on layout/re-render). `AndroidSource`/`IOSSource` override `MobileSource` when set, mirroring `_PressableEffect.Mobile.cs`'s family-sharing convention. Fills a gap the built-in `MauiImage`/`Resources/Images` catalog doesn't cover (that pipeline only overrides by density/file-path per `TargetFramework`, not a bindable "compositionally different per platform" source). Base of the `PlatformImage` → `OnboardingStepImage` specialization chain. | Implemented |
| `OnboardingStepImage` | `PlatformImage` (`UI/PlatformComponents/PlatformImage/OnboardingStepImage.cs`) | **Specialization** of `PlatformImage` (mirrors `DebouncedEntry` → `SearchInputField`) for the Onboarding flow's step illustrations. Exposes `StepImageFileName` (bound to `OnboardingViewModel.CurrentIllustration`, a per-step file name) and forwards the resolved `ImageSource` to `WindowsSource`/`MobileSource`/`DefaultSource` identically for now, since no separate mobile-specific onboarding art exists yet — the per-platform resolution seam is in place for when it does. Replaces the plain `Image` previously used in `OnboardingPage.xaml`. | Implemented |
| `MostaqlLinkButton` | `ContentView` (`UI/PlatformComponents/MostaqlLinkButton/`) | Reusable "View on Mostaql" outline chip button (icon + label pair) with shared `PressableEffect` feedback. | Implemented |
| `LastScanStatus` | `ContentView` (`UI/PlatformComponents/LastScanStatus/`) | The one "آخر فحص: منذ لحظات" readout. **Why it exists:** the line was hand-written three times — the footer status bar built it in `ProjectFeedViewModel` off its own dispatcher timer, while the radar tooltip and the dashboard's discovery card each formatted their own variant ("قبل 4.2 ث") — so the three disagreed in wording *and* in value. Worse, the footer copy timed from the last time the feed reloaded from SQLite, not from a scan, which is how it could read "منذ دقيقة" while the header advertised a 30-second poll interval. The unit owns the label, the once-a-second re-wording (started/stopped from `OnHandlerChanged`, so a detached copy never leaves a timer running) and the only `Text` assignment, which it skips when unchanged. Hosts hand it `LastScanAt` — always `GlobalAppStatusService.LastScanCompletedAt`, the timestamp `PollService` writes on every cycle — plus `FontSize`/`TextColor`; the footer additionally sets `ShowRefresh` for the ↻ affordance (a transparent `Button` over the glyph, per the `AutomationId` gotcha) and `RefreshCommand`, which now also calls `IPollService.RequestCheckNow()` so the button causes a real scan instead of pretending one happened. `LabelAutomationId`/`RefreshAutomationId` land on the inner controls rather than the wrapper because the UI tests read `Projects_LastScanLabel`'s text. The wording itself lives in `LastScanText` (Display formatters), which `PipelineRadar`'s discovery tooltip and the panel's drill-in block also call. **Checking feedback (per `.repertoire/design/mvp/projects.html`'s `retry-btn`):** a new `IsChecking` bindable property — the footer binds it to `GlobalStatus.IsScanning`, the one flag `PollService` actually flips for the duration of a real cycle — swaps the label to "جاري الفحص..." and loops a continuous 360° `RotateToAsync` on the refresh glyph (self-re-issuing per cycle, since MAUI has no built-in infinite-repeat animation helper; cancelled via an incrementing token the moment `IsChecking` flips back) instead of the previous static "↻" glyph with zero feedback while `RequestCheckNow()`'s cycle was actually running. | Implemented |

`PipelineRadar` gained the hooks that panel needs: `Diameter` (the drawable derives all geometry from
the canvas rect, so the same unit serves a small dial and a large one), `ShowTooltip` (hosts that
render the same figures themselves turn the built-in hover panel off rather than stacking two
readouts), the `HoverChanged`/`FocusedWorkerChanged` events, `FocusWorker()`, and the read-only
`DisplayedQueueCount`/`DisplayedUtilisation`/`WorkerStateAt()`/`ProjectTitleOfWorker()` accessors.
Its home is now `PipelineDashboardPanel`, not the footer status bar.

Only `.Windows.cs` partials exist today (V1 = Windows-only). `.Android.cs` / `.iOS.cs` /
`.MacCatalyst.cs` partials are added per-unit only when V3 mobile work actually starts.

## Platform Concepts

Location: `UI/PlatformConcepts/<Unit>.cs`

| Unit | Mobile shape (future) | Windows shape (current) | Purpose | Status |
|---|---|---|---|---|
| `NavigationControl` | Bottom tabs | `Grid`-based side panel (nav rail + content), composed via `NavigationControl.Build(navRail, content)` from real page content/commands (see `MainWindowPage`). | Primary app navigation surface. Composes 2-column SidePanel on Desktop and 2-row BottomNav on Mobile. | Implemented |
| `ModalPresenter` | Bottom sheet | Native WinUI `ContentDialog` (confirmation) | Overlay/modal presentation. Windows V1 backs real confirmation dialogs via native `ContentDialog` (`ShowConfirmationAsync`: title/message/primary/secondary, optional remember-checkbox, RTL `FlowDirection`, `IsSecondary` only on explicit secondary pick). Used by `ConfirmationBox`/`ExitConfirmationBox`; must not depend on MAUI Shell/navigation (callers include `AppWindow.Closing` while the window may be tearing down). Split per the barrel convention: `ModalPresenter.cs` (shared shell, `partial`, no `#if`), `ModalPresenter.Windows.cs` (real `ContentDialog`), `_ModalPresenter.Mobile.cs` (shared TODO/safe-default stub, exported by `ModalPresenter.Android.cs`/`ModalPresenter.MaciOS.cs`). Mobile stays TODO until V3. | Implemented |
| `Drawer` | Swipe drawer / BottomSheet | Flyout (`FlyoutPage` stand-in) | Secondary/contextual side panel. Composes right-pinned Flyout on Desktop and bottom/end Drawer on Mobile. | Implemented |
| `ActionMenu` | Action sheet | Context menu (`MenuFlyout` stand-in) | Contextual list of actions. Exposes `ShowAsync(page, title, cancel, destruction, buttons)` resolving to `DisplayActionSheetAsync` across platforms. | Implemented |

Naming rule: names must stay neutral/abstract (e.g. `NavigationControl`, not `SidePanel` or
`BottomTabs`) so call sites never need renaming when mobile platforms ship in V3.

## Design System

Location: `UI/DesignSystem/`

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `DesignTokens` | static class | Brand colors, spacing scale, corner-radius tokens (Mostaql blue, Slate palette, light/dark). | Implemented |
| `ShimmerBox` | `ContentView` | Skeleton-loading placeholder; sweeping shimmer animation. | Implemented |
| `TruncatingLabel` | `Label` | Text truncation with `MaxChars` cap + `…` ellipsis. | Scaffold |
| `LabelWithSubText` | `ContentView`/`Label` | Canonical error display: `ExternalMessage` + `FixMessage`. Used for the feed's empty/error states and the details page's error state. | Implemented |
| `PressableEffect` | `Behavior<View>` | Adds elegant pressing (scale/opacity) and theme-aware hover (highlight/cursor) effects to any interactive view; when nested inside another pressable ancestor (e.g. a chip button inside a `ProjectCard`), coordinates with the ancestor's own `PressableEffect` (`SuppressForChildHover`/`ResumeAfterChildHover`) so the two highlights don't visually stack/overlap. Split per-platform: `PressableEffect.cs` (shared press feedback), `PressableEffect.Windows.cs` (hover/cursor), `_PressableEffect.Mobile.cs` (shared Android+iOS haptic tick, exported by `PressableEffect.Android.cs`/`.iOS.cs`) — see "Platform Components" table row above for the full rationale. | Implemented |
| `PressableBorder` | `Border` | A `Border` that adds its own dedicated `PressableEffect` instance in its constructor (same pattern as `AppCard`). Required for any `Style` that carries a `PressableEffect` via `Style.Behaviors` and gets applied to more than one element (e.g. one per `CollectionView` item) — MAUI's own docs warn stateful `Style.Behaviors` are a single shared instance reused by every consumer, which silently broke hover/press for all but the last element. Used by `OutlineChipButtonStyle`. | Implemented |
| `OutlineChipButtonStyle` | keyed `Style` (`TargetType="ds:PressableBorder"`) | Compact accent-tinted "outline chip" secondary-action button (icon + label pair) for actions too light for a full `AppButtonBase` — e.g. `ProjectCard`'s "عرض في مستقل" link-out button. Defined in `Resources/Styles/AppButtonStyle.xaml`. | Implemented |
| `NewRibbonBadge` | `Grid` | Diagonal, animated "new" corner ribbon overlaid on the physical-left corner of an unread `AppCard` (the app runs `FlowDirection="RightToLeft"`, so physical-left is the layout's `End` edge — opposite the card's own inline-start accent border). Bindable `IsActive` (bound to `ProjectCardViewModel.IsUnread`) shows/hides it and starts a looping translucent shimmer sweep across the ribbon (same sweep idea as `ShimmerBox`); honours `MotionPreferences.IsReducedMotionRequested` (static badge, no sweep) and re-colors for light/dark theme on `Application.RequestedThemeChanged`. Used by `ProjectCard.xaml`, layered in a wrapping `Grid` alongside the `AppCard`. | Implemented |
| `EnrichmentBadgeStyle` | static class (`UI/DesignSystem/Badges/EnrichmentBadgeStyle.cs`) | Single ground for project enrichment badge styling, colors, and iconography (text, background hex, foreground hex, and icon glyph) mapping `EnrichmentStatus` to visual tokens. | Implemented |
| `EnrichmentShimmerOverlay` | `ContentView` | Full-card overlay (not a skeleton placeholder like `ShimmerBox`) that continuously sweeps a soft, angled, gradient-edged light-reflection band across an already-visible, fully-readable `ProjectCard` while its project is still being enriched. Bindable `IsActive` (bound to `ProjectCardViewModel.IsEnriching`, true only for `EnrichmentStatus.Pending`) fades the overlay in/out and starts/stops a looping `Easing.SinInOut` `TranslateToAsync` pass (2.6s, reset off-screen between passes so the loop has no visible jump); `InputTransparent` throughout so it never blocks the card's own tap gesture. Honours `MotionPreferences.IsReducedMotionRequested` (overlay simply never activates — no static replacement needed, unlike `NewRibbonBadge`, since it carries no information of its own). Used by `ProjectCard.xaml`, layered in the same wrapping `Grid` as `NewRibbonBadge`. Pairs with `ProjectFeedViewModel`/`ProjectRepository.GetRecentAsync`'s enrichment-completion sort (`ORDER BY (enriched_at IS NULL) ASC, enriched_at DESC, discovered_at DESC`, sourced from `ProjectSummary.EnrichedAt`, itself copied from `ProjectDetails.EnrichedAt`/`DetailParser`'s completion timestamp) — a card only loses its shimmer and jumps to the top of the feed once enrichment has genuinely finished. | Implemented |
| `ConfirmationBox` | static class (`UI/DesignSystem/ConfirmationBox.cs`) | Shared **base** confirmation unit (the Design System "AppEntry"-equivalent of the confirmation hierarchy). Thin wording-only API over `ModalPresenter.ShowConfirmationAsync`: `ShowAsync(window, title, message, primaryText, secondaryText, rememberText?)` → `Result(IsSecondary, Remember)`. Also exposes `TryGetActiveNativeWindow()` for ViewModel call sites that do not already hold a native WinUI handle (e.g. Settings destructive actions). Split per the barrel convention: `ConfirmationBox.cs` (shared shell, `partial`, no `#if`), `ConfirmationBox.Windows.cs` (`TryGetActiveNativeWindow`'s real WinUI lookup), `_ConfirmationBox.Mobile.cs` (shared "no native window yet" stub, exported by `ConfirmationBox.Android.cs`/`ConfirmationBox.MaciOS.cs`). Base of the `ConfirmationBox` → `ExitConfirmationBox` specialization chain. | Implemented |
| `ExitConfirmationBox` | static class (`UI/DesignSystem/ExitConfirmationBox.cs`, single file) | **Specialization** of `ConfirmationBox` (mirroring `DebouncedEntry` → `SearchInputField`): supplies only the X-button exit-confirmation Arabic wording ("إغلاق التطبيق" / "الاستمرار في الخلفية" / "إغلاق نهائي" / remember checkbox) and maps `Result.IsSecondary` to `(CloseAction Action, bool Remember)`. Drop-in replacement for the former `CloseConfirmationDialog.ShowAsync(window)`; used from `MauiProgram`'s `AppWindow.Closing` handler with `CloseBehaviorService`. `ShowAsync(object? window)` needs no platform split of its own — it never touches a WinUI type directly, only forwards to `ConfirmationBox`, so it has no `#if` and no per-platform file. | Implemented |

Planned (folder placeholders exist, no C# units yet): `IconSystem/`, `Letterbox/`, `Stickers/`. The
`Letterbox` visual language (dark navy canvas, centered icon scene, sparkle accents, feature pill,
white bold headline with one green-accented phrase) has been designed and validated in HTML at
`.repertoire/design/mvp/onboarding.html` (5-step first-run onboarding carousel: background polling,
notifications, local archive, search, final CTA) — no auth/login screens exist or are planned, per
`.repertoire/.steering/v2/tech/identity-and-auth.md`. The MAUI `Letterbox` unit itself is still not
implemented in `UI/DesignSystem/`.

## Block Components & Layout Barrels

Location: `Features/Projects/Views/` and `Features/Projects/Views/Layouts/`

Composite block components and pages that delegate their active layout tree to platform-specific views using the View Barrel pattern (`PlatformSelect.For<Func<View>>()`).

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `ProjectCard` | `ContentView` (`Features/Projects/Views/ProjectCard.xaml(.cs)`) | Host view barrel shell for the project feed card. Dynamically resolves its visual layout via `PlatformSelect.For<Func<View>>()`. Inherits `BindingContext` (`ProjectCardViewModel`) down to active child layout views without duplicated ViewModel logic. | Implemented |
| `ProjectCardWindowsLayout` | `ContentView` (`Features/Projects/Views/Layouts/ProjectCardWindowsLayout.xaml(.cs)`) | Desktop layout for `ProjectCard`: 4-column metadata grid (`PublishTime`, `Budget`, `Delivery`, `Execution`, `Proposals`), client initials avatar and rating row, skill tag pills, external link button, `NewRibbonBadge`, and `EnrichmentShimmerOverlay`. | Implemented |
| `ProjectCardMobileLayout` | `ContentView` (`Features/Projects/Views/Layouts/ProjectCardMobileLayout.xaml(.cs)`) | Streamlined mobile layout for `ProjectCard`: compact title + enrichment badge, 2-line tail-truncated description, compact budget and unread status strip, `NewRibbonBadge`, and `EnrichmentShimmerOverlay`. | Implemented |
| `MainWindowPage` | `ContentPage` (`Features/Projects/Views/MainWindowPage.xaml(.cs)`) | Host page barrel shell for the main application workspace. Dynamically resolves its root layout (`MainWindowWindowsLayout` on Windows/Desktop vs. `MainWindowMobileLayout` on Mobile) via `PlatformSelect.For<Func<View>>()`, preserving notification flyout routing and feed lifecycle. | Implemented |
| `MainWindowWindowsLayout` | `ContentView` (`Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml(.cs)`) | Desktop 4-column layout: navigation `AppSidebar`, search & controls header, multi-item feed `CollectionView`, draggable `SplitterHandle`, interactive `PipelineDashboardPanel`, and `RecentNotificationsFlyout`. | Implemented |
| `MainWindowMobileLayout` | `ContentView` (`Features/Projects/Views/Layouts/MainWindowMobileLayout.xaml(.cs)`) | Mobile single-column feed layout: compact search bar with polling toggle and notifications bell, feed `CollectionView` with shimmer/empty states, and mobile `RecentNotificationsFlyout`. | Implemented |
| `ProjectDetailsPage` | `ContentPage` (`Features/Projects/Views/ProjectDetailsPage.xaml(.cs)`) | Host page barrel shell for project details. Dynamically loads `ProjectDetailsWindowsLayout` on Windows/Desktop and `ProjectDetailsMobileLayout` on Mobile. | Implemented |
| `ProjectDetailsWindowsLayout` | `ContentView` (`Features/Projects/Views/Layouts/ProjectDetailsWindowsLayout.xaml(.cs)`) | Desktop 2-column layout with `AppSidebar`, rich metadata card, skills chips, and attachments manager. | Implemented |
| `ProjectDetailsMobileLayout` | `ContentView` (`Features/Projects/Views/Layouts/ProjectDetailsMobileLayout.xaml(.cs)`) | Mobile single-column layout with top bar back navigation, title card, compact stats, skills, and attachment list. | Implemented |
| `SettingsPanel` | `ContentPage` (`Features/Settings/Views/SettingsPanel.xaml(.cs)`) | Host page barrel shell for app settings. Dynamically loads `SettingsPanelWindowsLayout` on Windows/Desktop and `SettingsPanelMobileLayout` on Mobile. | Implemented |
| `SettingsPanelWindowsLayout` | `ContentView` (`Features/Settings/Views/Layouts/SettingsPanelWindowsLayout.xaml(.cs)`) | Desktop 2-column settings layout with `AppSidebar` and full configuration cards. | Implemented |
| `SettingsPanelMobileLayout` | `ContentView` (`Features/Settings/Views/Layouts/SettingsPanelMobileLayout.xaml(.cs)`) | Mobile single-column settings layout with top bar back navigation and compact configuration cards. | Implemented |
| `AboutPage` | `ContentPage` (`Features/Projects/Views/AboutPage.xaml(.cs)`) | Host page barrel shell for the about view. Dynamically loads `AboutPageWindowsLayout` on Windows/Desktop and `AboutPageMobileLayout` on Mobile. | Implemented |
| `AboutPageWindowsLayout` | `ContentView` (`Features/Projects/Views/Layouts/AboutPageWindowsLayout.xaml(.cs)`) | Desktop 2-column about layout with `AppSidebar`, identity banner, quick facts, and roadmap list. | Implemented |
| `AboutPageMobileLayout` | `ContentView` (`Features/Projects/Views/Layouts/AboutPageMobileLayout.xaml(.cs)`) | Mobile single-column about layout with top bar back navigation and compact facts cards. | Implemented |

## Display formatters

Location: `Core/Formatting/`

Not UI units (no visual surface of their own), but shared presentation helpers every view-model
must reuse instead of hand-rolling its own string interpolation.

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `BudgetFormatter` | static class | Turns the raw scraped `projects.budget` text into the mockup's presentation form — `2,500 - 5,500 ر.س` (thousands separator, no decimals, low value first, Saudi Riyal suffix). Storage keeps the source string untouched. Used by `ProjectCardViewModel.Budget`. | Implemented |
| `ArabicRelativeTime` | static class | Arabic relative-time wording (`منذ 3 دقائق`, `منذ 8 ساعات`) and day-count pluralisation (`يوم واحد`/`يومان`/`7 أيام`/`20 يوم`). Used by `ProjectCardViewModel.PostedRelative` (fallback when `posted_relative` is empty) and `.Delivery`. | Implemented |
| `LastScanText` | static class | Single source of truth for the "آخر فحص" wording: `Elapsed`/`Labelled` produce `منذ لحظات` (<5s), `منذ 42 ثانية` (<1min), then delegate to `ArabicRelativeTime.Since` for minutes and above. The sub-minute band is deliberate — the poll interval is measured in seconds, so falling straight to minutes reads as a stalled pipeline. Used by the `LastScanStatus` unit, `PipelineRadar`'s discovery tooltip and `PipelineDashboardPanel`'s drill-in block; nothing may re-implement it. | Implemented |
| `SkillsFormatter` | static class (`Core/Formatting/SkillsFormatter.cs`) | Parses the scraped `projects.skills` / `ProjectSummary.SkillsText` string into a capped tag list (`ParseTags`, default max 6) and the compact chip line (`FormatDisplay` — each tag space-padded, joined by a triple-space gap). Used by `ProjectCardViewModel.SkillTags` / `.SkillsDisplay` / `.SkillItems` so none of them hand-roll the split/trim/take. | Implemented |

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
| `InteractionLogger` | static class | Structured rolling log sink under `Microsoft.Maui.Storage.FileSystem.AppDataDirectory/interaction-log.txt` (cross-platform MAUI API — on the unpackaged Windows build this is `%LocalAppData%\User Name\com.companyname.mostaqlk\Data\`, matching `MostaqlK.UITests`' `InteractionLogPath`). `Enter`/`Exit`/`Fault` bracket a traced command; `Mark(checkpoint, variant, data)` is the A/B checkpoint helper ("A"=branch taken/enter, "B"=other branch/skip) used to prove which code path actually executed instead of guessing from UI behaviour alone. All writes are best-effort and never throw. | Implemented |
| `TraceInteractionAttribute` / `TraceScope` | attribute + `IDisposable` scope | `[TraceInteraction("Name")]` documents that a command/handler (`TogglePolling`, `RefreshCommand`, `SaveCommand`, `SelectCommand`, `ResolveCommand`, sidebar nav handlers, ...) is traced; the method body wraps itself in `using var _ = TraceScope.Begin("Name", parameters)` (calling `MarkFaulted` on catch) so entry/exit/exceptions land in `InteractionLogger` for both humans and Appium tests to inspect. No IL weaving — the attribute is documentation, the `TraceScope` call is what actually logs. | Implemented |
| `ErrorOutcomeAttribute` (`Core/ErrorOutcomeAttribute.cs`) | attribute + `ErrorOutcome` enum | Companion to `[ErrorCode]`/`[ErrorCategory]`/`[NeitherContract]` in `Core/ErrorAttributes.cs` (see `.repertoire/.steering/base/tech/errors-handling.md`). `[ErrorOutcome(ErrorOutcome.Handled\|Ignored\|Rethrown, Label = "...")]` is applied to the method enclosing a catch block/`Result<T>.Err` arm to document what happens to the captured `DomainError`/exception — surfaced, deliberately swallowed (best-effort path), or rethrown/propagated. Purely documentation/tooling metadata, consumed by the static checker in `tools/ErrorHandlingAudit` (see `docs/error-handling-audit.md`). | Implemented |

`InteractionLogger.Failure(checkpoint, DomainError, data)` is the sink for a **failing
`Result<T>`**, and is mandatory for any code that observes `IsError` without propagating it to a
caller. Until it existed, only *exceptions* had anywhere to go: `DomainError` values were built
faithfully and then dropped, which is how two permanent failures produced no evidence at all —
`PollService`'s loop discarded whatever `PollOnceAsync` returned (so a listing endpoint answering
HTTP 403 on every single cycle looked exactly like an idle pipeline), and `EnrichmentWorker`
assigned `EnrichErrors.MaxAttemptsExhausted(...)` to a discard `_`. `Failure` records the code, both
the internal and the external message, the fix hint and the `Cause`. Publishing the failure to the
UI (`GlobalAppStatusService.NotifyScanFailed`) is a separate obligation, not a substitute for it.

Naming convention for AutomationIds (added incrementally as pages are catalogued in
`docs/ui-test-catalog.md`): `<Page>_<Element>`, e.g. `Sidebar_ProjectsButton`,
`Projects_SearchInput`, `Settings_SaveButton` — set directly on the exact control that owns the
`TapGestureRecognizer`/`Command`, never on a wrapping container, so `WindowsDriver.FindElementByAccessibilityId` maps 1:1 to the real hit-test target.

## Window close behavior

Location: `Services/CloseBehaviorService.cs`, `UI/PlatformConcepts/ModalPresenter.cs`,
`UI/DesignSystem/ConfirmationBox.cs`, `UI/DesignSystem/ExitConfirmationBox.cs`,
`MauiProgram.cs` (`AppWindow.Closing` wiring)

Avast-style "keep running in background": the native window's X button no longer exits the
process. `AppWindow.Closing` (unlike WinUI's plain, non-cancelable `Window.Closed`) is
intercepted and always cancelled first; `ExitConfirmationBox` → `ConfirmationBox` →
`ModalPresenter.ShowConfirmationAsync` shows a native WinUI `ContentDialog` (must run from
inside the WinUI `Closing` handler while the window may already be tearing down and cannot
depend on MAUI's own Shell/navigation stack) asking the user to either hide to the tray
(pipeline keeps running, `TrayIconService`'s tray icon stays put) or force-close for real. A
"remember my choice" checkbox persists the decision so every later X-button click repeats it
silently (idempotent, no dialog shown again); closing from the tray icon's own "Quit" menu
entry always exits directly, unaffected by this flow.

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `CloseAction` / `CloseBehaviorService` | enum + platform-neutral service (`Services/`) | `CloseAction.MinimizeToTray` \| `Exit`. The service only reads/writes `Preferences` (`close_behavior_remembered`, `close_behavior_action`) — no WinUI dependency, so the persisted decision itself stays testable. `GetRememberedAction()` returns `null` until the user checks "remember my choice" once. | Implemented |
| `ModalPresenter` / `ConfirmationBox` / `ExitConfirmationBox` | Platform Concept + Design System base + specialization | See Platform Concepts and Design System tables above. The former `Platforms/Windows/ConfirmationDialog` + `CloseConfirmationDialog` pair was re-homed here: mechanism in `ModalPresenter`, shared wording API in `ConfirmationBox`, exit wording + `CloseAction` mapping in `ExitConfirmationBox`. | Implemented |
| `PlatformServiceRegistration.ConfigureWindowsLifecycleEvents` | static method (`Platforms/Windows/PlatformServiceRegistration.cs`) | Hosts the tray-icon wiring, this whole close-to-tray confirmation flow, and native title-bar restoration — moved verbatim out of `MauiProgram.cs`'s previously-inline `#if WINDOWS` `ConfigureLifecycleEvents` block (~175 lines) so the shared composition-root file carries no in-body platform logic. Returns an `Action<MauiApp>` the caller invokes once, right after `builder.Build()`, to supply the app reference the lifecycle callbacks need. | Implemented |

`TrayIconService.RestoreRequested` (raised by the existing "Open" tray/menu action) is what brings
the window back once it was hidden via `MinimizeToTray` — `PlatformServiceRegistration.ConfigureWindowsLifecycleEvents`
subscribes to it (moved here from `MauiProgram.cs`, see above) and calls `AppWindow.Show()` + `Window.Activate()`.

Two ways to reset a remembered choice, both reading/writing the same
`close_behavior_remembered`/`close_behavior_action` keys via `CloseBehaviorService`:

- **In-app**: Settings page ("سلوك إغلاق التطبيق" card) — `SettingsViewModel.RememberCloseBehavior`
  reflects whether a choice is currently remembered; toggling it off calls
  `CloseBehaviorService.ForgetRememberedAction()` so the confirmation dialog is shown again on the
  next X-button click (there is nothing to set it back on from this row alone — only the dialog's
  own "remember my choice" checkbox can create a new remembered choice).
- **Offline (app closed)**: `tools/reset-close-behavior-preference.ps1 -ConfirmReset`. The
  unpackaged Windows build's `Preferences` implementation stores every key as flat JSON under a
  single container in one file (`%LOCALAPPDATA%\User Name\com.companyname.mostaqlk\Settings\preferences.dat`
  by default, overridable via `-PreferencesPath`) — there is no per-key file to just delete, so the
  script parses that JSON (via `JavaScriptSerializer`, since `ConvertFrom-Json -AsHashtable` needs
  PowerShell 6+ and this repo's other `tools/` scripts target Windows PowerShell 5.1) and removes
  just the two close-behavior keys before rewriting the file. Modeled on
  `tools/reset-local-database.ps1`'s param/`-ConfirmReset` shape.

## Notifications

Location: `Infrastructure/Notifications/`, `Services/NotificationDispatcher.cs`, `Services/NotificationGrouper.cs`, `Services/INotificationSender.cs`

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `INotificationSender` | interface (`Services/INotificationSender.cs`) | Platform-neutral boundary for native notification delivery (`SendAsync(IReadOnlyList<ProjectSummary>, CancellationToken)`). `NotificationDispatcher` depends only on this interface — never on a concrete Windows type — so a future Android/iOS sender can plug in without touching the grouper/dispatcher. V1 implementation: `WindowsToastSender` (registered in `MauiProgram` under `#if WINDOWS` as `AddSingleton<INotificationSender, WindowsToastSender>()`). | Implemented |
| `IFileRevealService` / `FileRevealService` | interface + static accessor (`Services/IFileRevealService.cs`, `Services/FileRevealService.cs`) | Platform-neutral boundary for revealing a local file in the OS file manager. Resolved once via `PlatformCapability<IFileRevealService>.Resolve(...)` (every platform has a shape — not `WindowsOnly`). Windows impl opens `explorer.exe /select,"path"` (byte-identical to the former inline call in `AttachmentItemViewModel.RevealAsync`); other platforms open the containing folder via `Launcher`. Call sites use `FileRevealService.Current.RevealAsync(path)` — no ad hoc `#if WINDOWS`. | Implemented |
| `AppLifecycleService` | singleton service | Tracks the high-level lifecycle state: `IsInBackground` (true when hidden to tray) and `IsReadyToNotify` (true once the main UI has appeared). Used by the notification pipeline to filter out early startup toasts and to ensure native popups only fire when the app is actually in the background, matching the "antivirus-style" requirement. | Implemented |
| `ToastAumidRegistrar` | static class (`#if WINDOWS`) | Fixes real toasts never appearing on this unpackaged (`WindowsPackageType=None`) build: `AppNotificationManager.Register()` alone only registers the COM activation server, it does not give the process an identity, so without an explicit AUMID + a Start Menu shortcut carrying that AUMID, Windows silently drops the toast instead of showing it. Idempotently calls `SetCurrentProcessExplicitAppUserModelID` and creates/repairs `%AppData%\Microsoft\Windows\Start Menu\Programs\MostaqlK.lnk` with the `System.AppUserModel.ID` property set to the constant `Aumid` ("MostaqlK.App"), via raw `IShellLinkW`/`IPropertyStore` COM interop. Called once from `WindowsToastSender.EnsureRegistered()` before `AppNotificationManager.Default.Register()`. Best-effort/never throws — logged via `InteractionLogger`. | Implemented |
| `WindowsToastSender` | class (`#if WINDOWS`, implements `INotificationSender`) | The "winToast-handler" — a dual-variation dispatcher that orchestrates between modern Windows App SDK notifications and robust WinRT fallbacks. Performs a one-time check on startup: if `AppNotificationManager.Default.Setting` is not `Unsupported`, it delegates all work to `WinAppSdkVariation`; otherwise, it falls back to `WinRtVariation`. Now also acts as a **notification filter** (app lifecycle awareness) and a **rich content provider**: every toast now supports **RTL (Arabic)** layout, includes full project details (Title, Summary, Description), and features an interactive **"عرض على مستقل" button** that opens the project directly in the default browser. Clicking the notification body itself restores the app from the tray and navigates to the specific `ProjectDetailsPage`. | Implemented |
| `IToastVariation` | interface | The "winToast-logic" abstraction — defines the contract for notification backends (`EnsureRegistered`, `SendAsync`). Backends are responsible for building the specific platform XML/Builder payloads including RTL markers and interactive actions. | Implemented |
| `WinAppSdkVariation` | class (`#if WINDOWS`) | "winAppSdk variation" — implementation using the modern `Microsoft.Windows.AppNotifications` API. Fixed "toast does not have view on mostaql button": `BuildGroupedToast` (used whenever more than one project is flushed at once — the normal case whenever notification grouping is enabled) never added the "عرض على مستقل" button at all, only `BuildIndividualToast` (`Count == 1`) did. Now links the button to the first project with a non-empty `Url` in the batch, using the same `openUrl` argument routing as the individual toast. Also implemented a **Title-only debug mode** (removing all other content) and diagnostic logging to investigate why action buttons are sometimes hidden by the OS. | Implemented |
| `WinRtVariation` | class (`#if WINDOWS`) | "WinRT-variation" — implementation using the robust `Windows.UI.Notifications` API. Same "toast does not have view on mostaql button" fix applied here: `BuildGroupedToastXml` (Count > 1) now also emits an `<actions>` block with the "عرض على مستقل" button linked to the first project with a non-empty `Url` in the batch, matching `BuildIndividualToastXml`. Also follows the same **Title-only debug mode** and length logging as the SDK variation for diagnostic parity. | Implemented |
| `ToastActivator` | static class (`#if WINDOWS`) | Fixes "buttons ARE NOT WORKING" for the toast **body** click when the app is running on the `WinRtVariation` (classic WinRT) path: unlike the modern Windows App SDK's `AppNotificationManager.NotificationInvoked`, a plain `Windows.UI.Notifications.ToastNotification` shown by an unpackaged Win32 app has **no built-in click event at all** — Windows instead requires a registered COM server implementing `INotificationActivationCallback`, whose CLSID is written to `HKCU\Software\Classes\CLSID\{clsid}\LocalServer32` and stamped on the Start Menu shortcut via `System.AppUserModel.ToastActivatorCLSID` (both done by `ToastAumidRegistrar`). Registers itself as a COM local server (`CoRegisterClassObject`) from `WinRtVariation.EnsureRegistered()`; on activation it parses the toast's `launch` argument (e.g. `projectId=123`) and restores the window from the tray + navigates to `ProjectDetailsPage`, mirroring `WinAppSdkVariation.OnNotificationInvoked`. As of the fix below, an `openUrl=<percent-encoded-url>` argument is also recognized and delegated to `NotificationUrlLauncher` — the "عرض على مستقل" button no longer uses OS-level `activationType='protocol'`. | Implemented |
| `NotificationUrlLauncher` | static class | Fixes "open into mostaql button does not work" being untraceable: both toast variations previously handed the "عرض على مستقل" button's URL launch entirely to the OS (`activationType='protocol'` in `WinRtVariation`, `AppNotificationButton.SetInvokeUri` in `WinAppSdkVariation`) — if that silently failed, nothing was ever logged, so the report couldn't be diagnosed. Both variations now route the button through their existing foreground/COM activation path (`openUrl` argument on `WinRtVariation`'s action / `AddArgument("openUrl", ...)` instead of `SetInvokeUri` on `WinAppSdkVariation`'s button) into this shared helper, which validates the URL and launches it itself via `Process.Start(UseShellExecute=true)`, logging the attempt and outcome (`invalid-url` / `process-started` / a `FAULT` on exception) under `<caller>.OpenUrl` in `interaction-log.txt`. Because `ToastActivator.ParseArguments` naively splits on `&`, the URL is `Uri.EscapeDataString`-encoded when embedded in `WinRtVariation`'s XML `arguments` and decoded back out in `ParseArguments`, so a URL's own `&`/`=` (query string) characters can't corrupt the split. | Implemented |
| `NotificationGrouper` | class | Buffers newly discovered projects and decides when to flush a batch to `WindowsToastSender` (immediate single-item bypass, end-of-minute, after-N-minutes, or after-N-count), instrumented with `InteractionLogger.Mark` checkpoints on every timer schedule/flush so a real run can be traced to confirm flushing actually happens. Verified live: `NotificationGrouper.Flush` → `NotificationDispatcher.HandleFlush` → `WindowsToastSender.SendAsync` all fired for real newly-discovered projects with no `FAULT` entries, and Windows' own notification-sources settings list registered `MostaqlK` as a toast sender, confirming the AUMID fix took effect. | Implemented |
| `NotificationItemViewModel` | class (`Features/Notifications/ViewModels/NotificationItemViewModel.cs`) | Presentation view-model wrapper for `ProjectSummary` in the notification center flyout, exposing dynamic `PostedRelative` calculation via `ArabicRelativeTime.Since` and bindable properties for title, unread status, and navigation. | Implemented |
| `RecentNotificationsFlyout` / `NotificationCenterViewModel` | View + ViewModel | Recent-notifications popover (sidebar "التنبيهات" entry, header bell button, and tray "Recent notifications" action all open the same `MainWindowPage.NotificationsFlyout`). Its `Border` previously set neither `BackgroundColor` nor `Stroke`, so it rendered fully transparent instead of a real menu/popover; now has an explicit opaque `BackgroundColor`/`Stroke`/rounded `StrokeShape` plus a header row and per-item padding. Clicking a row already navigated to `ProjectDetailsPage?projectId=...` via `OpenProjectCommand`/`OpenProjectAsync` (unchanged). Opening the flyout (not clicking an individual project card) now also calls `NotificationCenterViewModel.MarkAllAsSeen()` via `MainWindowPage.SetNotificationsFlyoutVisible`, resetting the unread badge every time the menu is opened. The header now has an explicit X `AppIcon(Close)` button (`RecentNotificationsFlyout.CloseRequested` -> `MainWindowPage.OnNotificationsFlyoutCloseRequested`) since there was previously no way to dismiss it other than re-clicking whatever opened it, plus a full-page transparent `NotificationsBackdrop` `BoxView` (shown/hidden 1:1 with the flyout) whose tap also closes it, giving it normal auto-dismiss-on-outside-click menu behavior. Each row is now context-aware of read state: `ProjectSummary.IsUnread` drives a `DataTrigger`-based tinted background, a small accent dot, and a bold title for unread items, falling back to the plain read style once `NotificationDispatcher.MarkHistoryAsRead()` (called from `MarkAllAsSeen`) flips it off — `ProjectSummary` has no `INotifyPropertyChanged`, so `MarkAllAsSeen` re-populates the `ObservableCollection` (`RefreshFromHistory`) to force the `CollectionView` to re-evaluate each row's style. | Implemented |

**Root cause of "no notification, ever, including while running in the background":** two
compounding bugs, both fixed together.

1. **`InteractionLogger` was entirely `[Conditional("DEBUG")]`/`#if DEBUG`-gated.** Every
   `Mark`/`Enter`/`Exit`/`Fault`/`Failure` call — including `WindowsToastSender.SendAsync`'s own
   `catch` block — was stripped out by the compiler at every call site in a **Release** build,
   i.e. the actual installed exe the user runs day to day. Whatever the real toast-delivery
   failure was, it had zero chance of ever being logged/diagnosed outside a DEBUG build. Fixed by
   removing the `Conditional`/`#if DEBUG` gates entirely (`Services/Diagnostics/InteractionLogger.cs`)
   — writes stay best-effort/never-throw, so this adds no crash risk, only a log line per event.
2. **Toast COM/AUMID registration was previously lazy, but now eager.** Registering the AUMID and Start Menu shortcut as early as possible in the app's startup path (see `App.xaml.cs`) removes the race condition where the first toast could fire before Windows knows who the sender is.

**Refactored to WinRT notifications for maximum reliability:**
The project was originally using the modern Windows App SDK `AppNotificationManager`, which is powerful but has strict requirements for unpackaged apps (requiring the "Singleton" MSIX package to be installed and initialized correctly). On many machines, this caused the API to report `Setting=Unsupported` even if the runtime was present.
We have refactored `WindowsToastSender` to use the built-in WinRT `ToastNotificationManager` instead. This is the same underlying API used by Python and other languages, and it is known to work reliably for plain `.exe` apps as long as they provide a matching Start Menu shortcut and AUMID (which `ToastAumidRegistrar` handles).

**"Notifications still not working, did you add logs to traceback?" — the log path itself was the
first bug, and reading it then revealed the true, environment-level root cause:**

- `InteractionLogger.ResolveLogFilePath()` previously resolved its directory via
  `Microsoft.Maui.Storage.FileSystem.AppDataDirectory`, which goes through WinRT's
  `ApplicationData` API. On this unpackaged (`WindowsPackageType=None`) build that path is not
  reliably discoverable — a live check (with the app actually running, `Get-Process MostaqlK`
  confirmed) found **no** `interaction-log.txt` anywhere under `%LOCALAPPDATA%`, `%APPDATA%`,
  `%LOCALAPPDATA%\Packages`, or the `%TEMP%\MostaqlK` fallback, meaning the log — even once it
  started writing real entries — was effectively unreadable/unfindable by anyone. Fixed: the log
  now always resolves to the fixed, well-known path **`%LocalAppData%\MostaqlK\interaction-log.txt`**
  regardless of packaging/identity.
- With that fixed and the app rebuilt/relaunched, the initial implementation revealed an environment-level limitation: the Windows App SDK `AppNotificationManager` reported `Unsupported` on machines without the correctly provisioned "Singleton" MSIX package. Since we are an unpackaged app, the auto-deployment of that package was blocked.
- **Solution:** We refactored the entire notification pipeline to use the standard WinRT `ToastNotificationManager`. This bypasses the App SDK runtime requirements entirely while maintaining the same native look and feel. The log now confirms successful registration and delivery using the process AUMID.
- The in-app fallback already works independently of the native toast: `NotificationDispatcher.HandleFlush`
  inserts every flushed batch into `RecentHistory` (driving the unread badge and the
  `RecentNotificationsFlyout`) *before* it ever calls `WindowsToastSender.SendAsync`, so the badge/flyout
  update regardless of whether the native toast succeeds. If native toasts are required, the two
  options are (a) install the Windows App SDK Runtime redistributable on the target machine so the
  Singleton package is present system-wide, or (b) change the app's deployment mode away from
  self-contained+unpackaged so `DeploymentManager.Initialize()` can deploy it automatically — neither
  was done here since both are deployment/environment decisions outside this fix's scope.

## Secrets & session cookie

Location: `Infrastructure/Security/`, `Infrastructure/Database/SecretRepository.cs`, `Services/CookieStore.cs`, `Infrastructure/Http/CookieJar.cs`

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `SecretProtector` | static class | Encrypts/decrypts small secrets before they touch SQLite. On Windows the key is derived by the OS from the current user account (DPAPI, `CurrentUser` scope, plus app-specific entropy), so the stored blob is useless on another machine/user and no key material lives beside the ciphertext; non-Windows falls back to AES-GCM under a machine+user-derived key (obfuscation only). `TryUnprotect` returns `null` rather than throwing on a foreign/corrupt blob. | Implemented |
| `ISecretRepository` / `SecretRepository` | repository | `app_secrets(key, value, updated_at)` key/value store, values always written through `SecretProtector`. The table is created idempotently by `SqliteConnectionFactory.EnsureSecretsTable` — deliberately **not** a `user_version` bump, which would make every already-installed V1 database throw `SchemaVersionMismatch`. | Implemented |
| `CookieStore` | singleton service | Owns the Mostaql session cookie end to end: validates an uploaded cookie file via `CookieJar.ParseFile`, persists it encrypted, keeps the decrypted header in memory, and installs itself as `CookieJar.SecureProvider` so `MostaqlScraper`/`AssetDownloadService` pick it up without knowing its origin. Initialized eagerly in `MauiProgram` so the first poll cycle is already authenticated. | Implemented |
| `CookieJar` | static class | Parses Netscape/curl exports and plain `name=value` / `a=1; b=2` DevTools copies into one `Cookie:` header. Resolution order: explicit path → encrypted store (`SecureProvider`) → **DEBUG-only** `MOSTAQL_COOKIE` / `MOSTAQL_COOKIE_FILE` / repo-root `cookies.txt` walk-up. Those file/env fallbacks are compiled out of Release (`DevelopmentFallbacksEnabled`), so a shipped build can only ever use the cookie the user uploaded in Settings. | Implemented |

The Settings screen's "ملف الجلسة (الكوكيز)" card is the user-facing entry point: a dashed
`PressableBorder` drop zone (hover highlight + press animation from `PressableEffect`, hand cursor)
with an `ActivityIndicator` while the file is parsed/encrypted and `DataTrigger`-driven
green/red state colours for saved/rejected. No new UI unit was introduced — it composes
`PressableBorder` plus stock MAUI primitives. The development-only note under it is bound to
`ShowDevelopmentCookieNote`, so it never renders in Release.

## Tray Icon

Location: `UI/TrayIcon/`

| Unit | Type | Purpose | Status |
|---|---|---|---|
| `TrayIconService` | class | Windows system-tray icon: `TrayIconState` mirrored live from `IPollService.StatusChanged`/`DiscoveryQueue.Count` + right-click menu wired to real commands (Open, Pause/Resume, Check now, Recent notifications, Settings, Quit). Native icon hosting via `Platforms/Windows/TrayIconNativeHost.cs` (`Shell_NotifyIcon`). Resolved in DI via `PlatformCapability<TrayIconService>.WindowsOnly(...)` (not unconditional construction) so the "null on mobile" answer is explicit and typed through the shared platform-capability utility; call sites already null-check/`?.` and no-op when absent. Windows behavior is unchanged. | Implemented |

`TrayIconNativeHost` originally exposed a public `HandleWindowMessage(uint, nint, nint)` meant to be
called from "the host window's message loop / subclassed WndProc", but nothing ever actually
wired it up — no `WndProc` subclass existed anywhere in the app, so `WM_TRAYICON` never reached it
and left/right-clicking the tray icon silently did nothing. Fixed by having the host install its
own subclass in its constructor via comctl32's `SetWindowSubclass` (chains safely onto the WinUI3
window's own WndProc rather than overwriting it via `SetWindowLongPtr(GWLP_WNDPROC)`), forwarding
`WM_TRAYICON` to the now-private `HandleWindowMessage` and everything else to `DefSubclassProc`;
`Dispose()` calls `RemoveWindowSubclass` alongside the existing `NIM_DELETE` teardown. The
`SUBCLASSPROC` delegate is kept as an instance field so the CLR cannot collect it while the native
subclass is still installed.

Two follow-up bugs found after that fix, both in `TrayIconNativeHost`:

- **Right-click opened the app instead of a menu.** `HandleWindowMessage` treated `WM_LBUTTONUP`
  and `WM_RBUTTONUP` identically (both ran "Open") — no actual popup menu existed. Fixed by adding
  `ShowContextMenu()`, a real native context menu (`CreatePopupMenu`/`AppendMenu`/`TrackPopupMenuEx`
  with `TPM_RETURNCMD`) built from `TrayIconService.MenuItems` and shown at the cursor position on
  right-click; left-click still runs "Open" directly, unchanged.
- **Icon still not showing / "cache problem".** The icon was identified only by `hWnd`+`uID`
  (`NIM_ADD`/`NIM_MODIFY`/`NIM_DELETE`). Since the app's `hWnd` is different on every relaunch,
  Explorer's own tray-icon cache (keyed by `hWnd`+`uID` for non-GUID icons) can leave a stale/ghost
  entry behind from a killed/crashed prior run, or fail to surface the current one, until Explorer
  restarts. Fixed by giving the icon a fixed `guidItem`/`NIF_GUID` (a static `Guid` constant) so its
  identity is stable across restarts, plus a defensive `NIM_DELETE` of that same GUID on startup to
  clear any stale entry left by a previous run before the fresh `NIM_ADD`.
- **Tray menu "Settings" did nothing.** `TrayIconService.OnSettings` navigated with the bare
  relative route `nameof(SettingsPanel)`, while every other call site in the app (`AppShell`,
  `AboutPage`, `MainWindowPage`, `ProjectDetailsPage`) uses the absolute `"//SettingsPanel"` route.
  A relative `GoToAsync` depends on the current page's own route stack, which a tray click (firing
  outside any page's context) cannot rely on, so navigation silently no-opped. Fixed to use the
  same absolute route, and to call the same window-restore step `OnOpen()` already runs, in case
  the window is currently hidden to the tray.
- **"Check now" (tray menu / footer refresh) silently did nothing while paused.** `PollService`'s
  loop guarded the *entire* cycle - both the regular timer tick and a manual `RequestCheckNow()`
  signal - behind the same `if (!_isPaused)` check, so a manual check-now request only ever got
  queued and then dropped once `_isPaused` was true; nothing in the UI reflected that it had been
  ignored. Fixed in `PollService.RunLoopAsync` by tracking which task of the `Task.WhenAny` actually
  completed and running the cycle whenever it was the check-now signal, regardless of `_isPaused` -
  a regular timer tick still honours the pause. Note: `SetPaused` itself was already working
  correctly (flips `_isPaused` immediately); it just has no dedicated visual feedback (no tray icon
  state / tooltip change on pause), which is expected today, not a bug.

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

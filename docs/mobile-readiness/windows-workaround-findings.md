## Finding 1: Button.ImageSource and ZIndex hacks for WinUI rendering bugs
- File: Features\Onboarding\Views\OnboardingPage.xaml:131-171
- Description: Explicitly avoids Button.ImageSource and plain Image elements because they stay invisible on first paint in WinUI. Uses AppIcon (code-behind source setting) and explicit ZIndex to guarantee paint order when IsVisible toggles, which can otherwise re-order native children on Windows.
- Suggested fix: Split into OnboardingPage.xaml (shared layout) and OnboardingPage.Windows.xaml (the hacky version) or isolate the button/icon behavior into a platform-aware AppButtonWithIcon unit that handles these quirks internally.

## Finding 2: Transparent Button overlay to fix Windows UI AutomationId bug
- File: UI\PlatformComponents\AppSidebar\AppSidebar.xaml:23-26, 68-71
- Description: Overlays an invisible Button on top of a Border specifically because Border+TapGestureRecognizer fails to surface AutomationId to the Windows UI Automation tree (bug dotnet/maui#4715). This makes sidebar rows unreachable to Appium/WinAppDriver on Windows.
- Suggested fix: The workaround should be encapsulated in a SidebarNavRow Platform Component with a .Windows.cs or .Windows.xaml partial to keep the shared XAML clean of "invisible button" hacks that might interfere with touch handling on mobile.

## Finding 3: PNG rasterization bypass for WinUI runtime font loading bug
- File: UI\PlatformComponents\AppIcon\AppIcon.cs:9-22
- Description: Component abandoned font-based icons in favor of pre-rasterized PNGs because WinUI's native text stack fails to load runtime-referenced custom font files in unpackaged builds.
- Suggested fix: Keep AppIcon.cs as the shared shell, but move the PNG-resolution logic into an internal helper or partial method that can use vector fonts on mobile (where they work and save space/memory) while keeping the PNG workaround limited to Windows.

## Finding 4: Shared ticker workaround for WinUI composition animation crash
- File: UI\DesignSystem\EnrichmentShimmerOverlay.cs:18-31
- Description: Implements a custom shared ticker (IDispatcherTimer based) to drive dozens of card animations because concurrent native WinUI composition animations caused a native access violation (combase.dll).
- Suggested fix: Move the ticker implementation details into a platform-specific helper. On mobile, native MAUI TranslateToAsync might be more efficient and stable than a manual 30fps C# timer.

## Finding 5: Hover coordination to fix WinUI PointerEntered geometry quirk
- File: UI\DesignSystem\PressableEffect.cs:22-38
- Description: Implements a "static hover coordination" system where parents suppress their hover highlights if a descendant is hovered. This is a workaround for WinUI's PointerEntered firing on every element in the visual tree under the cursor, regardless of geometry/layering.
- Suggested fix: The HoverCoordinator logic should be abstracted into a platform-aware service or hidden behind a partial class. Mobile targets (touch-first) don't need this logic at all and shouldn't pay the overhead of tracking the "last hovered component".

## Finding 6: Windows-specific window metrics and style-override workarounds
- File: App.xaml.cs:16-26, 40-52
- Description: Hardcoded constants for Windows Caption Height and Frame Insets (8px adjustment for Windows 11). Also applies styles in code-behind via #if WINDOWS to bypass ResourceDictionary.Source runtime resolution crashes in WinUI.
- Suggested fix: Move metrics to a IPlatformMetrics service. Move the Windows style overrides into a separate Platforms\Windows\App.Styles.xaml or similar that is only merged on that platform.

## Finding 7: WinUI-specific ScrollBar suppression in shared startup
- File: MauiProgram.cs:114-144
- Description: HideCollectionViewScrollBars is implemented as an #if WINDOWS block in the shared MauiProgram.cs. It directly manipulates WinUI's ListViewBase and ScrollViewer handlers.
- Suggested fix: Move this handler mapping into Platforms\Windows\PlatformService.cs or a similar platform-specific initialization hook to keep the shared startup logic clean.

## Finding 8: Hardcoded Windows Chrome User-Agent for HttpClient
- File: MauiProgram.cs:204-205
- Description: Hardcodes a Windows 10 Chrome User-Agent for all HttpClient calls to bypass bot detection on Mostaql. This will be incorrect when running on Android.
- Suggested fix: Use an IUserAgentProvider that provides a platform-appropriate browser string (or a randomized mobile one for Android).

## Resolution status (windows-workaround-isolation plan, all 8 items closed)

- **Finding 1 [DONE - reclassified]**: On inspection, the Button/AppIcon ZIndex overlay pattern has zero platform-conditional code (AppIcon.cs is itself confirmed cross-platform-safe) - safe to ship unmodified to Android. Documented in `OnboardingPage.xaml`'s comment instead of a risky rewrite.
- **Finding 2 [DONE - reclassified]**: The transparent-Button-over-Border AutomationId workaround is plain, portable MAUI composition with no platform branching - AutomationId resolves fine on Android's accessibility tree too. Documented in `AppSidebar.xaml`'s comment.
- **Finding 3 [DONE - reclassified]**: `AppIcon.cs`'s PNG-rasterization strategy has no `#if WINDOWS`/platform-conditional code - `Image`/`MauiImage` PNG rendering is standard cross-platform MAUI. Documented in `AppIcon.cs`'s XML doc comment.
- **Finding 4 [DONE - reclassified]**: `EnrichmentShimmerOverlay`'s shared-ticker fix is built entirely on `IDispatcherTimer` (MAUI's cross-platform timer abstraction), no WinUI-specific API. Documented in the class's XML remarks.
- **Finding 5 [DONE - split]**: Hover-highlight/cross-hover-coordination/cursor logic moved into `PressableEffect.Windows.cs`; shared `PressableEffect.cs` keeps only the already-cross-platform press/scale/opacity feedback. New `_PressableEffect.Mobile.cs` (shared Android+iOS haptic-tick) plus `PressableEffect.Android.cs`/`PressableEffect.iOS.cs` give mobile its own genuine native press feel instead of a stripped-down hover design.
- **Finding 6 [DONE - extracted]**: Window metrics (`WindowsCaptionHeight`/`WindowsFrameInset`) and the `AppButtonBase` style-injection moved out of `App.xaml.cs` into new `Platforms/Windows/AppWindowMetrics.cs`.
- **Finding 7 [DONE - extracted]**: `HideCollectionViewScrollBars` (plus the native title-bar management block) moved out of `MauiProgram.cs` into new `Platforms/Windows/PlatformServiceRegistration.cs`.
- **Finding 8 [DONE - reclassified]**: The hardcoded User-Agent impersonates a Windows browser for the SCRAPED SITE's bot filter - it describes the target site's view, not this app's host OS, so it is intentionally identical on every platform. Documented in `MauiProgram.cs` instead of extracted into an `IUserAgentProvider`.

See `.junie/plans/windows-workaround-isolation.md` for the full delivery-step history and
`.repertoire/.steering/v1/tech/cross-platform-ui-conventions.md` ("Isolating Windows-bug
workarounds" / "Native feel" / "OS-family-shared implementations") for the conventions this
resolution established for future audits.

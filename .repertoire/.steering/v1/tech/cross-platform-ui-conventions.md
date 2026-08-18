# Cross-Platform UI Conventions

[← Back to V1 Tech](./README.md)

> **Version scope:** V1 defines and applies this convention for Windows only. The naming and folder layout are chosen so Android/iOS/macOS (V3+) plug in without renaming anything already shipped.

## Purpose

MostaqlK's UI is built once against **conventional, platform-neutral names**, not against "the Windows control" or "the Android control." Two distinct problems are handled by two distinct mechanisms:

1. **Same-shape components that need per-platform tweaks** (padding, corner radius, native handler mapping) — e.g. a button looks and behaves the same everywhere, just needs small native adjustments.
2. **Same-concept components that are structurally different per platform** — e.g. primary navigation is a bottom tab bar on mobile but a side panel on desktop. These are not the same control with different styling; they are different controls that serve the same conceptual role in the app.
3. **Composite block components that host distinct visual layout trees per platform** — e.g. a `ProjectCard` or `MainWindowPage` that requires a 4-column multi-pane layout on desktop but a streamlined single-column feed or compact card layout on mobile.

Both mechanisms live under `UI/` or within vertical feature slices (`Features/*/Views/`), in clean structure matching their responsibilities.

## Mechanism 1 — `PlatformComponents/` (same shape, per-OS tweaks)

Uses C#'s native multi-targeting file convention: a shared partial class plus one partial file per OS. The build system — not a runtime lookup — picks the right file for the current target framework.

```text
UI/PlatformComponents/
├── AppButton/
│   ├── AppButton.cs             # public partial class AppButton : Button — shared API/logic
│   ├── AppButton.Windows.cs     # partial class AppButton — Windows-only tweaks (V1 scope)
│   └── AppButton.Android.cs     # added only when V3 actually needs it
├── AppCard/
├── AppEntry/
└── AppToggle/
```

Style overrides for these follow the same shared-base + platform-override split, using MAUI's own merge mechanism (`Style.BasedOn`) instead of a custom "flatten" function:

```text
Resources/Styles/AppButtonStyle.xaml            # <Style x:Key="AppButtonBase" ...> shared base
Platforms/Windows/Styles/AppButtonStyle.Windows.xaml   # BasedOn="{StaticResource AppButtonBase}"
```

## Mechanism 2 — `PlatformConcepts/` (same concept, different shape per platform)

Used when the platform-idiomatic control for a concept is a **different type of widget**, not a styled variant of the same one. Each file exports one conventional name; internally it resolves to the concrete per-platform implementation via `PlatformSelect`.

```text
UI/PlatformConcepts/
├── NavigationControl.cs   # primary navigation: BottomTabs (mobile) vs SidePanel (desktop)
├── ModalPresenter.cs      # shared partial shell (+ .Windows.cs, .Android.cs, ...) for overlay/modal surface
├── Drawer.cs              # secondary/contextual panel: SwipeDrawer (mobile) vs Flyout (desktop)
└── ActionMenu.cs          # action list: ActionSheet (mobile) vs ContextMenu (desktop)
```

Example resolution:

```csharp
// UI/PlatformConcepts/NavigationControl.cs
public static class NavigationControl
{
    public static readonly Func<View> Current = PlatformSelect.For<Func<View>>(
        android:     () => new BottomTabs(),
        ios:         () => new BottomTabs(),
        windows:     () => new SidePanel(),
        macCatalyst: () => new SidePanel()
    );
}
```

`PlatformSelect.For<T>` is a small static helper (`UI/PlatformComponents/PlatformSelect.cs`) used for the rare runtime (non-XAML-bindable) case. For XAML-declared values, MAUI's built-in `OnPlatform`/`OnIdiom` markup extensions cover the same need without a custom helper:

```xml
<Button HeightRequest="{OnPlatform Android=48, iOS=44, WinUI=36}" />
```

## Mechanism 3 — Block & View Layout Swapping (Composite block components)

Used when high-level UI blocks (e.g. `ProjectCard`) or pages (e.g. `MainWindowPage`) require fundamentally distinct layout hierarchies (DOM structures) on desktop vs. mobile rather than inline visibility toggles or conditional styling.

In this pattern:
1. The host component or page is a lightweight shell (`ContentView` or `ContentPage`).
2. The shell delegates instantiation of its active visual tree to platform-specific layout views via `PlatformSelect.For<Func<View>>()`.
3. Child layout views inherit the `BindingContext` from the host shell, allowing existing ViewModels (`ProjectCardViewModel`, `ProjectFeedViewModel`) to bind without change.
4. Platform-specific layouts reside in a `Layouts/` subfolder adjacent to the host view.

```text
Features/Projects/Views/
├── ProjectCard.xaml(.cs)                     # Host ContentView shell
├── Layouts/
│   ├── ProjectCardWindowsLayout.xaml(.cs)    # Full-featured 4-column desktop layout
│   └── ProjectCardMobileLayout.xaml(.cs)     # Streamlined mobile layout (title + description)
```

Example host implementation:

```csharp
public partial class ProjectCard : ContentView
{
    public ProjectCard()
    {
        InitializeComponent();
        var layoutFactory = PlatformSelect.For<Func<View>>(
            windows: () => new ProjectCardWindowsLayout(),
            android: () => new ProjectCardMobileLayout(),
            ios: () => new ProjectCardMobileLayout(),
            macCatalyst: () => new ProjectCardWindowsLayout()
        );
        Content = layoutFactory();
    }
}
```

## Naming rule: neutral, abstract names — never a platform's native term

Every entry in `PlatformConcepts/` MUST be named after the **conceptual role** the component plays in the app, not after either platform's native widget name. This avoids renaming call sites when a second platform ships, and avoids implying one platform's vocabulary is canonical.

| Conceptual name | Windows (V1) | Mobile (V3, future) | Why not name it after either platform |
|---|---|---|---|
| `NavigationControl` | `SidePanel` | `BottomTabs` | Neither "SidePanel" nor "BottomTabs" describes the other platform's shape |
| `ModalPresenter` | `Dialog` / `Popup` | `BottomSheet` | "Dialog" reads wrong once BottomSheet exists; "BottomSheet" reads wrong on desktop |
| `Drawer` | `Flyout` | `SwipeDrawer` | Same concept, incompatible native names |
| `ActionMenu` | `ContextMenu` | `ActionSheet` | Same concept, incompatible native names |

Rejected alternatives (see prior discussion) and why:
- **Desktop-term-first** (e.g. calling it `SidePanel` everywhere) was rejected — it would force a rename of every call site once Android/iOS ship in V3.
- **Whichever platform ships first, per component** was rejected — produces an inconsistent, undocumented mix of desktop- and mobile-flavored names over time.

## Decision guide: which mechanism does a new component need?

- If the control is the *same shape* on every platform and only needs native tuning (padding, radius, handler mapping) → `PlatformComponents/` (partial classes + `Style.BasedOn`).
- If the *idiomatic shape itself* differs per platform for the same conceptual role → `PlatformConcepts/` (neutral name + `PlatformSelect`/`OnPlatform`).
- If unsure, default to `PlatformComponents/` and only promote to `PlatformConcepts/` once a second platform genuinely needs a structurally different control.

## V1 reality check

V1 targets Windows only. Only the Windows-side implementation of each `PlatformConcepts/` entry needs to exist now (e.g. `NavigationControl` resolving to `SidePanel`); mobile branches (`BottomTabs`, etc.) are added only when Android/iOS work actually starts, per the "avoid empty/premature folders" rule in [`structure.md`](../../base/structure.md).

## Isolating Windows-bug workarounds (not just Windows *features*)

Mechanism 1's `.Windows.cs` split isn't only for genuine per-OS *tuning* — it also applies whenever code exists **only to work around a Windows/WinUI-specific rendering bug, crash, or automation quirk**, not because the behavior is conceptually Windows-only. The distinguishing question: *"if this exact WinUI bug didn't exist, would this code exist at all?"* If the answer is no, the workaround is Windows-only code and must live in `X.Windows.cs`/`X.Windows.xaml`, never inline in the shared file behind an `#if WINDOWS` block or, worse, unconditionally.

This matters because bug-workaround code is easy to miss during a "what's platform-specific" audit — unlike a real per-OS API call, it often *compiles fine on every target* (e.g. a `ZIndex` tweak, a custom dispatcher-timer ticker, a "suppress parent hover" coordinator) while still only being *necessary* on Windows. Left inline, it silently ships to Android/iOS, adding complexity/overhead for a bug those platforms never had.

**Rule:** when you find code whose comment/intent is "this exists because WinUI does X wrong," treat it exactly like a `.Windows.cs` case:

```text
UI/DesignSystem/
├── PressableEffect.cs          # shared bindable properties, gesture wiring, platform-neutral seam
├── PressableEffect.Windows.cs  # Windows-only: hover highlight, cursor, hover-coordination workaround
└── PressableEffect.Android.cs  # Android-only: touch press feedback (added when the feature/fix is built)
```

- **Preserve the "why" comment** — move it into the `.Windows.cs` file verbatim (or lightly trimmed), don't delete it. Future maintainers need to know *why* the workaround exists, not just that it does.
- **`X.cross.cs` marker file**: if extracting the Windows-only piece would leave the shared `X.cs` file with essentially no code (just a namespace/using and an empty partial class declaration), add a trivial `X.cross.cs` instead of leaving the "shared" concept implicit — it makes the platform split visible at a glance in the file tree even when there's nothing to share yet beyond the type declaration itself. Do **not** add a `.cross.cs` file when the shared file already has substantial real shared logic (bindable properties, gesture wiring, etc.) — the shared `.cs` file itself already serves that purpose.
- **XAML-embedded workarounds** (a hack living directly in a page's markup, not in a reusable component) can't be split the way C# partials can. Extract the workaround into its own small internal component first (giving it a `.cs`/`.Windows.cs` code-behind pair), then consume that component from the page's XAML — the page itself stays platform-neutral.

## Native feel, not just "no crash on this platform"

`PlatformSelect.For<T>()` exists to let **one conceptual entry point resolve to genuinely different, native-feeling implementations per platform** — not merely to null out a branch that doesn't apply. When a shared interactive behavior (e.g. `PressableEffect`) is really only designed around one platform's interaction model (hover-first, desktop-cursor-first) and the "other platform" branch is just that design with the unsupported parts stripped out, that is **not** cross-platform-ready — it's one platform's feel wearing a second platform's label.

Concretely: hover (`PointerEntered`/`PointerExited`) is a desktop/cursor concept with no equivalent on touch — a touch platform's press feedback should be designed from "what does a native press feel like here" (e.g. scale/opacity change on touch-down, ripple), not derived by deleting the hover half of a desktop design. Each platform branch under a `PlatformComponents/`-style split should be authored to feel native on that platform, matching the "same shape, per-OS tweaks" mechanism's intent — this is more than skin-deep styling when the interaction model itself differs.

### OS-family-shared implementations: `_X.{Family}.cs`

Some behavior is genuinely shared by an entire *class* of platforms, not the whole app and not just one OS — e.g. Android and iOS both want the same touch-native tactile feedback (a haptic tick on press) that a mouse-driven Windows app should not get; a future macOS/Windows "desktop" pairing could similarly want shared mouse/trackpad-hover logic. For this case, use a leading-underscore, family-named file instead of duplicating the implementation into every platform-suffixed file:

```text
UI/DesignSystem/
├── PressableEffect.cs               # shared bindable properties, gesture wiring, platform-neutral seams
├── PressableEffect.Windows.cs       # Windows-only: hover highlight, cursor
├── _PressableEffect.Mobile.cs       # shared by Android + iOS: haptic press feedback (no platform APIs of its own)
├── PressableEffect.Android.cs       # Android: exports _PressableEffect.Mobile.cs's behavior
└── PressableEffect.iOS.cs           # iOS: exports _PressableEffect.Mobile.cs's behavior
```

Rules for this pattern:
- **Naming**: `_X.{Family}.cs` (leading underscore, family name like `Mobile`/`Desktop` — NOT a real `TargetPlatformIdentifier` such as `Android`/`iOS`/`Windows`). The underscore signals "this is a shared-implementation helper file, not something the SDK auto-selects by TargetFramework."
- **Compiles everywhere**: because `Mobile`/`Desktop` aren't recognized platform suffixes, the .NET SDK's automatic per-TFM file exclusion does **not** apply to this file — it compiles for every target, including ones that never call into it. It must therefore contain **zero platform-specific APIs**; only plain, portable C# (or MAUI cross-platform Essentials APIs, e.g. `HapticFeedback`) that is safe to have present-but-unused on a platform that never invokes it.
- **Exporting**: each real platform-suffixed file (`X.Android.cs`, `X.iOS.cs`, etc.) that belongs to the family implements its partial method(s) as a one-line call into the shared family method — it "exports" the shared behavior rather than re-implementing it. If a platform in the family later needs a genuine per-OS nuance on top, add it directly in that platform's own file after the shared call.
- **When to use vs. duplicate**: only introduce a family-shared file once a second platform in the family actually needs the *identical* behavior (mirrors the "avoid empty/premature folders" and "promote once a second platform genuinely needs it" rules elsewhere in this doc) — don't pre-emptively create `_X.Mobile.cs` for a behavior only one mobile platform currently has.

### Before any cross-platform refactor

Before splitting/refactoring a component for multi-platform support, check current best practices (the .NET MAUI multi-targeting file-suffix mechanism and platform API surface both evolve between SDK releases) and re-confirm alignment with [`base/structure.md`](../../base/structure.md) and [`base/product/README.md`](../../base/product/README.md)/[`base/tech/README.md`](../../base/tech/README.md) — don't rely purely on memorized conventions for a fast-moving area of the SDK.

## Rule: no in-body `#if PLATFORM` outside the canonical mapping utilities

A **shared** file — anything not suffixed `.Windows.cs`/`.Android.cs`/`.iOS.cs`/`.MaciOS.cs`/`.MacCatalyst.cs` and not itself living under `Platforms/{Platform}/` — must **never** contain `#if WINDOWS`/`#if ANDROID`/`#if IOS` in its body. If a shared class needs platform-specific behavior, split it into the shared shell (`X.cs`, `partial`, no `#if`) plus one file per platform (`X.Windows.cs`, `X.Android.cs`, …), using a `partial`/`private static partial` method as the seam — never an inline `#if` block. This is exactly the anti-pattern this refactor eliminated from `ModalPresenter`, `ConfirmationBox`, `ExitConfirmationBox`, and `MauiProgram.cs`'s `ConfigureLifecycleEvents` wiring (moved to `Platforms/Windows/PlatformServiceRegistration.cs`).

**The only two exemptions** — both are the canonical, single-purpose compile-time switches every other mapping utility in the codebase is built on top of, not app logic with a platform branch mixed in:
- **`Core/Platform/CurrentPlatform.cs`** — the one place `AppPlatform.Current` is assigned via an `#if WINDOWS`/`#elif ANDROID`/`#elif IOS` ladder.
- **`UI/PlatformComponents/PlatformSelect.cs`** — `PlatformSelect.For<T>()`'s own `#if ANDROID`/`#elif IOS`/`#elif WINDOWS`/`#elif MACCATALYST` ladder, which resolves the platform value handed to every `PlatformConcepts/` entry (Mechanism 2). Same shape/justification as `CurrentPlatform.cs`: one canonical switch, not a mixed-concern class.

A single-line call from a shared composition-root file (`MauiProgram.cs`, `App.xaml.cs`) into an **already** Windows-only class (e.g. `AppWindowMetrics.ChromeHeight`, `WindowsToastSender.EnsureRegisteredEagerly()`) wrapped in its own `#if WINDOWS` is tolerated — it is a call-site guard, not platform logic living inline; anything larger than one line must be extracted into the Windows-only class first (see `PlatformServiceRegistration.ConfigureWindowsLifecycleEvents`, extracted from a ~175-line inline block for exactly this reason).

**Self-check** (run after any refactor touching platform code — the only hits should be the two exemptions above, plus comment-only mentions):

```bash
grep -rn "#if WINDOWS\|#if ANDROID\|#if IOS" --include=*.cs
```

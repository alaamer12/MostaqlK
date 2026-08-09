# Cross-Platform UI Conventions

[← Back to V1 Tech](./README.md)

> **Version scope:** V1 defines and applies this convention for Windows only. The naming and folder layout are chosen so Android/iOS/macOS (V3+) plug in without renaming anything already shipped.

## Purpose

MostaqlK's UI is built once against **conventional, platform-neutral names**, not against "the Windows control" or "the Android control." Two distinct problems are handled by two distinct mechanisms:

1. **Same-shape components that need per-platform tweaks** (padding, corner radius, native handler mapping) — e.g. a button looks and behaves the same everywhere, just needs small native adjustments.
2. **Same-concept components that are structurally different per platform** — e.g. primary navigation is a bottom tab bar on mobile but a side panel on desktop. These are not the same control with different styling; they are different controls that serve the same conceptual role in the app.

Both mechanisms live under `UI/`, but in separate folders, because they solve different problems.

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
├── ModalPresenter.cs      # overlay/modal surface: BottomSheet (mobile) vs Dialog/Popup (desktop)
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

---
name: senior-refactor
description: Universal multi-platform refactoring skill to decouple codebases across TypeScript/React Native, Go, Rust, Flutter, and C#/.NET. Eliminates inline preprocessor directives (#if PLATFORM) and runtime OS switch sprawl, establishes 3-tier unit hierarchies (UNITS.md), and swaps view layouts across desktop and mobile. MUST BE USED whenever refactoring client code for cross-platform readiness, resolving platform coupling, splitting partial classes or build tags by OS family, or converting ad-hoc components into base/specialization units. Examples: "refactor this to be platform independent", "eliminate #if WINDOWS", "split into platform files", "extract base and specialization unit", "implement view barrel layout swapping".
---

# Senior Refactor: Zero-Coupling & Universal Multi-Platform Architecture

A comprehensive, language-agnostic skill for transforming tightly-coupled, platform-locked client codebases into clean, decoupled, multi-platform architectures without breaking existing functionality.

## Core Architectural Invariants

1. **Elimination of Direct & Indirect Platform Directives (`#if PLATFORM` / Runtime OS Checks)**: No compile-time preprocessor guards (`#if WINDOWS`, `#if ANDROID`) or sprawling runtime OS branching (`if (os == 'android')`, `Platform.OS === ...`, `DeviceInfo.Platform == ...`). Eradicate indirect platform-correlated checks (e.g. `DeviceInfo.Idiom == Desktop`, checking for webview objects, pointer type sniffers) in shared code.
2. **The Partial-Class / Build-Tag / Platform Suffix Convention**: Platform divergence is strictly isolated into dedicated compilation files aligned with the environment's bundler and compiler rules (`X.cs`, `X.Windows.cs`, `_X.Mobile.cs`, `X.Android.cs`, `X.iOS.cs` in .NET; `X.types.ts`, `X.windows.tsx`, `X.native.tsx`, `X.ios.tsx`, `X.android.tsx` in React Native / Expo Metro; `x_windows.go`, `x_darwin.go` in Go). Respect platform-sensitive bundler conventions (e.g. Metro resolves `.native.tsx` for iOS/Android, not `.mobile.tsx`).
3. **The 3-Tier Unit Hierarchy (`UNITS.md`)**: Eliminate ad-hoc code duplication by building layered units (`Core Primitive` ➔ `Base Unit` ➔ `Domain Specialization`) and formally cataloging in `UNITS.md`.
4. **View Barrel Layout Swapping & Grouper Shells**: Composite blocks, cards, and pages act as lightweight grouper host shells that import all platform layouts. They must never perform ad-hoc dimension or platform checks directly; instead, they delegate layout tree selection strictly to canonical platform resolvers (`PlatformSelect.For<T>()` in .NET, `getPlatform()` in TypeScript/Flutter).
5. **Continuous Zero-Regression Build & Automated Audits**: Every refactoring phase must verify target compilation with 0 errors and 0 warnings, verified by automated invariant scanners (`scripts/audit-coupling-spec.md` with polyglot pseudocode, TypeScript, and Python reference implementations) checking for both direct and indirect platform conditions.

---

## The 4-Phase Refactoring Workflow

### Phase 1: Orthogonal Discovery & Triage (Dual Independent Subagents)
Before altering any code, dispatch the two registered scout subagents simultaneously on the codebase to execute orthogonal discovery passes:
- **`orthogonal-scout-empirical`** (invoked via `spawn_subagent(agent="orthogonal-scout-empirical", mode="EXPLORE")` or Claude subagent): Performs automated high-recall scanning (AST traversal, regex patterns, direct `#if PLATFORM` leaks, indirect hardware/idiom sniffing, platform SDK leaks).
- **`orthogonal-scout-structural`** (invoked via `spawn_subagent(agent="orthogonal-scout-structural", mode="EXPLORE")` or Claude subagent): Performs high-precision structural auditing (class contracts, boundary leaks, missing 3-tier unit abstractions, ad-hoc UI duplication).

Each agent operates without knowledge of the other, outputting its own findings log. The main orchestrator awaits both runs, triangulates consensus vs unique findings, reconciles anomalies, and generates the prioritized refactoring backlog.

*(Refer to `references/case-study-universal-refactor.md` as the universal case study, and `references/anti-coupling-patterns.md` Section 4).*

### Phase 2: Platform Decoupling & Contract Isolation
When a class or module contains platform-specific implementations:
- Create a platform-neutral contract / interface (`X.cs`, `X.types.ts`, `x.go`).
- Move desktop-specific implementation to the desktop platform file (`X.Windows.cs`, `X.windows.tsx`, `x_windows.go`).
- Create a shared mobile family implementation where iOS and Android share logic (`_X.Mobile.cs`, `X.native.tsx`).
- Create leaf platform files for OS-specific APIs (`X.Android.cs`, `X.iOS.cs`).
- For asymmetric features (e.g. System Tray), wrap with `PlatformCapability<T>` returning null / no-op on platforms without that capability.

👉 *See `references/anti-coupling-patterns.md`, `examples/universal-walkthrough-examples.md`, and template `assets/templates/platform-split-template.md`.*

### Phase 3: Unit Specialization & UNITS.md Cataloging
Refactor repeated micro-behaviors into the 3-tier hierarchy:
- **Inputs**: `Core/Debouncer` ➔ `AppEntry` ➔ `DebouncedEntry` ➔ `SearchInputField`
- **Confirmations**: `ModalPresenter` ➔ `ConfirmationBox` ➔ `ExitConfirmationBox`
- **Asset Resolvers**: `PlatformSelect` ➔ `PlatformImage` ➔ `OnboardingStepImage`
- **Formatters**: Centralized domain formatters (e.g. `SkillsFormatter`)

Register every newly extracted or specialized unit in `UNITS.md`.

👉 *See `references/unit-hierarchy-guide.md`.*

### Phase 4: View Barrel Layout Swapping
For composite block components and pages:
- Convert the host class into a lightweight container shell.
- Move rich multi-column desktop layout into `Layouts/*DesktopLayout` (or `*WindowsLayout`).
- Move single-column mobile layout into `Layouts/*MobileLayout`.
- In the host constructor/component, instantiate strictly the active layout tree via dynamic platform layout builders.

👉 *See `assets/templates/layout-swapping-template.md`.*

---

## Verification & Parity Gates

Run automated coupling audits and compilation gates:
1. **Zero Unexpected In-Body Directives**: Verify no `#if WINDOWS`, `#if ANDROID`, or `#if IOS` exist in shared files outside sanctioned platform resolvers.
2. **Zero Platform API Leaks**: Confirm native OS APIs do not leak into domain services or view models.
3. **Build Cleanliness**: Build all active targets with 0 errors and 0 warnings.
4. **Touch, Sensory & Security Parity**: Verify touch haptics, tap-to-inspect hit testing, and hardware keystore encryption per `references/platform-parity-matrix.md`.

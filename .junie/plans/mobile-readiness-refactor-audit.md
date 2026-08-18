---
sessionId: session-260818-110058-hljz
---

# Requirements

### Overview & Goals
Provide a comprehensive, high-context architectural master prompt that encapsulates the full evolutionary journey of the MostaqlK project across this entire session. The prompt explains:
1. **The Origin & Baseline**: Moving from a Windows-first MVP to a cross-platform architecture.
2. **Cross-Platformality & The Separation Principle**: Eliminating in-body `#if WINDOWS` conditionals and WinUI-specific hacks; enforcing the partial-class convention (`X.cs`, `X.Windows.cs`, `_X.Mobile.cs`, `X.Android.cs`, `X.MaciOS.cs`) and compile-time resolution via `PlatformSelect.For<T>()` and `PlatformCapability<T>`.
3. **Abstractionality & The Base/Specialization Hierarchy**: Transforming ad hoc per-screen UI patterns into reusable, named units cataloged in `UNITS.md` (e.g. `DebouncedEntry` → `SearchInputField`, `ConfirmationBox` → `ExitConfirmationBox`, `PlatformImage` → `OnboardingStepImage`).
4. **View Barrel Layout Swapping**: Decoupling composite block components (`ProjectCard`, `MainWindowPage`, `ProjectDetailsPage`, `SettingsPanel`, `AboutPage`) into host shells delegating to `Layouts/*WindowsLayout.xaml` and `Layouts/*MobileLayout.xaml`.
5. **Mobile Design & Architecture Specifications**: Grounding all future mobile development in the HTML mockups (`.repertoire/design/postmvp/mobile/`) and `docs/mobile-architecture-specification.md` (4-tab navigation, 3 card formats, power button widget, in-app WebView auth, SQLite FTS5 Arabic search, and haptic feedback).
6. **Master-Slave Execution Instructions**: Concrete directives for master orchestrators and subagents to ensure zero desktop regressions and exact visual/behavioral parity.

# Delivery Steps

###   Step 1: Formulate Full-Context Architectural Master Prompt
Compose the complete contextual explanation and directional prompt in the conversation answer detailing cross-platformality, abstractionality, layout swapping, and mobile specifications.

###   Step 2: Finalize and Submit Planning Specification
Ensure all architectural principles and project documentation references are consolidated and submitted cleanly.
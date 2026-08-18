---
sessionId: session-260818-110058-hljz
---

# Requirements

### Overview & Goals
Extend the mobile-readiness architecture to complete full platform independence across all remaining dimensions:
1. **Secure Storage Abstraction**: Abstract and split `SecretProtector` across platforms (`.Windows.cs` using DPAPI, `_SecretProtector.Mobile.cs` using MAUI `SecureStorage` / Keystore) without hardcoded OS checks in shared code.
2. **Secondary Pages View Barrel Layouts**: Convert `ProjectDetailsPage`, `SettingsPanel`, and `AboutPage` into View Barrel layout shells (`Layouts/` Windows vs Mobile) so desktop `AppSidebar` is not hardcoded on mobile screens.
3. **Platform Concepts Completion**: Promote `NavigationControl`, `Drawer`, and `ActionMenu` into fully mapped cross-platform concepts.
4. **Touch Interactivity Parity**: Enhance `PipelineRadar` with tap-to-inspect fallback for touch devices without mouse hover.
5. **Documentation & Zero Regression**: Catalog all new units in `UNITS.md` and verify 0 errors/warnings on Windows.

### Scope
**In Scope:**
- `Infrastructure/Security/SecretProtector` split into partial classes (`SecretProtector.cs`, `SecretProtector.Windows.cs`, `_SecretProtector.Mobile.cs`, `SecretProtector.Android.cs`, `SecretProtector.MaciOS.cs`).
- View Barrel refactor for `ProjectDetailsPage`, `SettingsPanel`, `AboutPage` with dedicated `Layouts/*WindowsLayout.xaml` and `Layouts/*MobileLayout.xaml`.
- `NavigationControl`, `Drawer`, and `ActionMenu` concept implementations.
- `PipelineRadar` touch/tap inspection fallback.
- `UNITS.md` and steering docs updates.

**Out of Scope:**
- Pixel-perfect native custom graphics for future mobile MVP screens (clean functional stubs wired to ViewModels).
- Unrelated backend scraper changes.

# Delivery Steps

### ✓ Step 1: Abstract and Split Secure Storage (SecretProtector)
Split `SecretProtector` into a platform-agnostic partial shell with dedicated per-platform implementations (`SecretProtector.Windows.cs` using DPAPI, `_SecretProtector.Mobile.cs` using MAUI SecureStorage / Keystore, `SecretProtector.Android.cs`, `SecretProtector.MaciOS.cs`).

### ✓ Step 2: Refactor Secondary Pages (ProjectDetailsPage, SettingsPanel, AboutPage) into View Barrels
Extract the desktop layouts (with `AppSidebar`) into `*WindowsLayout.xaml` and create streamlined `*MobileLayout.xaml` containers for `ProjectDetailsPage`, `SettingsPanel`, and `AboutPage`.

### ✓ Step 3: Implement Mobile Concept Mappings for NavigationControl, Drawer, and ActionMenu
Update `NavigationControl`, `Drawer`, and `ActionMenu` in `UI/PlatformConcepts/` with functional mobile and desktop implementations.

### ✓ Step 4: Touch Interaction Parity for PipelineRadar
Add tap-to-inspect gesture handling to `PipelineRadar.xaml.cs` to enable node tooltips on touch devices without mouse hover.

### ✓ Step 5: Update UNITS.md and Perform Windows Regression Build
Update `UNITS.md` for all new units and layouts, and run `dotnet build` to verify clean compilation with 0 warnings/errors.
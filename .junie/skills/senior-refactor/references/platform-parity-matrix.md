# Universal Platform Parity Matrix & Sensory Guidelines

When building or refactoring multi-platform client applications (Desktop, Mobile, Tablet, Web), feature equivalence cannot be treated as a visual afterthought. Touch devices, desktop workstations, and web browsers possess fundamentally different sensory, security, and lifecycle constraints.

---

## 1. Sensory & Interaction Parity Matrix

| Desktop Interaction Model | Mobile / Touch Parity Solution | Web / Responsive Solution |
|---|---|---|
| **Mouse Hover (`PointerEntered` / `PointerExited`)** | Scale compression (`0.97`) + Native Haptics (`impactLight` / Click vibration). | CSS `:hover` with `@media (hover: hover)` guard so touch does not stick. |
| **Hover Tooltips (e.g. data points, radar canvas nodes)** | Tap-to-Inspect Hit Testing: Tap activates data point, opens inspect pill/modal with explicit dismiss button. | Hover popup with fallback click-popover for touch web viewports. |
| **Right-Click Context Menu** | Long-press or horizontal swipe-to-reveal action buttons (`SwipeView` / `ActionSheet`). | Right-click event listener with fallback long-press handler. |
| **Drag-to-Resize Splitter Bar** | Adaptive Master-Detail Breakpoint: Single-column on mobile (< 600px); 2-column split above 600px. | Responsive CSS grid / flex with media query breakpoints. |
| **Keyboard Shortcuts & Accelerators** | Dedicated Floating Action Button (FAB) or bottom navigation bar action. | Global keyboard listener (`keydown`) mapped to accessible shortcuts. |

---

## 2. Security & Credential Protection Parity Matrix

Every operating system environment provides distinct hardware-backed secure storage primitives:

| Platform Family | Recommended Native Enclave | Architecture & Fallback Rule |
|---|---|---|
| **Windows Desktop** | Windows DPAPI (`CryptProtectData`) / Credential Locker | Per-user encryption tied to login credentials. |
| **macOS / iOS** | Apple Keychain Services | Hardware-backed Secure Enclave with `kSecAttrAccessibleAfterFirstUnlock`. |
| **Android** | Android Keystore Provider + AES-256 GCM | Hardware-backed Keystore wrapping encrypted shared preferences / master keys. |
| **Linux Desktop** | Secret Service API / libsecret / KWallet | D-Bus communication with user desktop keyring. |
| **Web / Browser** | Web Cryptography API (`SubtleCrypto`) + IndexedDB | Ephemeral session storage or encrypted IndexedDB (never store raw tokens in localStorage). |

### ❌ Anti-Pattern: Weak Machine-Derived Fallbacks
Falling back to a plaintext config file, base64 obfuscation, or a machine-name-derived hash when running on an unfamiliar platform. If a platform lacks native secure hardware storage, require explicit user passphrase encryption or fail safely.

---

## 3. Background Services & Battery Governance Matrix

| Platform Family | Background Execution Primitive | OS Constraints & Governance Rules |
|---|---|---|
| **Desktop (Windows/macOS/Linux)** | Background Worker Thread / Daemon / System Tray Host | Always-on, unconstrained network and timer access unless OS is sleeping. |
| **Android** | `AndroidX.Work.WorkManager` / Foreground Service | Strict Doze mode; minimum 15-minute periodic intervals unless Foreground Service with user-visible persistent notification. |
| **iOS** | `BGTaskScheduler` / `BGAppRefreshTask` | Discretionary execution controlled strictly by iOS machine learning and battery heuristics (no fixed guarantee of execution interval). |
| **Web** | Web Workers / Service Workers (`PeriodicSync`) | Suspended when tab is inactive; periodic background sync requires PWA installation and user engagement score. |

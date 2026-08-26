# Dynamic Tray Icon Generation & Status Badging

## Overview

When desktop background services transition across multiple pipeline states (such as Idle, Polling, Processing, or Error), updating the tray icon dynamically provides immediate visual feedback.

Instead of switching to generic stock icons (which confuse users with question marks or warning triangles), best practice is to:
1. **Retain the application logo** as a circular or rounded-centroid base.
2. **Overlay a tactile status badge** in the bottom-right corner.
3. **Compile multi-resolution `.ico` binaries** containing 16x16, 24x24, 32x32, 48x48, and 64x64 icon frames for sharp rendering at all DPI scalings.

---

## 1. Palette & Status Color Conventions

| Pipeline State | Badge Color | Contrast Ring | Hex Code | Purpose |
|----------------|-------------|---------------|----------|---------|
| **Idle** | Vivid Orange | Solid White (`#FFFFFF`) | `#F97316` | Background service active and listening on standby |
| **Polling / Pulling** | Vibrant Blue | Solid White (`#FFFFFF`) | `#3B82F6` | Actively querying network endpoints or RSS feeds |
| **Processing / Backlog** | Fresh Green | Solid White (`#FFFFFF`) | `#22C55E` | Draining notification backlog or executing jobs |
| **Error / Alert** | Alert Red | Solid White (`#FFFFFF`) | `#EF4444` | Service encountered network/auth/runtime error |
| **Base / Unbadged** | None | None | N/A | Clean unbadged application brand icon |

---

## 2. Anti-Aliasing and Masking Techniques

To avoid jagged edges on dark and light Windows taskbars:
1. **Super-sampling:** Render circular masks and badges at `2x` or `4x` scale (`1024x1024`), then downsample using `Lanczos` or `Bicubic` filters.
2. **Contrast Outer Ring:** Add a `3px` white border around the badge to ensure high contrast against both the inner logo and the Windows taskbar.
3. **Subtle Drop Shadow:** Apply an alpha-blended black ellipse under the badge for depth and separation.

---

## 3. High-DPI Multi-Resolution `.ico` Packaging

Windows taskbars scale icons based on monitor DPI settings (100% = 16x16, 150% = 24x24, 200% = 32x32, etc.).

When packaging `.ico` assets using Pillow (Python):
```python
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
base_image.save(
    "tray_idle.ico",
    format="ICO",
    sizes=sizes
)
```

---

## 4. Native Windows C# Loading Integration

In `.NET MAUI` / Win32 `TrayIconNativeHost.cs`:

```csharp
// Query exact system metric for small icon at current DPI
int cx = GetSystemMetrics(SM_CXSMICON); // e.g. 16, 24, 32
int cy = GetSystemMetrics(SM_CYSMICON);

// Load the high-DPI icon from file or unpacked temp path
nint hIcon = LoadImage(nint.Zero, iconFilePath, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);

// Update active tray icon data
_iconData.hIcon = hIcon;
_iconData.uFlags = NIF_ICON | NIF_TIP;
Shell_NotifyIcon(NIM_MODIFY, ref _iconData);
```

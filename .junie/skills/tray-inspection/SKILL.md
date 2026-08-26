---
name: tray-inspection
description: Comprehensive workflow and Win32 inspection procedures for Windows system tray items, including listing active tray icons, reading explorer toolbar memory, extracting native HICON bitmaps/PNGs, capturing high-DPI tray snips, and diagnosing tray state or badging issues.
---

# Windows System Tray Inspection and Diagnostics

## Overview

Windows system tray icons are managed by Windows Explorer across two distinct toolbar controls (`Shell_TrayWnd` for visible taskbar items and `NotifyIconOverflowWindow` for overflow flyout items). Inspecting, listing, or extracting icons from these areas requires cross-process Win32 memory scanning, toolbar message interception (`TB_BUTTONCOUNT`, `TB_GETBUTTON`), or GDI screen capture.

This skill provides the standardized workflow, automation scripts, and reference implementations for:
1. **Enumerating all active tray icons** (process ID, executable path, window title, tooltip, and visibility area).
2. **Extracting live icon bitmaps** directly into `.png` / `.ico` files.
3. **Capturing high-DPI bounding-box screenshots** of the notification area for visual regression testing.
4. **Diagnosing missing, corrupted, or stock placeholder glyphs** in desktop applications (.NET MAUI, WPF, WinUI, Win32).
5. **Generating multi-state rounded-centroid badge icons** for background worker pipelines.

---

## When to Use This Skill

- When verifying whether an application's tray icon is registered and active in the system tray.
- When extracting live tray icon graphics to inspect visual appearance or diagnose corrupted/stock glyph bugs.
- When writing or debugging native tray hosting code (`Shell_NotifyIcon`, `NOTIFYICONDATA`, `NIM_ADD`, `NIM_MODIFY`, `NIM_DELETE`).
- When diagnosing why a tray icon disappears after leaving the app in the background or during explorer restarts (`TaskbarCreated`).
- When creating or updating dynamic status badge icon artwork for background processes.

**Do NOT use this skill for:**
- Standard mobile notification channels (Android/iOS).
- Non-Windows desktop notification hubs.

---

## Progressive Disclosure Reference Guide

Load specific references and scripts on demand as needed during your task:

| Resource | Purpose | When to Load |
|----------|---------|--------------|
| [`references/tray-listing.md`](references/tray-listing.md) | Win32 toolbar window hierarchy, 64-bit `TBBUTTON` struct definitions, remote memory buffer reading. | Read when building custom tray listing logic or debugging memory offsets. |
| [`references/icon-extraction.md`](references/icon-extraction.md) | Native `HIMAGELIST` extraction, `WM_GETICON`, `GetClassLongPtr`, and GDI tray snip algorithms. | Read when extracting live icons or capturing screen snips. |
| [`references/dynamic-badging.md`](references/dynamic-badging.md) | Rounded-centroid logo masking, contrast rings, drop shadows, and multi-res `.ico` compilation. | Read when generating status badges or building branded `.ico` assets. |
| [`scripts/list-tray-items.ps1`](scripts/list-tray-items.ps1) | Ready-to-execute PowerShell script to list all visible and overflow tray items. | Run directly via PowerShell to enumerate tray icons. |
| [`scripts/extract-tray-icons.ps1`](scripts/extract-tray-icons.ps1) | Standalone script to extract all active tray icons as PNGs to disk. | Run to extract current system tray images into a folder. |

---

## Standard Diagnostics Workflow

### Phase 1: Locate and Enumerate Tray Items

1. Run the listing script to check if the target process has an active tray item:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .junie/skills/tray-inspection/scripts/list-tray-items.ps1
   ```
2. Verify the following output properties:
   - **Area:** `Visible` (promoted on taskbar) vs `Overflow` (inside hidden chevron flyout).
   - **ProcessName & ProcessId:** Matches the target application.
   - **Tooltip:** Matches the configured tooltip text.
   - **ExecutablePath:** Confirms the exact binary publishing path.

### Phase 2: Extract or Capture Live Tray Artwork

When inspecting visual bugs (such as incorrect glyphs or corrupted file icons):

1. **Direct PNG Extraction:**
   Run the extraction script to dump all tray icons into a destination directory:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .junie/skills/tray-inspection/scripts/extract-tray-icons.ps1 -OutDir "temp-tray-icons"
   ```
2. **Visual Screen Snip (Optional):**
   If pixel-level rendering against dark/light taskbar themes needs verification, see [`references/icon-extraction.md`](references/icon-extraction.md) for GDI screen capture routines.

### Phase 3: Diagnose Root Cause

Check against known failure patterns:

| Symptom | Root Cause | Fix |
|---------|------------|-----|
| Generic blank rectangle / page icon | Code uses Win32 stock placeholder `IDI_APPLICATION` (`32512`). | Load explicit `.ico` from disk or embedded assembly resource via `LoadImage`. |
| Blue `(i)` or yellow `!` warning triangle | Code uses stock `IDI_QUESTION` (`32515`) or `IDI_WARNING` (`32516`). | Replace state-mapping glyph resolver with customized state `.ico` assets. |
| Tray icon disappears on Explorer restart | Application window does not register or handle the Win32 `TaskbarCreated` message. | Register `RegisterWindowMessage("TaskbarCreated")` and re-issue `NIM_ADD` on receipt. |
| Icon missing in single-file / portable build | `.ico` files exist only as loose project files and are not embedded or copied to output. | Add `<EmbeddedResource Include="Resources\Images\Tray\*.ico" />` in `.csproj` and extract stream on fallback. |
| Tooltip truncated | Tooltip string exceeds 128 characters (Win32 `szTip` limit). | Truncate tooltip text to `< 128` characters. |

---

## Output Template: Tray Audit Report

When presenting tray inspection results to the user, format the output as follows:

```markdown
### System Tray Inspection Summary

- **Total Tray Items Found:** [Count] ([Count] Visible, [Count] Overflow)
- **Target Application Status:** [Active / Missing / Inactive]
- **Target Process ID:** [PID] (`[ProcessName].exe`)
- **Location:** [Visible Notification Bar / Overflow Flyout]
- **Current Tooltip:** "[Tooltip text]"
- **Current Rendered Icon:** [Custom Branded Logo / Stock Placeholder / Missing]

#### Active Items Table
| Area | Process | PID | Tooltip | Status |
|------|---------|-----|---------|--------|
| Visible | [Process] | [PID] | [Tooltip] | [OK / Warning / Error] |
| Overflow | [Process] | [PID] | [Tooltip] | [OK / Warning / Error] |
```

---

## Gotchas and Edge Cases

1. **64-bit vs 32-bit Structure Alignment (`TBBUTTON`):**
   On 64-bit Windows, `TBBUTTON` is 32 bytes with specific pointer alignment for `dwData` and `iString`. Reading remote memory with a 32-bit layout on 64-bit Explorer will corrupt pointers and crash memory reads. Always use 64-bit layout (`TBBUTTON64`) on modern 64-bit Windows.
2. **Windows 11 Taskbar Architecture:**
   On certain Windows 11 builds, the notification area toolbar structure may reside in modern XAML surfaces (`Windows.UI.Composition`), while the classic `NotifyIconOverflowWindow` remains standard Win32. The scripts handle both toolbar fallback trees.
3. **Process Integrity Levels:**
   If the target application runs as Administrator (High IL) and Explorer runs as Medium IL, standard messages may be filtered by UIPI (User Interface Privilege Isolation).

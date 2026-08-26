# Tray Icon Extraction & Screen Capture Reference

## 1. Multi-Stage Icon Extraction Strategy

Because applications register tray icons with varying Win32 techniques, a robust extraction tool executes a 3-tier fallback strategy:

### Stage 1: Window Message Interception (`WM_GETICON`)
Send messages directly to the target window handle (`HWND`):
- `WM_GETICON` with `ICON_SMALL2` (`2`): Queries the window's exact small notification icon.
- `WM_GETICON` with `ICON_SMALL` (`0`): Queries the standard small titlebar/tray icon.
- `WM_GETICON` with `ICON_BIG` (`1`): Queries the high-resolution app icon.

### Stage 2: Class Long Pointer Resolution (`GetClassLongPtr`)
If `WM_GETICON` returns null:
- `GetClassLongPtr(hWnd, GCLP_HICONSM)` (`-34`): Reads the small class icon handle.
- `GetClassLongPtr(hWnd, GCLP_HICON)` (`-14`): Reads the standard class icon handle.

### Stage 3: Executable Resource Association (`ExtractAssociatedIcon` / `ExtractIconEx`)
If the process hosts the icon via a background message-only window without an `HICON` property:
- Extract the main icon resource directly from the process executable:
  ```csharp
  Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
  ```

---

## 2. Converting `HICON` to 32-bit ARGB PNG

```csharp
public static void SaveHIconToPng(IntPtr hIcon, string destinationPath)
{
    using (Icon icon = Icon.FromHandle(hIcon))
    using (Bitmap bmp = icon.ToBitmap())
    {
        bmp.Save(destinationPath, System.Drawing.Imaging.ImageFormat.Png);
    }
}
```

---

## 3. High-DPI GDI Tray Screen Capture

To capture a pixel-perfect screenshot of the active tray toolbar directly from the screen:

```powershell
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class TraySnapper {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
"@

$hTray = [TraySnapper]::FindWindow("Shell_TrayWnd", $null)
$hTrayNotify = [TraySnapper]::FindWindowEx($hTray, [IntPtr]::Zero, "TrayNotifyWnd", $null)
$hSysPager = [TraySnapper]::FindWindowEx($hTrayNotify, [IntPtr]::Zero, "SysPager", $null)
$hToolbar = [TraySnapper]::FindWindowEx($hSysPager, [IntPtr]::Zero, "ToolbarWindow32", $null)
if ($hToolbar -eq [IntPtr]::Zero) {
    $hToolbar = [TraySnapper]::FindWindowEx($hTrayNotify, [IntPtr]::Zero, "ToolbarWindow32", $null)
}

$rect = New-Object TraySnapper+RECT
[TraySnapper]::GetWindowRect($hToolbar, [ref]$rect) | Out-Null

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

if ($width -gt 0 -and $height -gt 0) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
    $bmp.Save("scratch\tray_snip.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
    Write-Host "Captured tray screenshot to scratch\tray_snip.png"
}
```

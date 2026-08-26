# Win32 System Tray Architecture & Memory Listing

## Window Hierarchy

Windows Explorer organizes system tray icons into two Win32 `ToolbarWindow32` controls:

1. **Visible Tray Toolbar (Notification Area):**
   - Class hierarchy: `Shell_TrayWnd` → `TrayNotifyWnd` → `SysPager` → `ToolbarWindow32`
   - Secondary layout fallback: `Shell_TrayWnd` → `TrayNotifyWnd` → `ToolbarWindow32`

2. **Overflow Flyout Toolbar (Hidden Icons Chevron):**
   - Class hierarchy: `NotifyIconOverflowWindow` → `ToolbarWindow32`

---

## 64-bit Win32 Structures

### `TBBUTTON` (64-bit Layout)
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct TBBUTTON64
{
    public int iBitmap;        // Bitmap index in toolbar image list
    public int idCommand;      // Command identifier
    public byte fsState;       // Button state flags (TBSTATE_*)
    public byte fsStyle;       // Button style flags (TBSTYLE_*)
    public byte bReserved0;    // Alignment padding
    public byte bReserved1;
    public byte bReserved2;
    public byte bReserved3;
    public byte bReserved4;
    public byte bReserved5;
    public IntPtr dwData;      // Pointer to remote TRAYDATA structure
    public IntPtr iString;     // Pointer to remote tooltip string buffer
}
```

### `TRAYDATA` (Extracted via `dwData`)
The first 8 bytes of the structure pointed to by `dwData` in Explorer's address space contain the target window handle (`HWND`):
- `HWND = (IntPtr)BitConverter.ToInt64(trayDataBytes, 0)`

---

## Remote Memory Scanning Flow

To read the button structures across process boundaries:

```
1. FindWindow("Shell_TrayWnd" / "NotifyIconOverflowWindow")
   └── FindWindowEx(...) to locate ToolbarWindow32
2. GetWindowThreadProcessId(hToolbar, out explorerPid)
3. OpenProcess(PROCESS_ALL_ACCESS, false, explorerPid)
4. VirtualAllocEx(hProcess, IntPtr.Zero, 4096, MEM_COMMIT, PAGE_READWRITE)
5. Loop i from 0 to TB_BUTTONCOUNT - 1:
   ├── SendMessage(hToolbar, TB_GETBUTTON, (IntPtr)i, remoteBuffer)
   ├── ReadProcessMemory(hProcess, remoteBuffer, localBuffer, sizeof(TBBUTTON64))
   ├── Marshal to TBBUTTON64
   ├── ReadProcessMemory(hProcess, tb.dwData, trayData, 64) -> target HWND
   ├── GetWindowThreadProcessId(targetHWND, out targetPid)
   ├── Process.GetProcessById(targetPid) -> ProcessName, ExecutablePath
   └── ReadProcessMemory(hProcess, tb.iString, stringBuf, 512) -> Tooltip
6. VirtualFreeEx(hProcess, remoteBuffer, 0, MEM_RELEASE)
7. CloseHandle(hProcess)
```

<#
.SYNOPSIS
    Extracts all active system tray icons from Windows Explorer to high-resolution PNG files.
.DESCRIPTION
    Scans the system tray toolbars (Visible and Overflow) and extracts the native HICON / Bitmap
    for each running tray item using a 3-tier fallback strategy (WM_GETICON -> GetClassLongPtr -> ExtractAssociatedIcon).
.PARAMETER OutDir
    The target directory where extracted .png files will be saved. Default is "temp-tray-icons".
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .cursor\skills\tray-inspection\scripts\extract-tray-icons.ps1 -OutDir "temp-tray-icons"
#>

[CmdletBinding()]
param(
    [string]$OutDir = "temp-tray-icons"
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$ExtractorCode = @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;

public class ExtractedTrayItem
{
    public string Area;
    public string ProcessName;
    public int ProcessId;
    public string WindowTitle;
    public string Tooltip;
    public string ExecutablePath;
    public Bitmap IconBitmap;
}

public class TrayExtractor
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    const uint TB_BUTTONCOUNT = 0x0418;
    const uint TB_GETBUTTON = 0x0417;
    const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
    const uint MEM_COMMIT = 0x1000;
    const uint MEM_RELEASE = 0x8000;
    const uint PAGE_READWRITE = 0x04;

    const uint WM_GETICON = 0x007F;
    const uint ICON_SMALL = 0;
    const uint ICON_BIG = 1;
    const uint ICON_SMALL2 = 2;
    const int GCLP_HICONSM = -34;
    const int GCLP_HICON = -14;

    [StructLayout(LayoutKind.Sequential)]
    struct TBBUTTON64
    {
        public int iBitmap;
        public int idCommand;
        public byte fsState;
        public byte fsStyle;
        public byte bReserved0;
        public byte bReserved1;
        public byte bReserved2;
        public byte bReserved3;
        public byte bReserved4;
        public byte bReserved5;
        public IntPtr dwData;
        public IntPtr iString;
    }

    public static List<ExtractedTrayItem> ExtractFromToolbar(IntPtr hToolbar, string areaName)
    {
        var result = new List<ExtractedTrayItem>();
        if (hToolbar == IntPtr.Zero) return result;

        int count = (int)SendMessage(hToolbar, TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
        uint pid = 0;
        GetWindowThreadProcessId(hToolbar, out pid);
        IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
        if (hProcess == IntPtr.Zero) return result;

        int tbSize = Marshal.SizeOf(typeof(TBBUTTON64));
        IntPtr remoteBuffer = VirtualAllocEx(hProcess, IntPtr.Zero, 4096, MEM_COMMIT, PAGE_READWRITE);

        try
        {
            byte[] localBuffer = new byte[tbSize];
            IntPtr bytesRead = IntPtr.Zero;
            for (int i = 0; i < count; i++)
            {
                SendMessage(hToolbar, TB_GETBUTTON, (IntPtr)i, remoteBuffer);
                ReadProcessMemory(hProcess, remoteBuffer, localBuffer, tbSize, out bytesRead);

                GCHandle handle = GCHandle.Alloc(localBuffer, GCHandleType.Pinned);
                var tb = (TBBUTTON64)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(TBBUTTON64));
                handle.Free();

                byte[] trayData = new byte[64];
                ReadProcessMemory(hProcess, tb.dwData, trayData, 64, out bytesRead);
                IntPtr targetHWnd = (IntPtr)BitConverter.ToInt64(trayData, 0);

                var item = new ExtractedTrayItem();
                item.Area = areaName;
                item.ProcessName = "Unknown";
                item.ExecutablePath = "";
                item.WindowTitle = "";
                item.Tooltip = "";

                if (targetHWnd != IntPtr.Zero)
                {
                    uint targetPid = 0;
                    GetWindowThreadProcessId(targetHWnd, out targetPid);
                    item.ProcessId = (int)targetPid;
                    if (targetPid > 0)
                    {
                        try
                        {
                            Process proc = Process.GetProcessById((int)targetPid);
                            item.ProcessName = proc.ProcessName;
                            if (proc.MainModule != null)
                            {
                                item.ExecutablePath = proc.MainModule.FileName;
                            }
                        }
                        catch {}
                    }
                    var sb = new StringBuilder(256);
                    GetWindowText(targetHWnd, sb, 256);
                    item.WindowTitle = sb.ToString();

                    // Resolve Icon Handle
                    IntPtr hIcon = SendMessage(targetHWnd, WM_GETICON, (IntPtr)ICON_SMALL2, IntPtr.Zero);
                    if (hIcon == IntPtr.Zero)
                        hIcon = SendMessage(targetHWnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
                    if (hIcon == IntPtr.Zero)
                        hIcon = SendMessage(targetHWnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
                    if (hIcon == IntPtr.Zero)
                        hIcon = GetClassLongPtr(targetHWnd, GCLP_HICONSM);
                    if (hIcon == IntPtr.Zero)
                        hIcon = GetClassLongPtr(targetHWnd, GCLP_HICON);

                    if (hIcon != IntPtr.Zero)
                    {
                        try
                        {
                            using (var ico = Icon.FromHandle(hIcon))
                            {
                                item.IconBitmap = ico.ToBitmap();
                            }
                        }
                        catch {}
                    }
                }

                if (item.IconBitmap == null && !string.IsNullOrEmpty(item.ExecutablePath) && File.Exists(item.ExecutablePath))
                {
                    try
                    {
                        using (var ico = Icon.ExtractAssociatedIcon(item.ExecutablePath))
                        {
                            if (ico != null)
                            {
                                item.IconBitmap = ico.ToBitmap();
                            }
                        }
                    }
                    catch {}
                }

                if (tb.iString != IntPtr.Zero && (long)tb.iString != -1)
                {
                    byte[] strBuf = new byte[512];
                    if (ReadProcessMemory(hProcess, tb.iString, strBuf, strBuf.Length, out bytesRead))
                    {
                        string str = Encoding.Unicode.GetString(strBuf);
                        int nullIdx = str.IndexOf('\0');
                        item.Tooltip = nullIdx >= 0 ? str.Substring(0, nullIdx) : str;
                    }
                }

                result.Add(item);
            }
        }
        finally
        {
            VirtualFreeEx(hProcess, remoteBuffer, 0, MEM_RELEASE);
            CloseHandle(hProcess);
        }

        return result;
    }

    public static List<ExtractedTrayItem> ExtractAll()
    {
        var all = new List<ExtractedTrayItem>();

        IntPtr hTray = FindWindow("Shell_TrayWnd", null);
        IntPtr hTrayNotify = FindWindowEx(hTray, IntPtr.Zero, "TrayNotifyWnd", null);
        IntPtr hSysPager = FindWindowEx(hTrayNotify, IntPtr.Zero, "SysPager", null);
        IntPtr hToolbar = FindWindowEx(hSysPager, IntPtr.Zero, "ToolbarWindow32", null);
        if (hToolbar == IntPtr.Zero) hToolbar = FindWindowEx(hTrayNotify, IntPtr.Zero, "ToolbarWindow32", null);

        all.AddRange(ExtractFromToolbar(hToolbar, "Visible"));

        IntPtr hOverflow = FindWindow("NotifyIconOverflowWindow", null);
        IntPtr hOverflowToolbar = FindWindowEx(hOverflow, IntPtr.Zero, "ToolbarWindow32", null);

        all.AddRange(ExtractFromToolbar(hOverflowToolbar, "Overflow"));

        return all;
    }
}
"@

if (-not ([System.Management.Automation.PSTypeName]'TrayExtractor').Type) {
    Add-Type -TypeDefinition $ExtractorCode -ReferencedAssemblies System.Drawing
}

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}

$items = [TrayExtractor]::ExtractAll()
Write-Host "Extracting $($items.Count) tray icons to '$OutDir'..." -ForegroundColor Cyan

$index = 0
foreach ($item in $items) {
    $safeName = $item.ProcessName -replace '[^a-zA-Z0-9_\-]', '_'
    $fileName = "$($item.Area.ToLower())_${index}_${safeName}.png"
    $fullPath = Join-Path $OutDir $fileName

    if ($item.IconBitmap -ne $null) {
        $item.IconBitmap.Save($fullPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $item.IconBitmap.Dispose()
        Write-Host "  [OK] Extracted: $fileName ($($item.ProcessName) - PID: $($item.ProcessId))" -ForegroundColor Green
    } else {
        Write-Host "  [--] No icon handle resolved for $($item.ProcessName) (PID: $($item.ProcessId))" -ForegroundColor Yellow
    }
    $index++
}

Write-Host "`nExtraction complete. Saved to: $(Resolve-Path $OutDir)" -ForegroundColor Cyan

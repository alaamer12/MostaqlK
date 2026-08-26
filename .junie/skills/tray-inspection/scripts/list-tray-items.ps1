<#
.SYNOPSIS
    Enumerates and lists all active Windows system tray icons from both visible and overflow toolbars.
.DESCRIPTION
    Reads Explorer's ToolbarWindow32 control memory structures to retrieve Process ID, Process Name,
    Executable Path, Window Title, and Tooltip for all registered system tray icons.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .junie\skills\tray-inspection\scripts\list-tray-items.ps1
#>

[CmdletBinding()]
param()

$TrayListingCode = @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;

public class TrayItemInfo
{
    public string Area { get; set; }
    public string ProcessName { get; set; }
    public int ProcessId { get; set; }
    public string WindowTitle { get; set; }
    public string Tooltip { get; set; }
    public string ExecutablePath { get; set; }
}

public class TrayReader
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

    const uint TB_BUTTONCOUNT = 0x0418;
    const uint TB_GETBUTTON = 0x0417;
    const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
    const uint MEM_COMMIT = 0x1000;
    const uint MEM_RELEASE = 0x8000;
    const uint PAGE_READWRITE = 0x04;

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

    public static List<TrayItemInfo> GetTrayItems(IntPtr hToolbar, string areaName)
    {
        var result = new List<TrayItemInfo>();
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

                var item = new TrayItemInfo();
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

    public static List<TrayItemInfo> GetAllTrayItems()
    {
        var all = new List<TrayItemInfo>();

        IntPtr hTray = FindWindow("Shell_TrayWnd", null);
        IntPtr hTrayNotify = FindWindowEx(hTray, IntPtr.Zero, "TrayNotifyWnd", null);
        IntPtr hSysPager = FindWindowEx(hTrayNotify, IntPtr.Zero, "SysPager", null);
        IntPtr hToolbar = FindWindowEx(hSysPager, IntPtr.Zero, "ToolbarWindow32", null);
        if (hToolbar == IntPtr.Zero) hToolbar = FindWindowEx(hTrayNotify, IntPtr.Zero, "ToolbarWindow32", null);

        all.AddRange(GetTrayItems(hToolbar, "Visible"));

        IntPtr hOverflow = FindWindow("NotifyIconOverflowWindow", null);
        IntPtr hOverflowToolbar = FindWindowEx(hOverflow, IntPtr.Zero, "ToolbarWindow32", null);

        all.AddRange(GetTrayItems(hOverflowToolbar, "Overflow"));

        return all;
    }
}
"@

if (-not ([System.Management.Automation.PSTypeName]'TrayReader').Type) {
    Add-Type -TypeDefinition $TrayListingCode
}

$items = [TrayReader]::GetAllTrayItems()
Write-Host "`nFound $($items.Count) active system tray items:`n" -ForegroundColor Cyan
$items | Format-Table Area, ProcessName, ProcessId, WindowTitle, Tooltip, ExecutablePath -AutoSize

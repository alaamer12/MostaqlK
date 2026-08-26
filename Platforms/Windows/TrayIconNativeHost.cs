using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using MostaqlK.UI.TrayIcon;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// Minimal native Windows system-tray icon host, backed directly by the Win32
/// <c>Shell_NotifyIcon</c> API (there is no first-party MAUI/WinUI3 tray-icon surface as of
/// V1's target SDK, so a small interop layer is unavoidable). Kept intentionally small and
/// scoped to this file: it only owns the native icon handle + a native popup context menu,
/// while all state/business logic lives in the platform-neutral <see cref="TrayIconService"/>.
/// </summary>
public sealed class TrayIconNativeHost : IDisposable
{
    private const int WM_TRAYICON = 0x8001;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_GUID = 0x00000020;

    private const uint MF_STRING = 0x00000000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint WM_NULL = 0x0000;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

    // Arbitrary non-zero id for this subclass, only needs to be unique per-hwnd (we only ever
    // install one), as required by SetWindowSubclass.
    private const nuint SubclassId = 1;

    // Legacy GUID from earlier versions, used strictly during cleanup so any leftover icon
    // registration from older runs is purged from Explorer.
    private static readonly Guid LegacyTrayIconGuid = new("5f2b9a1e-6e3f-4a9a-9c9a-2a6a2e9c0f11");

    // Registered window message broadcast by Windows Shell whenever Explorer restarts or the
    // taskbar is recreated, allowing us to re-add the tray icon immediately.
    private static readonly uint WmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    // Cache loaded HICON handles per state to avoid creating new native handles repeatedly
    private static readonly ConcurrentDictionary<TrayIconState, nint> CachedIcons = new();

    private readonly TrayIconService _trayIconService;
    private readonly nint _hwnd;
    private NOTIFYICONDATA _iconData;
    private bool _isAdded;
    private bool _isSubclassed;
    // Keeping a field reference to the delegate is required so the CLR does not garbage-collect
    // it (and the native code call back into freed memory) for as long as the subclass installed
    // via SetWindowSubclass below is alive.
    private readonly SUBCLASSPROC _subclassProc;

    public TrayIconNativeHost(TrayIconService trayIconService, nint hwnd)
    {
        _trayIconService = trayIconService;
        _hwnd = hwnd;

        // Clean up any stale legacy icon registered by GUID
        var staleGuidData = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            uFlags = NIF_GUID,
            guidItem = LegacyTrayIconGuid,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
        Shell_NotifyIcon(NIM_DELETE, ref staleGuidData);

        // Standard hWnd + uID registration (without NIF_GUID). Windows ties NIF_GUID to the original
        // executable path where it was first registered, causing Shell_NotifyIcon to fail for portable
        // releases running from different directories.
        _iconData = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = LoadIconHandleFor(trayIconService.State),
            szTip = "MostaqlK",
        };

        // Defensively clear any stale icon for this hwnd + uID
        var staleHwndData = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = 0
        };
        Shell_NotifyIcon(NIM_DELETE, ref staleHwndData);

        _isAdded = Shell_NotifyIcon(NIM_ADD, ref _iconData);

        _trayIconService.StateChanged += OnStateChanged;

        // WinUI window WndProc subclassing to observe WM_TRAYICON and WM_TASKBARCREATED
        _subclassProc = SubclassProc;
        _isSubclassed = SetWindowSubclass(_hwnd, _subclassProc, SubclassId, nint.Zero);
    }

    /// <summary>
    /// The <c>SUBCLASSPROC</c> installed on the main window via <see cref="SetWindowSubclass"/>;
    /// forwards <see cref="WM_TRAYICON"/> to <see cref="HandleWindowMessage"/> and defers every
    /// other message to <see cref="DefSubclassProc"/> so the window keeps behaving normally.
    /// </summary>
    private nint SubclassProc(nint hWnd, uint message, nint wParam, nint lParam, nuint subclassId, nint refData)
    {
        HandleWindowMessage(message, wParam, lParam);
        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    /// <summary>
    /// Handles <paramref name="message"/> whenever it arrives via the subclassed WndProc above,
    /// so a tray click can trigger the appropriate menu action.
    /// </summary>
    private void HandleWindowMessage(uint message, nint wParam, nint lParam)
    {
        if (message == WmTaskbarCreated)
        {
            _isAdded = Shell_NotifyIcon(NIM_ADD, ref _iconData);
            return;
        }

        if (message != WM_TRAYICON)
        {
            return;
        }

        var mouseEvent = (int)lParam;
        if (mouseEvent == WM_LBUTTONUP)
        {
            // Left click: jump straight to the primary "Open" action, matching the tray icon's
            // most common use (and Explorer's usual left-click convention).
            _trayIconService.MenuItems.FirstOrDefault(m => m.Label == "Open")?.Command();
        }
        else if (mouseEvent == WM_RBUTTONUP)
        {
            // Right click must show the actual options menu, not just re-run "Open".
            ShowContextMenu();
        }
    }

    /// <summary>
    /// Builds and shows the native right-click popup menu from <see cref="TrayIconService.MenuItems"/>,
    /// then invokes whichever entry the user picked.
    /// </summary>
    private void ShowContextMenu()
    {
        var menuItems = _trayIconService.MenuItems;
        var hMenu = CreatePopupMenu();
        if (hMenu == nint.Zero)
        {
            return;
        }

        try
        {
            for (var i = 0; i < menuItems.Count; i++)
            {
                AppendMenu(hMenu, MF_STRING, (nuint)(i + 1), menuItems[i].Label);
            }

            GetCursorPos(out var cursor);

            // Required so the popup menu closes correctly when the user clicks elsewhere,
            // per the standard Win32 tray-icon context menu recipe.
            SetForegroundWindow(_hwnd);

            var selectedId = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, cursor.X, cursor.Y, _hwnd, nint.Zero);

            // Also part of the standard recipe: without this extra message, the menu can fail
            // to dismiss until the window regains focus by some other means.
            PostMessage(_hwnd, WM_NULL, nint.Zero, nint.Zero);

            if (selectedId > 0 && selectedId <= menuItems.Count)
            {
                menuItems[(int)selectedId - 1].Command();
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private void OnStateChanged(TrayIconState state)
    {
        _iconData.hIcon = LoadIconHandleFor(state);
        if (!_isAdded)
        {
            _isAdded = Shell_NotifyIcon(NIM_ADD, ref _iconData);
        }
        else
        {
            Shell_NotifyIcon(NIM_MODIFY, ref _iconData);
        }
    }

    /// <summary>
    /// Resolves and loads the high-DPI rounded-centroid native icon handle for each pipeline state:
    ///   - Idle: Orange badge
    ///   - Polling: Blue badge
    ///   - BacklogDraining: Green badge
    ///   - Error: Red badge
    /// </summary>
    private nint LoadIconHandleFor(TrayIconState state)
    {
        return CachedIcons.GetOrAdd(state, s =>
        {
            var hIcon = LoadStateIcon(s);
            if (hIcon != nint.Zero)
            {
                return hIcon;
            }

            // Fallback to stock icons if disk/embedded asset cannot be loaded
            var fallbackIconId = s switch
            {
                TrayIconState.Error => 32513, // IDI_ERROR
                TrayIconState.BacklogDraining => 32516, // IDI_WARNING
                TrayIconState.Polling => 32515, // IDI_QUESTION
                _ => 32512, // IDI_APPLICATION
            };
            return LoadIcon(nint.Zero, fallbackIconId);
        });
    }

    private static nint LoadStateIcon(TrayIconState state)
    {
        var iconFileName = state switch
        {
            TrayIconState.Error => "tray_error.ico",
            TrayIconState.BacklogDraining => "tray_processing.ico",
            TrayIconState.Polling => "tray_pulling.ico",
            _ => "tray_idle.ico"
        };

        // 1. Try AppContext.BaseDirectory / Resources / Images / Tray /
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "Images", "Tray", iconFileName),
            Path.Combine(AppContext.BaseDirectory, iconFileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Images", "Tray", iconFileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconFileName),
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                var hIcon = LoadIconFromFile(path);
                if (hIcon != nint.Zero)
                {
                    return hIcon;
                }
            }
        }

        // 2. Try loading from Embedded Resources (e.g. single-file publish / bundle)
        try
        {
            var assembly = typeof(TrayIconNativeHost).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(iconFileName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(resourceName))
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "MostaqlK_TrayIcons");
                    Directory.CreateDirectory(tempDir);
                    var tempFile = Path.Combine(tempDir, iconFileName);

                    using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        stream.CopyTo(fs);
                    }

                    var hIcon = LoadIconFromFile(tempFile);
                    if (hIcon != nint.Zero)
                    {
                        return hIcon;
                    }
                }
            }
        }
        catch
        {
            // Ignore embedded resource fallback errors and let caller handle stock fallback
        }

        return nint.Zero;
    }

    private static nint LoadIconFromFile(string filePath)
    {
        try
        {
            var cx = GetSystemMetrics(SM_CXSMICON);
            var cy = GetSystemMetrics(SM_CYSMICON);
            if (cx <= 0) cx = 16;
            if (cy <= 0) cy = 16;

            var hIcon = LoadImage(nint.Zero, filePath, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);
            if (hIcon != nint.Zero)
            {
                return hIcon;
            }

            // Fallback with default size flag
            return LoadImage(nint.Zero, filePath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        }
        catch
        {
            return nint.Zero;
        }
    }

    public void Dispose()
    {
        _trayIconService.StateChanged -= OnStateChanged;

        if (_isSubclassed)
        {
            RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
            _isSubclassed = false;
        }

        if (_isAdded)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _iconData);
            _isAdded = false;
        }
    }

    /// <summary>Native signature required by <see cref="SetWindowSubclass"/>/<see cref="RemoveWindowSubclass"/>.</summary>
    private delegate nint SUBCLASSPROC(nint hWnd, uint message, nint wParam, nint lParam, nuint subclassId, nint refData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint hInstance, int lpIconName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint lptpm);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    // comctl32's window-subclassing API: safely chains a custom WndProc onto whatever WndProc
    // the window already has (here, the framework's own WinUI3 WndProc), instead of overwriting
    // it outright via SetWindowLongPtr(GWLP_WNDPROC), which would risk breaking WinUI itself.
    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);
}

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
    // Identifies the icon by a fixed GUID rather than by hWnd+uID. Without this, every relaunch
    // of the app produces a new hWnd, and Explorer's own tray-icon cache (which keys entries by
    // hWnd+uID for non-GUID icons) can end up showing a stale/ghost icon from a previous run, or
    // failing to show the current one at all until Explorer is restarted - matching the "still
    // not working / cache problem" symptom. A GUID keeps the same identity across restarts.
    private const uint NIF_GUID = 0x00000020;

    private const uint MF_STRING = 0x00000000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint WM_NULL = 0x0000;

    // Arbitrary non-zero id for this subclass, only needs to be unique per-hwnd (we only ever
    // install one), as required by SetWindowSubclass.
    private const nuint SubclassId = 1;

    // Fixed, stable identity for this app's single tray icon so Explorer recognizes it across
    // process restarts instead of caching it by the (ever-changing) hWnd+uID pair.
    private static readonly Guid TrayIconGuid = new("5f2b9a1e-6e3f-4a9a-9c9a-2a6a2e9c0f11");

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

        _iconData = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_GUID,
            uCallbackMessage = WM_TRAYICON,
            hIcon = LoadIconHandleFor(trayIconService.State),
            szTip = "MostaqlK",
            guidItem = TrayIconGuid,
        };

        // Defensively clear any stale icon left behind under the same GUID by a previous run
        // that crashed/was killed before it could call NIM_DELETE (e.g. the debug-session
        // process kills seen during development); ignore the result, since there is normally
        // nothing to delete.
        var staleIconData = new NOTIFYICONDATA { cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), uFlags = NIF_GUID, guidItem = TrayIconGuid, szTip = string.Empty, szInfo = string.Empty, szInfoTitle = string.Empty };
        Shell_NotifyIcon(NIM_DELETE, ref staleIconData);

        Shell_NotifyIcon(NIM_ADD, ref _iconData);
        _isAdded = true;

        _trayIconService.StateChanged += OnStateChanged;

        // Nothing in the app ever forwarded WM_TRAYICON to HandleWindowMessage below - the main
        // WinUI window's WndProc is owned by the framework, so this host has to install its own
        // subclass (comctl32's SetWindowSubclass, which safely chains onto whatever WndProc is
        // already there) to actually observe tray clicks. Without this, left/right-clicking the
        // tray icon silently did nothing.
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
        Shell_NotifyIcon(NIM_MODIFY, ref _iconData);
    }

    /// <summary>
    /// Resolves the glyph for each state. Uses the system's built-in stock icons as a
    /// zero-asset placeholder differentiator (info/question/error/warning) until dedicated
    /// tray artwork is added.
    /// </summary>
    private nint LoadIconHandleFor(TrayIconState state)
    {
        var iconId = state switch
        {
            TrayIconState.Error => 32513, // IDI_ERROR
            TrayIconState.BacklogDraining => 32516, // IDI_WARNING
            TrayIconState.Polling => 32515, // IDI_QUESTION
            _ => 32512, // IDI_APPLICATION
        };

        return LoadIcon(nint.Zero, iconId);
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint hInstance, int lpIconName);

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

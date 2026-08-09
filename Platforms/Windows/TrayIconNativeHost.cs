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

    private readonly TrayIconService _trayIconService;
    private readonly nint _hwnd;
    private NOTIFYICONDATA _iconData;
    private bool _isAdded;

    public TrayIconNativeHost(TrayIconService trayIconService, nint hwnd)
    {
        _trayIconService = trayIconService;
        _hwnd = hwnd;

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

        Shell_NotifyIcon(NIM_ADD, ref _iconData);
        _isAdded = true;

        _trayIconService.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Call from the host window's message loop / subclassed WndProc whenever <paramref name="message"/>
    /// arrives, so a tray click can trigger the appropriate menu action.
    /// </summary>
    public void HandleWindowMessage(uint message, nint wParam, nint lParam)
    {
        if (message != WM_TRAYICON)
        {
            return;
        }

        var mouseEvent = (int)lParam;
        if (mouseEvent is WM_LBUTTONUP or WM_RBUTTONUP)
        {
            // A full native popup menu is out of scope for this minimal host - left/right click
            // both default to the primary "Open" action, matching the tray icon's most common use.
            _trayIconService.MenuItems.FirstOrDefault(m => m.Label == "Open")?.Command();
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

        if (_isAdded)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _iconData);
            _isAdded = false;
        }
    }

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
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint hInstance, int lpIconName);
}

using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// Instant native Win32 splash screen displayed immediately upon launch on Windows.
/// Runs on a dedicated background STA thread with double-buffered GDI rendering
/// so visual feedback appears in &lt; 20ms while WinUI, .NET runtime, and MAUI initialize.
/// </summary>
public static class NativeSplashScreen
{
    private static Thread? _splashThread;
    private static IntPtr _hwnd = IntPtr.Zero;
    private static readonly object _lock = new();
    private static volatile bool _shouldClose = false;
    private static int _animFrame = 0;

    // Win32 Constants
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOW = 5;
    private const int WM_DESTROY = 0x0002;
    private const int WM_PAINT = 0x000F;
    private const int WM_TIMER = 0x0113;
    private const int WM_CLOSE = 0x0010;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int DT_CENTER = 0x00000001;
    private const int DT_VCENTER = 0x00000004;
    private const int DT_SINGLELINE = 0x00000020;
    private const int DT_NOPREFIX = 0x00000800;
    private const int TRANSPARENT = 1;
    private const int HWND_TOPMOST = -1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, [In] ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hWnd, IntPtr uIDEvent);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int iBkMode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint crColor);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(
        int cHeight, int cWidth, int cEscapement, int cOrientation,
        int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut,
        uint iCharSet, uint iOutPrecision, uint iClipPrecision,
        uint iQuality, uint iPitchAndFamily, string pszFaceName);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, [In] ref RECT lprc, IntPtr hbr);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Keep delegate reference alive so GC doesn't collect it while window proc is active
    private static WndProcDelegate? _wndProc;

    /// <summary>
    /// Displays the native splash screen immediately on a background thread.
    /// </summary>
    public static void Show()
    {
        lock (_lock)
        {
            if (_splashThread != null && _splashThread.IsAlive)
            {
                return;
            }

            _shouldClose = false;
            _animFrame = 0;

            _splashThread = new Thread(SplashThreadProc)
            {
                IsBackground = true,
                Name = "MostaqlK.NativeSplashScreen"
            };
            _splashThread.SetApartmentState(ApartmentState.STA);
            _splashThread.Start();
        }
    }

    /// <summary>
    /// Dismisses the native splash screen.
    /// </summary>
    public static void Hide()
    {
        lock (_lock)
        {
            _shouldClose = true;
            if (_hwnd != IntPtr.Zero)
            {
                PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    private static void SplashThreadProc()
    {
        const string className = "MostaqlKSplashScreenWndClass";
        IntPtr hInstance = GetModuleHandle(null);

        _wndProc = WndProc;

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x0003, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            hbrBackground = IntPtr.Zero,
            lpszClassName = className
        };

        RegisterClassEx(ref wc);

        int width = 420;
        int height = 260;
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);
        int x = Math.Max(0, (screenWidth - width) / 2);
        int y = Math.Max(0, (screenHeight - height) / 2);

        _hwnd = CreateWindowEx(
            WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
            className,
            "MostaqlK",
            WS_POPUP,
            x, y, width, height,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // Apply smooth rounded corners
        IntPtr hRgn = CreateRoundRectRgn(0, 0, width, height, 20, 20);
        if (hRgn != IntPtr.Zero)
        {
            SetWindowRgn(_hwnd, hRgn, true);
        }

        ShowWindow(_hwnd, SW_SHOW);
        UpdateWindow(_hwnd);

        // Timer for progress animation (30ms ~ 33fps)
        SetTimer(_hwnd, (IntPtr)1, 30, IntPtr.Zero);

        if (_shouldClose)
        {
            PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        _hwnd = IntPtr.Zero;
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TIMER:
                _animFrame = (_animFrame + 1) % 120;
                InvalidateRect(hWnd, IntPtr.Zero, false);
                return IntPtr.Zero;

            case WM_PAINT:
                PaintSplashScreen(hWnd);
                return IntPtr.Zero;

            case WM_CLOSE:
                KillTimer(hWnd, (IntPtr)1);
                DestroyWindow(hWnd);
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                return DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private static void PaintSplashScreen(IntPtr hWnd)
    {
        IntPtr hdc = BeginPaint(hWnd, out PAINTSTRUCT ps);
        if (hdc == IntPtr.Zero) return;

        GetClientRect(hWnd, out RECT rect);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        // Double buffering to prevent any flicker
        IntPtr memDC = CreateCompatibleDC(hdc);
        IntPtr memBitmap = CreateCompatibleBitmap(hdc, width, height);
        IntPtr oldBitmap = SelectObject(memDC, memBitmap);

        // Background Color: #2386C8 (RGB: 35, 134, 200) -> Win32 COLORREF is 0x00BBGGRR -> 0x00C88623
        uint bgCol = 0x00C88623;
        IntPtr bgBrush = CreateSolidBrush(bgCol);
        FillRect(memDC, ref rect, bgBrush);
        DeleteObject(bgBrush);

        SetBkMode(memDC, TRANSPARENT);

        // 1. App Title: "MostaqlK"
        IntPtr titleFont = CreateFont(
            -34, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE_QUALITY */, 0, "Segoe UI");
        IntPtr oldFont = SelectObject(memDC, titleFont);
        SetTextColor(memDC, 0x00FFFFFF); // White

        var titleRect = new RECT { Left = 20, Top = 50, Right = width - 20, Bottom = 100 };
        DrawText(memDC, "MostaqlK", -1, ref titleRect, DT_CENTER | DT_SINGLELINE | DT_NOPREFIX);

        // 2. Arabic Subtitle: "مستقل ك"
        IntPtr arFont = CreateFont(
            -18, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        SelectObject(memDC, arFont);
        SetTextColor(memDC, 0x00D9F0FF); // Very light cyan/white

        var arRect = new RECT { Left = 20, Top = 105, Right = width - 20, Bottom = 135 };
        DrawText(memDC, "منصة متابعة وإشعارات مستقل", -1, ref arRect, DT_CENTER | DT_SINGLELINE | DT_NOPREFIX);

        // 3. Status text: "جاري بدء التطبيق..."
        IntPtr statusFont = CreateFont(
            -13, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        SelectObject(memDC, statusFont);
        SetTextColor(memDC, 0x00EAF6FF);

        var statusRect = new RECT { Left = 20, Top = 175, Right = width - 20, Bottom = 200 };
        DrawText(memDC, "جاري بدء التطبيق...", -1, ref statusRect, DT_CENTER | DT_SINGLELINE | DT_NOPREFIX);

        // 4. Smooth animated loading bar
        int barWidth = 240;
        int barHeight = 4;
        int barX = (width - barWidth) / 2;
        int barY = 210;

        // Bar background track (semi-transparent dark blue)
        IntPtr trackBrush = CreateSolidBrush(0x009C6514); // Darker blue track
        var trackRect = new RECT { Left = barX, Top = barY, Right = barX + barWidth, Bottom = barY + barHeight };
        FillRect(memDC, ref trackRect, trackBrush);
        DeleteObject(trackBrush);

        // Animated moving progress indicator
        int indicatorWidth = 70;
        float progress = (_animFrame % 60) / 60.0f;
        // Ping-pong or smooth travel across the bar
        int indX = barX + (int)((barWidth - indicatorWidth) * (Math.Sin(progress * Math.PI * 2 - Math.PI / 2) + 1) / 2.0);

        IntPtr indBrush = CreateSolidBrush(0x00FFFFFF); // Pure white indicator
        var indRect = new RECT { Left = indX, Top = barY, Right = indX + indicatorWidth, Bottom = barY + barHeight };
        FillRect(memDC, ref indRect, indBrush);
        DeleteObject(indBrush);

        // Clean up fonts
        SelectObject(memDC, oldFont);
        DeleteObject(titleFont);
        DeleteObject(arFont);
        DeleteObject(statusFont);

        // Blit to screen
        BitBlt(hdc, 0, 0, width, height, memDC, 0, 0, 0x00CC0020 /* SRCCOPY */);

        SelectObject(memDC, oldBitmap);
        DeleteObject(memBitmap);
        DeleteDC(memDC);

        EndPaint(hWnd, ref ps);
    }
}

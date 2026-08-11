"""
Snip Tool - A Windows Snipping Tool clone with Rectangle, Window, and Fullscreen modes.

Requirements (Windows only):
    pip install pywin32 pillow psutil

Run interactively (GUI):
    python snip_tool.py

Run targeted at a specific app (headless / scriptable):
    python snip_tool.py --name notepad.exe
    python snip_tool.py --pid 12345
    python snip_tool.py --title "Visual Studio Code"
    python snip_tool.py --name "Antigravity IDE.exe" --list   (just list candidates)
    python snip_tool.py --name chrome.exe --output out.png --preview

Modes (GUI):
    - Rectangle Snip : drag to select a region of the screen
    - Window Snip     : hover over a window to highlight it, click to capture
                         it (uses PrintWindow, so it works even if the window
                         is partially covered by other windows)
    - Fullscreen Snip : captures the entire virtual screen (all monitors)
    - Target Snip     : type a process name, PID, or window-title substring
                         and capture that window directly, without hovering

Targeting notes:
    Many apps (Electron apps, browsers, IDEs, etc.) run many child/helper
    processes that all share the same executable name (e.g. "Antigravity
    IDE.exe" appearing a dozen times in Task Manager with different PIDs).
    Only one of those processes usually owns the real, visible main window.
    When targeting by name, this tool walks the process tree, discards
    processes whose parent has the same name (treating them as child/helper
    processes), and among the remaining "root" candidates picks the one
    with the largest visible top-level window - i.e. the actual main app
    window, not a background/renderer/GPU helper process.

Captured images are saved to a "Snips" folder in your Pictures directory,
copied to the clipboard, and opened in a preview window with a Save As option.
"""

import argparse
import ctypes
import ctypes.wintypes
import logging
import os
import re
import sys
import time
import traceback
from datetime import datetime

import tkinter as tk
from tkinter import filedialog, messagebox, simpledialog

from PIL import Image, ImageGrab

try:
    import win32gui
    import win32ui
    import win32con
    import win32api
    import win32process
    import win32clipboard
except ImportError:
    raise SystemExit(
        "This tool requires pywin32. Install it with:\n"
        "    pip install pywin32"
    )

try:
    import psutil
except ImportError:
    raise SystemExit(
        "This tool requires psutil for process targeting. Install it with:\n"
        "    pip install psutil"
    )

# Make the process DPI aware so coordinates/captures aren't scaled/blurry
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)  # PROCESS_PER_MONITOR_DPI_AWARE
except Exception:
    try:
        ctypes.windll.user32.SetProcessDPIAware()
    except Exception:
        pass


SNIP_DIR = os.path.join(os.path.expanduser("~"), "Pictures", "Snips")

log = logging.getLogger("snip_tool")


def setup_logging(debug=False):
    level = logging.DEBUG if debug else logging.WARNING
    handler = logging.StreamHandler(sys.stderr)
    handler.setFormatter(logging.Formatter("[%(levelname)s] %(message)s"))
    log.handlers.clear()
    log.addHandler(handler)
    log.setLevel(level)
    log.propagate = False


def ensure_snip_dir():
    os.makedirs(SNIP_DIR, exist_ok=True)


def default_filename():
    return os.path.join(SNIP_DIR, datetime.now().strftime("Snip_%Y-%m-%d_%H-%M-%S.png"))


def copy_image_to_clipboard(image: Image.Image):
    """Copy a PIL image to the Windows clipboard as a DIB bitmap."""
    output = image.convert("RGB")
    import io
    buf = io.BytesIO()
    output.save(buf, "BMP")
    data = buf.getvalue()[14:]  # strip the 14-byte BMP file header, keep DIB
    win32clipboard.OpenClipboard()
    try:
        win32clipboard.EmptyClipboard()
        win32clipboard.SetClipboardData(win32con.CF_DIB, data)
    finally:
        win32clipboard.CloseClipboard()


def get_virtual_screen_bounds():
    """Bounding box covering all monitors."""
    left = win32api.GetSystemMetrics(win32con.SM_XVIRTUALSCREEN)
    top = win32api.GetSystemMetrics(win32con.SM_YVIRTUALSCREEN)
    width = win32api.GetSystemMetrics(win32con.SM_CXVIRTUALSCREEN)
    height = win32api.GetSystemMetrics(win32con.SM_CYVIRTUALSCREEN)
    return left, top, left + width, top + height


def is_rtl_mirrored_window(hwnd):
    """Return True if the window has WS_EX_LAYOUTRTL set. Windows with this
    style (common in apps supporting Arabic/Hebrew UI) have GDI mirror all
    drawing horizontally at the device-context level. The screen compositor
    displays this correctly, but PrintWindow grabs the raw, physically
    mirrored bitmap - so captures of these windows come out flipped
    left-right unless we compensate.
    """
    try:
        ex_style = win32gui.GetWindowLong(hwnd, win32con.GWL_EXSTYLE)
        mirrored = bool(ex_style & win32con.WS_EX_LAYOUTRTL)
        log.debug("is_rtl_mirrored_window: hwnd=%s ex_style=0x%X mirrored=%s",
                  hwnd, ex_style, mirrored)
        return mirrored
    except Exception as e:
        log.debug("is_rtl_mirrored_window: failed to read style for hwnd=%s: %s", hwnd, e)
        return False


def capture_window(hwnd, flip_override=None) -> Image.Image:
    """Capture a specific window by handle, using PrintWindow (works even if
    the window is occluded / not the foreground window).

    flip_override: None = auto-detect WS_EX_LAYOUTRTL and flip if needed;
    True = always flip horizontally; False = never flip.
    """
    log.debug("capture_window: hwnd=%s title=%r", hwnd, win32gui.GetWindowText(hwnd))

    left, top, right, bottom = win32gui.GetWindowRect(hwnd)
    width, height = right - left, bottom - top
    width, height = max(width, 1), max(height, 1)
    log.debug("capture_window: rect=(%s,%s,%s,%s) size=%sx%s", left, top, right, bottom, width, height)

    hwnd_dc = win32gui.GetWindowDC(hwnd)
    if not hwnd_dc:
        raise RuntimeError(f"GetWindowDC failed for hwnd {hwnd} (window may have closed)")
    mfc_dc = win32ui.CreateDCFromHandle(hwnd_dc)
    save_dc = mfc_dc.CreateCompatibleDC()

    bitmap = win32ui.CreateBitmap()
    bitmap.CreateCompatibleBitmap(mfc_dc, width, height)
    save_dc.SelectObject(bitmap)

    # PW_RENDERFULLCONTENT (2) captures modern apps (UWP, browsers, etc.) correctly
    result = ctypes.windll.user32.PrintWindow(hwnd, save_dc.GetSafeHdc(), 2)
    log.debug("capture_window: PrintWindow(flags=2) result=%s", result)
    if result == 0:
        # Fallback flag
        result = ctypes.windll.user32.PrintWindow(hwnd, save_dc.GetSafeHdc(), 0)
        log.debug("capture_window: PrintWindow(flags=0) fallback result=%s", result)
    if result == 0:
        err = ctypes.GetLastError()
        log.debug("capture_window: both PrintWindow attempts returned 0; "
                  "GetLastError=%s (image may be blank)", err)

    bmpinfo = bitmap.GetInfo()
    bmpstr = bitmap.GetBitmapBits(True)
    image = Image.frombuffer(
        "RGB",
        (bmpinfo["bmWidth"], bmpinfo["bmHeight"]),
        bmpstr, "raw", "BGRX", 0, 1,
    )

    win32gui.DeleteObject(bitmap.GetHandle())
    save_dc.DeleteDC()
    mfc_dc.DeleteDC()
    win32gui.ReleaseDC(hwnd, hwnd_dc)

    log.debug("capture_window: produced image size=%s", image.size)

    should_flip = flip_override if flip_override is not None else is_rtl_mirrored_window(hwnd)
    if should_flip:
        log.debug("capture_window: flipping image horizontally to correct RTL mirroring")
        image = image.transpose(Image.FLIP_LEFT_RIGHT)

    return image


def capture_window_with_frame(hwnd) -> Image.Image:
    """Capture a specific window INCLUDING its native OS-drawn frame (title
    bar, caption buttons, drop shadow / rounded corners).

    `capture_window()` uses PrintWindow, which only renders the window's own
    client-area content - it never includes the non-client chrome that
    Windows/DWM composites on top (the title bar and its caption buttons in
    particular). To see what the *real* window frame looks like (e.g. to
    check whether native vs. custom title-bar buttons are being drawn), grab
    the actual composited pixels straight off the screen instead.

    This only works correctly if the window isn't occluded by other windows,
    since it reads real screen pixels rather than the window's own bitmap.
    """
    log.debug("capture_window_with_frame: hwnd=%s title=%r", hwnd, win32gui.GetWindowText(hwnd))

    # DWMWA_EXTENDED_FRAME_BOUNDS (9) gives the true visible outer bounds of
    # the window (including the border DWM draws), which is more accurate
    # than GetWindowRect for modern (DWM-composited) windows - GetWindowRect
    # can include several extra pixels of invisible resize-border padding.
    rect = ctypes.wintypes.RECT()
    hresult = ctypes.windll.dwmapi.DwmGetWindowAttribute(
        ctypes.wintypes.HWND(hwnd),
        ctypes.wintypes.DWORD(9),  # DWMWA_EXTENDED_FRAME_BOUNDS
        ctypes.byref(rect),
        ctypes.sizeof(rect),
    )
    if hresult == 0:
        left, top, right, bottom = rect.left, rect.top, rect.right, rect.bottom
        log.debug("capture_window_with_frame: DwmGetWindowAttribute bounds=(%s,%s,%s,%s)",
                  left, top, right, bottom)
    else:
        left, top, right, bottom = win32gui.GetWindowRect(hwnd)
        log.debug("capture_window_with_frame: DWM call failed (hresult=%s), "
                  "falling back to GetWindowRect=(%s,%s,%s,%s)",
                  hresult, left, top, right, bottom)

    # Bring the window to the foreground so it isn't occluded by whatever we
    # are capturing from (e.g. this very terminal window).
    try:
        win32gui.SetForegroundWindow(hwnd)
        time.sleep(0.2)
    except Exception as e:
        log.debug("capture_window_with_frame: SetForegroundWindow failed: %s", e)

    image = ImageGrab.grab(bbox=(left, top, right, bottom), all_screens=True)
    log.debug("capture_window_with_frame: produced image size=%s", image.size)
    return image


def get_toplevel_window_at(x, y):
    """Given screen coords, return the top-level window handle under the cursor."""
    hwnd = win32gui.WindowFromPoint((x, y))
    if hwnd == 0:
        return None
    # Walk up to the top-level owner window
    root = win32gui.GetAncestor(hwnd, win32con.GA_ROOT)
    return root if root else hwnd


# ---------------------------------------------------------------------------
# Process / window targeting
# ---------------------------------------------------------------------------

def get_process_name(pid):
    try:
        name = psutil.Process(pid).name()
        log.debug("get_process_name(%s) -> %r", pid, name)
        return name
    except Exception as e:
        log.debug("get_process_name(%s) failed: %s", pid, e)
        return None


def get_root_pids_for_name(name):
    """Among all running processes matching `name` (case-insensitive), return
    the pids that look like the 'parent'/root of the group - i.e. their
    parent process does NOT have the same name. This filters out child/
    helper processes an app spawns under its own name (common in Electron
    apps, browsers, IDEs, etc.) so we target the real app, not a helper.

    If every matching process has a same-named parent (or none do), falls
    back to returning every matching pid.
    """
    name_lower = name.lower()
    matches = []
    for p in psutil.process_iter(["pid", "name", "ppid"]):
        try:
            pname = p.info["name"]
            if pname and pname.lower() == name_lower:
                matches.append(p.info)
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue

    log.debug("get_root_pids_for_name(%r): %d process(es) matched by name: %s",
              name, len(matches), [(m["pid"], m["ppid"]) for m in matches])

    match_pids = {m["pid"] for m in matches}
    roots = [m["pid"] for m in matches if m["ppid"] not in match_pids]
    log.debug("get_root_pids_for_name(%r): root pid(s) (parent not same name): %s",
              name, roots)
    result = roots if roots else [m["pid"] for m in matches]
    if not roots and matches:
        log.debug("get_root_pids_for_name(%r): no clear root found, falling back "
                  "to all %d matching pid(s)", name, len(matches))
    return result


def enum_visible_windows(include_hidden=False):
    """Return a list of dicts describing top-level windows.

    By default only windows that are visible AND have a non-empty title are
    returned (these are almost always the ones a user would recognize as
    "the app's window"). Pass include_hidden=True to also include
    invisible/untitled top-level windows - useful for diagnosing apps that
    are minimized to the tray, cloaked, or otherwise not showing a normal
    window right now.
    """
    windows = []

    total_seen = 0
    included = 0

    def callback(hwnd, _):
        nonlocal total_seen, included
        total_seen += 1
        visible = win32gui.IsWindowVisible(hwnd)
        title = win32gui.GetWindowText(hwnd)
        if not include_hidden and not (visible and title):
            return True
        try:
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
            l, t, r, b = win32gui.GetWindowRect(hwnd)
            area = max(0, r - l) * max(0, b - t)
            windows.append({
                "hwnd": hwnd,
                "pid": pid,
                "title": title or "(no title)",
                "rect": (l, t, r, b),
                "area": area,
                "visible": visible,
            })
            included += 1
        except Exception as e:
            log.debug("enum_visible_windows: skipping hwnd=%s due to error: %s", hwnd, e)
        return True

    win32gui.EnumWindows(callback, None)
    log.debug("enum_visible_windows(include_hidden=%s): scanned %d top-level "
              "window(s), kept %d", include_hidden, total_seen, included)
    return windows


def find_target_windows(name=None, pid=None, title=None, include_hidden=False):
    """Find candidate top-level windows matching the given process name,
    pid, and/or window-title substring. When matching by name, prefers
    'root' processes (see get_root_pids_for_name) over child/helper
    processes of the same name, then sorts by window area (largest/main
    window first).
    """
    candidates = enum_visible_windows(include_hidden=include_hidden)
    log.debug("find_target_windows: %d window(s) before filtering", len(candidates))

    if pid is not None:
        candidates = [w for w in candidates if w["pid"] == pid]
        log.debug("find_target_windows: %d window(s) after pid=%s filter: %s",
                  len(candidates), pid, [(w["pid"], w["title"]) for w in candidates])

    if name is not None:
        root_pids = set(get_root_pids_for_name(name))
        name_lower = name.lower()
        matched = []
        for w in candidates:
            pname = get_process_name(w["pid"])
            is_match = (pname or "").lower() == name_lower
            log.debug("find_target_windows: hwnd=%s pid=%s process_name=%r "
                      "matches_target=%s", w["hwnd"], w["pid"], pname, is_match)
            if is_match:
                matched.append(w)
        log.debug("find_target_windows: %d window(s) after name=%r filter", len(matched), name)
        preferred = [w for w in matched if w["pid"] in root_pids]
        log.debug("find_target_windows: %d window(s) are on a 'root' pid %s",
                  len(preferred), sorted(root_pids))
        candidates = preferred if preferred else matched

    if title:
        title_lower = title.lower()
        before = len(candidates)
        candidates = [w for w in candidates if title_lower in w["title"].lower()]
        log.debug("find_target_windows: %d -> %d window(s) after title=%r filter",
                  before, len(candidates), title)

    candidates.sort(key=lambda w: w["area"], reverse=True)
    log.debug("find_target_windows: final candidate count=%d", len(candidates))
    return candidates


def sanitize_filename(name):
    return re.sub(r'[<>:"/\\|?*]', "_", name).strip() or "window"


def default_output_for_pid(pid, out_dir=None):
    """Default output path: <CWD or out_dir>/<processName>_<pid>.png"""
    proc_name = get_process_name(pid) or "process"
    base = os.path.splitext(proc_name)[0]  # drop .exe
    safe = sanitize_filename(base)
    directory = out_dir or os.getcwd()
    return os.path.join(directory, f"{safe}_{pid}.png")


class PreviewWindow(tk.Toplevel):
    """Shows the captured image with Save As / Copy / Close options."""

    def __init__(self, master, image: Image.Image):
        super().__init__(master)
        self.title("Snip Preview")
        self.image = image
        self.attributes("-topmost", True)

        from PIL import ImageTk
        display_img = image.copy()
        display_img.thumbnail((900, 700))
        self.tk_img = ImageTk.PhotoImage(display_img)

        tk.Label(self, image=self.tk_img).pack()

        btn_frame = tk.Frame(self)
        btn_frame.pack(fill="x", pady=6)

        tk.Button(btn_frame, text="Save As...", command=self.save_as).pack(side="left", padx=6)
        tk.Button(btn_frame, text="Copy to Clipboard", command=self.copy).pack(side="left", padx=6)
        tk.Button(btn_frame, text="Close", command=self.destroy).pack(side="right", padx=6)

        # Auto-save + auto-copy immediately
        ensure_snip_dir()
        self.autosave_path = default_filename()
        self.image.save(self.autosave_path)
        copy_image_to_clipboard(self.image)
        self.title(f"Snip Preview - saved to {self.autosave_path}")

    def save_as(self):
        path = filedialog.asksaveasfilename(
            defaultextension=".png",
            filetypes=[("PNG", "*.png"), ("JPEG", "*.jpg"), ("All files", "*.*")],
            initialdir=SNIP_DIR,
            initialfile=os.path.basename(default_filename()),
        )
        if path:
            self.image.save(path)
            messagebox.showinfo("Saved", f"Saved to {path}")

    def copy(self):
        copy_image_to_clipboard(self.image)


class RectangleSnipOverlay(tk.Toplevel):
    """Fullscreen transparent overlay for drag-to-select rectangle snipping."""

    def __init__(self, master, on_done):
        super().__init__(master)
        self.on_done = on_done
        left, top, right, bottom = get_virtual_screen_bounds()
        self.geometry(f"{right-left}x{bottom-top}+{left}+{top}")
        self.overrideredirect(True)
        self.attributes("-alpha", 0.35)
        self.attributes("-topmost", True)
        self.config(bg="black")
        self.config(cursor="cross")

        self.canvas = tk.Canvas(self, bg="black", highlightthickness=0, cursor="cross")
        self.canvas.pack(fill="both", expand=True)

        self.start_x = self.start_y = 0
        self.rect_id = None

        self.canvas.bind("<ButtonPress-1>", self.on_press)
        self.canvas.bind("<B1-Motion>", self.on_drag)
        self.canvas.bind("<ButtonRelease-1>", self.on_release)
        self.bind("<Escape>", lambda e: self.destroy())
        self.focus_force()

    def on_press(self, event):
        self.start_x, self.start_y = event.x, event.y
        self.rect_id = self.canvas.create_rectangle(
            self.start_x, self.start_y, self.start_x, self.start_y,
            outline="red", width=2
        )

    def on_drag(self, event):
        self.canvas.coords(self.rect_id, self.start_x, self.start_y, event.x, event.y)

    def on_release(self, event):
        x1, y1 = self.start_x, self.start_y
        x2, y2 = event.x, event.y
        left, top = min(x1, x2), min(y1, y2)
        right, bottom = max(x1, x2), max(y1, y2)

        vleft, vtop, _, _ = get_virtual_screen_bounds()
        abs_box = (left + vleft, top + vtop, right + vleft, bottom + vtop)

        self.destroy()
        if right - left > 2 and bottom - top > 2:
            self.after(100, lambda: self.on_done(abs_box))


class WindowSnipOverlay(tk.Toplevel):
    """Fullscreen transparent overlay that highlights the window under the
    cursor and captures it on click."""

    def __init__(self, master, on_done):
        super().__init__(master)
        self.on_done = on_done
        left, top, right, bottom = get_virtual_screen_bounds()
        self.vleft, self.vtop = left, top
        self.geometry(f"{right-left}x{bottom-top}+{left}+{top}")
        self.overrideredirect(True)
        self.attributes("-alpha", 0.25)
        self.attributes("-topmost", True)
        self.config(bg="black")

        self.canvas = tk.Canvas(self, bg="black", highlightthickness=0, cursor="hand2")
        self.canvas.pack(fill="both", expand=True)
        self.highlight_id = self.canvas.create_rectangle(0, 0, 0, 0, outline="red", width=3)

        self.current_hwnd = None

        self.canvas.bind("<Motion>", self.on_motion)
        self.canvas.bind("<Button-1>", self.on_click)
        self.bind("<Escape>", lambda e: self.destroy())
        self.focus_force()
        self._poll()

    def _poll(self):
        """Poll cursor position (more reliable than <Motion> alone for
        detecting windows below the transparent overlay)."""
        if not self.winfo_exists():
            return
        x, y = win32api.GetCursorPos()
        hwnd = get_toplevel_window_at(x, y)
        if hwnd and hwnd != win32gui.GetDesktopWindow():
            self.current_hwnd = hwnd
            try:
                l, t, r, b = win32gui.GetWindowRect(hwnd)
                self.canvas.coords(
                    self.highlight_id,
                    l - self.vleft, t - self.vtop, r - self.vleft, b - self.vtop
                )
            except Exception:
                pass
        self.after(50, self._poll)

    def on_motion(self, event):
        pass  # handled by polling for accuracy

    def on_click(self, event):
        hwnd = self.current_hwnd
        self.destroy()
        if hwnd:
            self.after(150, lambda: self.on_done(hwnd))


class MainApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Snip Tool")
        self.geometry("300x150")
        self.resizable(False, False)

        tk.Label(self, text="Choose a snip mode", font=("Segoe UI", 11)).pack(pady=10)

        tk.Button(self, text="Rectangle Snip", width=25, command=self.rectangle_snip).pack(pady=3)
        tk.Button(self, text="Window Snip", width=25, command=self.window_snip).pack(pady=3)
        tk.Button(self, text="Fullscreen Snip", width=25, command=self.fullscreen_snip).pack(pady=3)

    def rectangle_snip(self):
        self.withdraw()
        RectangleSnipOverlay(self, self._on_rectangle_captured)

    def _on_rectangle_captured(self, box):
        img = ImageGrab.grab(bbox=box, all_screens=True)
        self.deiconify()
        PreviewWindow(self, img)

    def window_snip(self):
        self.withdraw()
        WindowSnipOverlay(self, self._on_window_captured)

    def _on_window_captured(self, hwnd):
        try:
            img = capture_window(hwnd)
        except Exception as e:
            self.deiconify()
            messagebox.showerror("Capture failed", str(e))
            return
        self.deiconify()
        PreviewWindow(self, img)

    def fullscreen_snip(self):
        self.withdraw()
        self.after(200, self._do_fullscreen)

    def _do_fullscreen(self):
        box = get_virtual_screen_bounds()
        img = ImageGrab.grab(bbox=box, all_screens=True)
        self.deiconify()
        PreviewWindow(self, img)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def build_arg_parser():
    parser = argparse.ArgumentParser(
        prog="snip_tool.py",
        description="Capture a specific window by process name, PID, or title.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "Examples:\n"
            "  snip_tool.py --name notepad.exe\n"
            "  snip_tool.py --pid 12345\n"
            "  snip_tool.py --title \"Visual Studio Code\"\n"
            "  snip_tool.py --name \"Antigravity IDE.exe\" --list\n"
            "  snip_tool.py --name chrome.exe --output shot.png\n"
            "  snip_tool.py --interactive-window\n"
            "  snip_tool.py --interactive-rect\n"
            "  snip_tool.py --fullscreen\n"
            "  snip_tool.py --gui\n"
        ),
    )
    target = parser.add_argument_group("target selection")
    target.add_argument("-n", "--name", help="process/executable name, e.g. notepad.exe")
    target.add_argument("-p", "--pid", type=int, help="exact process ID")
    target.add_argument("-t", "--title", help="substring to match against the window title")

    actions = parser.add_argument_group("actions")
    actions.add_argument("-l", "--list", action="store_true",
                          help="list matching candidate windows instead of capturing")
    actions.add_argument("--include-hidden", action="store_true",
                          help="also consider hidden/untitled top-level windows "
                               "(useful for tray apps or when nothing normally matches)")
    actions.add_argument("--fullscreen", action="store_true",
                          help="capture the entire virtual screen (ignores target options)")
    actions.add_argument("--interactive-window", action="store_true",
                          help="hover-and-click window picker (ignores target options)")
    actions.add_argument("--interactive-rect", action="store_true",
                          help="drag-to-select rectangle picker (ignores target options)")
    actions.add_argument("--gui", action="store_true",
                          help="launch the full button-based GUI app")
    actions.add_argument("--include-frame", action="store_true",
                          help="capture the real, on-screen window INCLUDING its native "
                               "OS-drawn frame/title bar/caption buttons, instead of just the "
                               "window's own client-area content (PrintWindow never renders "
                               "the non-client chrome, so this is the only way to see what the "
                               "actual title bar looks like). The window is brought to the "
                               "foreground so it isn't occluded.")

    out = parser.add_argument_group("output")
    out.add_argument("-o", "--output",
                      help="output file path. Default: ./<processName>_<pid>.png in the "
                           "current working directory")
    out.add_argument("--no-clipboard", action="store_true",
                      help="don't copy the captured image to the clipboard")
    out.add_argument("--preview", action="store_true",
                      help="open a preview window after capturing")

    mirror = parser.add_argument_group(
        "RTL / mirrored window correction",
        "Windows with an RTL layout (e.g. Arabic/Hebrew UI) can capture "
        "horizontally mirrored via PrintWindow. By default this is "
        "auto-detected (WS_EX_LAYOUTRTL) and corrected automatically.",
    )
    mirror_group = mirror.add_mutually_exclusive_group()
    mirror_group.add_argument("--flip-horizontal", action="store_true",
                               help="force a horizontal flip correction, "
                                    "regardless of auto-detection")
    mirror_group.add_argument("--no-flip-fix", action="store_true",
                               help="disable the flip correction entirely, "
                                    "even if auto-detection says it's needed")

    parser.add_argument("--debug", action="store_true",
                         help="print verbose debug logging showing exactly which "
                              "processes/windows were found and filtered at each step")

    return parser


def print_candidates(candidates):
    if not candidates:
        print("No matching windows found.")
        return
    print(f"{'PID':>7}  {'Process':<28} {'Size':<11} {'Visible':<8} Title")
    print("-" * 90)
    for w in candidates:
        proc = get_process_name(w["pid"]) or "?"
        l, t, r, b = w["rect"]
        size = f"{r - l}x{b - t}"
        visible = "yes" if w.get("visible", True) else "no"
        print(f"{w['pid']:>7}  {proc:<28.28} {size:<11} {visible:<8} {w['title']}")


def capture_and_save(hwnd, pid, output=None, copy_clip=True, preview=False, tk_root=None,
                      flip_override=None, include_frame=False):
    log.debug("capture_and_save: hwnd=%s pid=%s output=%r copy_clip=%s preview=%s "
              "flip_override=%s include_frame=%s", hwnd, pid, output, copy_clip, preview,
              flip_override, include_frame)
    try:
        if include_frame:
            img = capture_window_with_frame(hwnd)
        else:
            img = capture_window(hwnd, flip_override=flip_override)
    except Exception:
        log.debug("capture_and_save: capture_window raised:\n%s", traceback.format_exc())
        raise

    if output:
        out_path = output
        out_dir = os.path.dirname(os.path.abspath(out_path))
        if out_dir:
            os.makedirs(out_dir, exist_ok=True)
    else:
        out_path = default_output_for_pid(pid)
    log.debug("capture_and_save: resolved output path=%r", out_path)

    img.save(out_path)
    print(f"Saved: {out_path}")

    if copy_clip:
        # Clipboard access needs a live Windows message loop context on some
        # systems; a hidden Tk root (if provided) keeps this reliable.
        try:
            copy_image_to_clipboard(img)
            print("Copied to clipboard.")
        except Exception as e:
            log.debug("capture_and_save: clipboard copy failed:\n%s", traceback.format_exc())
            print(f"(clipboard copy failed: {e})")

    if preview:
        root = tk_root or tk.Tk()
        root.withdraw()
        win = PreviewWindow(root, img)
        win.protocol("WM_DELETE_WINDOW", root.destroy)
        root.mainloop()

    return out_path


def resolve_flip_override(args):
    if getattr(args, "flip_horizontal", False):
        return True
    if getattr(args, "no_flip_fix", False):
        return False
    return None  # auto-detect


def _report_no_match(args):
    """Print a helpful diagnosis of why nothing matched, instead of just
    'not found' - e.g. distinguishing 'process doesn't exist' from 'process
    exists but has no visible/titled window right now'."""
    print("No matching window found.", file=sys.stderr)

    if args.pid is not None:
        if psutil.pid_exists(args.pid):
            proc_name = get_process_name(args.pid) or "?"
            print(f"  PID {args.pid} exists (process: {proc_name}), but has no "
                  f"visible top-level window with a title right now.", file=sys.stderr)
            hidden = find_target_windows(pid=args.pid, include_hidden=True)
            if hidden:
                print(f"  It does own {len(hidden)} hidden/untitled top-level "
                      f"window(s). Re-run with --include-hidden --list to see them.",
                      file=sys.stderr)
            else:
                print("  It doesn't own any top-level window at all (it may be "
                      "a background/tray-only process, or the window is a "
                      "child window rather than top-level).", file=sys.stderr)
        else:
            print(f"  PID {args.pid} is not currently running.", file=sys.stderr)

    if args.name:
        pids = get_root_pids_for_name(args.name)
        if pids:
            print(f"  Process '{args.name}' is running (pid(s): "
                  f"{', '.join(map(str, pids))}), but none of the candidates "
                  f"have a visible top-level window with a title right now.",
                  file=sys.stderr)
            hidden = find_target_windows(name=args.name, include_hidden=True)
            if hidden:
                print(f"  {len(hidden)} hidden/untitled window(s) exist for it. "
                      f"Re-run with --include-hidden --list to see them.",
                      file=sys.stderr)
        else:
            print(f"  No running process named '{args.name}' was found.", file=sys.stderr)

    if args.title and not args.pid and not args.name:
        print(f"  No visible window title contains '{args.title}'. "
              f"Try --include-hidden --list to see hidden/untitled windows too.",
              file=sys.stderr)


def run_cli(args):
    log.debug("run_cli: args=%s", vars(args))

    # --- Full GUI app ---
    if args.gui:
        ensure_snip_dir()
        MainApp().mainloop()
        return

    # --- Interactive pickers (still need a Tk event loop, but no menu window) ---
    if args.interactive_rect:
        root = tk.Tk()
        root.withdraw()

        def done(box):
            img = ImageGrab.grab(bbox=box, all_screens=True)
            out_path = args.output or os.path.join(
                os.getcwd(), datetime.now().strftime("Snip_%Y-%m-%d_%H-%M-%S.png")
            )
            img.save(out_path)
            print(f"Saved: {out_path}")
            if not args.no_clipboard:
                copy_image_to_clipboard(img)
                print("Copied to clipboard.")
            if args.preview:
                PreviewWindow(root, img)
            else:
                root.destroy()

        RectangleSnipOverlay(root, done)
        root.mainloop()
        return

    if args.interactive_window:
        root = tk.Tk()
        root.withdraw()

        def done(hwnd):
            _, pid = win32process.GetWindowThreadProcessId(hwnd)
            capture_and_save(hwnd, pid, output=args.output,
                              copy_clip=not args.no_clipboard,
                              preview=args.preview, tk_root=root,
                              flip_override=resolve_flip_override(args),
                              include_frame=args.include_frame)
            if not args.preview:
                root.destroy()

        WindowSnipOverlay(root, done)
        root.mainloop()
        return

    # --- Fullscreen ---
    if args.fullscreen:
        box = get_virtual_screen_bounds()
        img = ImageGrab.grab(bbox=box, all_screens=True)
        out_path = args.output or os.path.join(
            os.getcwd(), datetime.now().strftime("Snip_%Y-%m-%d_%H-%M-%S.png")
        )
        img.save(out_path)
        print(f"Saved: {out_path}")
        if not args.no_clipboard:
            copy_image_to_clipboard(img)
            print("Copied to clipboard.")
        if args.preview:
            root = tk.Tk()
            root.withdraw()
            PreviewWindow(root, img)
            root.mainloop()
        return

    # --- Target by name / pid / title ---
    if args.name or args.pid or args.title:
        candidates = find_target_windows(
            name=args.name, pid=args.pid, title=args.title,
            include_hidden=args.include_hidden,
        )

        if args.list:
            print_candidates(candidates)
            return

        if not candidates:
            _report_no_match(args)
            sys.exit(1)

        chosen = candidates[0]
        log.debug("run_cli: chosen candidate=%s", chosen)
        if len(candidates) > 1:
            print(f"Multiple candidate windows matched; using the largest "
                  f"(PID {chosen['pid']}, \"{chosen['title']}\"). "
                  f"Use --list to see all {len(candidates)} matches.")

        root = None
        if args.preview or not args.no_clipboard:
            # Tk root needed for clipboard reliability / preview window
            root = tk.Tk()
            root.withdraw()

        capture_and_save(
            chosen["hwnd"], chosen["pid"],
            output=args.output,
            copy_clip=not args.no_clipboard,
            preview=args.preview,
            tk_root=root,
            flip_override=resolve_flip_override(args),
            include_frame=args.include_frame,
        )
        if root and not args.preview:
            root.destroy()
        return

    # --- No target/action given: show help ---
    build_arg_parser().print_help()


if __name__ == "__main__":
    ensure_snip_dir()
    _parser = build_arg_parser()
    _args = _parser.parse_args()
    setup_logging(debug=_args.debug)
    log.debug("startup: sys.argv=%s", sys.argv)
    try:
        run_cli(_args)
    except SystemExit:
        raise
    except Exception:
        print("ERROR: an unexpected exception occurred.", file=sys.stderr)
        print(traceback.format_exc(), file=sys.stderr)
        print("Re-run with --debug for more detail on what led up to this.",
              file=sys.stderr)
        sys.exit(1)
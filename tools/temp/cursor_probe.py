import ctypes
from ctypes import wintypes

user32 = ctypes.windll.user32
user32.SetProcessDPIAware()

IDC_ARROW = 32512
IDC_HAND = 32649


class CURSORINFO(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.DWORD),
        ("flags", wintypes.DWORD),
        ("hCursor", ctypes.c_void_p),
        ("ptScreenPos", wintypes.POINT),
    ]


def cursor_name():
    ci = CURSORINFO()
    ci.cbSize = ctypes.sizeof(CURSORINFO)
    user32.GetCursorInfo(ctypes.byref(ci))
    h = int(ci.hCursor or 0)
    arrow = int(user32.LoadCursorW(None, IDC_ARROW) or 0)
    hand = int(user32.LoadCursorW(None, IDC_HAND) or 0)
    if h == hand:
        return "HAND"
    if h == arrow:
        return "ARROW"
    return f"OTHER(0x{h:x})"


import sys
import time

x, y = int(sys.argv[1]), int(sys.argv[2])
user32.SetCursorPos(x, y)
time.sleep(0.35)
print(f"@({x},{y}): {cursor_name()}")

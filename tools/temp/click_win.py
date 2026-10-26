import ctypes
import sys
import time
from ctypes import wintypes

user32 = ctypes.windll.user32
user32.SetProcessDPIAware()

FindWindowW = user32.FindWindowW
GetWindowRect = user32.GetWindowRect
SetCursorPos = user32.SetCursorPos
mouse_event = user32.mouse_event
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004

hwnd = FindWindowW(None, "MostaqlK")
if not hwnd:
    print("window not found")
    sys.exit(1)

user32.ShowWindow(hwnd, 9)  # SW_RESTORE
user32.BringWindowToTop(hwnd)
# Alt-key trick so SetForegroundWindow succeeds from a background process.
ctypes.windll.user32.keybd_event(0x12, 0, 0, 0)
user32.SetForegroundWindow(hwnd)
ctypes.windll.user32.keybd_event(0x12, 0, 2, 0)
time.sleep(0.4)

rect = wintypes.RECT()
GetWindowRect(hwnd, ctypes.byref(rect))
print(f"window rect: {rect.left},{rect.top} - {rect.right},{rect.bottom}")

x = rect.left + int(sys.argv[1])
y = rect.top + int(sys.argv[2])
SetCursorPos(x, y)
time.sleep(0.3)
mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
time.sleep(0.18)
mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
print(f"clicked at {x},{y}")

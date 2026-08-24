"""
Automated Visual Parity Test Runner for MostaqlK Mobile.
Runs each screen on emulator-5554, captures the screenshot, and scores similarity against the design baseline.
"""

from __future__ import annotations

import os
import subprocess
import sys
import time
from pathlib import Path
from PIL import Image
import numpy as np
from skimage.metrics import structural_similarity as ssim

PROJECT_ROOT = Path(__file__).resolve().parent.parent
TEMP_ROOT = PROJECT_ROOT / "tools" / "temp" / "mobile"
SIMILARITY_SCRIPT = PROJECT_ROOT / "tools" / "image_similarity.py"
PYTHON_EXE = PROJECT_ROOT / "tools" / ".venv" / "Scripts" / "python.exe"

PACKAGE = "com.mostaqlk"
ACTIVITY = f"{PACKAGE}/crc642205d7cf1821d852.MainActivity"

SCREENS = [
    "project-details",
    "project-details_dark",
    "more_closed",
    "more_opened",
    "more-bottomsheet-visible",
    "dashboard",
    "projects",
    "search",
    "more"
]

def capture_screen(screen_name: str) -> Path:
    out_dir = TEMP_ROOT / screen_name
    out_dir.mkdir(parents=True, exist_ok=True)
    raw_path = out_dir / "current_raw.png"
    cropped_path = out_dir / "current.png"
    
    print(f"\n--- Testing Screen: {screen_name} ---")
    
    # 1. Force stop app
    subprocess.run(["adb", "shell", "am", "force-stop", PACKAGE], check=True)
    time.sleep(1)
    
    # 2. Launch with intent extras
    default_page_arg = screen_name
    theme_arg = "dark" if screen_name.endswith("_dark") else "light"
    clean_page = screen_name.replace("_dark", "")
    extra_flags = []
    
    if clean_page == "more_opened":
        default_page_arg = "more-opened"
        extra_flags = ["-e", "more-opened", "1"]
    elif clean_page == "more_closed":
        default_page_arg = "more-closed"
        extra_flags = ["-e", "more-closed", "1"]
    elif clean_page == "more-bottomsheet-visible":
        default_page_arg = "more-bottomsheet-visible"
        extra_flags = ["-e", "more-bottomsheet-visible", "1"]
    elif clean_page == "project-details":
        default_page_arg = "project-details"
        extra_flags = ["-e", "project-id", "1300000"]
    else:
        default_page_arg = clean_page

    cmd = [
        "adb", "shell", "am", "start", "-S",
        "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER",
        "-n", ACTIVITY,
        "-e", "default-page", default_page_arg,
        "-e", "theme", theme_arg,
        "-e", "design-data", "1"
    ] + extra_flags
    subprocess.run(cmd, check=True)
    time.sleep(14) # allow layout, db init, and complete draw
    
    # 3. Screencap
    with open(raw_path, "wb") as f:
        subprocess.run(["adb", "exec-out", "screencap", "-p"], stdout=f, check=True)
        
    print(f"Captured raw screenshot to {raw_path}")
    
    # 4. Crop status bar (top 64px) and Android navigation bar (from Y=2270)
    with Image.open(raw_path) as img:
        w, h = img.size
        content = img.crop((0, 64, w, 2270))
        content.save(cropped_path)
        
    print(f"Prepared cropped image to {cropped_path}")
    return cropped_path

def run_similarity_score(screen_name: str, current_path: Path):
    design_path = TEMP_ROOT / screen_name / "design.png"
    if not design_path.exists():
        print(f"Design baseline {design_path} not found!")
        return
        
    heat_path = TEMP_ROOT / screen_name / "heat.png"
    
    cmd = [
        str(PYTHON_EXE),
        str(SIMILARITY_SCRIPT),
        str(design_path),
        str(current_path),
        "--resize-mode", "pad",
        "--regional-score", "4",
        "--palette",
        "--heatmap-out", str(heat_path)
    ]
    
    result = subprocess.run(cmd, capture_output=True, text=True)
    print(result.stdout)
    if result.stderr:
        print("Errors/Warnings:\n", result.stderr)

def main():
    screens_to_run = sys.argv[1:] if len(sys.argv) > 1 else SCREENS
    for screen in screens_to_run:
        curr = capture_screen(screen)
        run_similarity_score(screen, curr)

if __name__ == "__main__":
    main()

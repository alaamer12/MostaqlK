"""Capture all four MVP mockups in both light and dark theme states.

Assumes `.repertoire/design/mvp/` is being served (default http://localhost:8123).
Each capture lands in `tools/temp/<page>/design_<theme>.png` at a fixed viewport so the
app captures can be compared against it directly.

Usage:
  python tools/capture_designs.py
  python tools/capture_designs.py --pages projects --themes dark
"""

from __future__ import annotations

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from design_screenshot import capture  # noqa: E402

PAGES = ["projects", "project-details", "settings", "about"]
THEMES = ["light", "dark"]
TEMP_ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "temp")


def main():
    parser = argparse.ArgumentParser(description="Capture the MVP mockups in both themes.")
    parser.add_argument("--base-url", default="http://localhost:8123")
    parser.add_argument("--pages", nargs="*", default=PAGES, choices=PAGES)
    parser.add_argument("--themes", nargs="*", default=THEMES, choices=THEMES)
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=800)
    args = parser.parse_args()

    for page in args.pages:
        for theme in args.themes:
            out = os.path.join(TEMP_ROOT, page, f"design_{theme}.png")
            capture(
                url=f"{args.base_url}/{page}.html",
                output=out,
                width=args.width,
                height=args.height,
                full_page=False,
                wait_ms=500,
                theme=theme,
            )


if __name__ == "__main__":
    main()

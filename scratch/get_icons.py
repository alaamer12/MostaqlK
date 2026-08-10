import subprocess
import re
import pathlib

OUT = pathlib.Path("Resources/Images")
BASE = "https://cdn.jsdelivr.net/npm/@fortawesome/fontawesome-free@6.7.2/svgs"

# (style, fa-name, output file, fill colour)
WANTED = [
    ("solid", "filter", "icon_filter.svg", "#94A3B8"),
    ("solid", "pause", "icon_pause.svg", "#FFFFFF"),
    ("solid", "users", "icon_users.svg", "#94A3B8"),
    ("regular", "circle-check", "icon_circle_check.svg", "#2E9E6B"),
    ("solid", "circle-check", "icon_circle_check_verified.svg", "#22C55E"),
    ("regular", "clock", "icon_clock.svg", "#94A3B8"),
    ("regular", "clock", "icon_clock_amber.svg", "#D97706"),
    ("regular", "clock", "icon_clock_red.svg", "#DC2626"),
]

for style, name, out_name, fill in WANTED:
    url = f"{BASE}/{style}/{name}.svg"
    raw = subprocess.run(["curl", "-k", "-sS", url], capture_output=True, check=True).stdout.decode("utf-8")
    if not raw.startswith("<svg"):
        raise SystemExit(f"unexpected payload for {url}: {raw[:80]}")
    coloured = re.sub(r"^<svg", f'<svg fill="{fill}"', raw, count=1)
    (OUT / out_name).write_text(coloured, encoding="utf-8")
    print(out_name, len(coloured))

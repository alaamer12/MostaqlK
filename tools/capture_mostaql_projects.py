#!/usr/bin/env python3
"""Download Mostaql's projects page for manual parser inspection."""

from __future__ import annotations

import argparse
import re
import ssl
import urllib.request
from pathlib import Path

URL = "https://mostaql.com/projects"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default="scratch/mostaql-projects.html")
    args = parser.parse_args()
    request = urllib.request.Request(
        URL,
        headers={
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126 Safari/537.36",
            "Accept-Language": "ar,en;q=0.8",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30, context=ssl.create_default_context()) as response:
            data = response.read()
            charset = response.headers.get_content_charset() or "utf-8"
            html = data.decode(charset, errors="replace")
            print(f"HTTP {response.status}; charset={charset}; bytes={len(data)}")
    except Exception as exc:
        print(f"Could not download {URL}: {exc}")
        return 1

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(html, encoding="utf-8")
    print(f"Saved HTML to {output.resolve()}")
    links = re.findall(r"href=[\"']([^\"']*/project/[^\"']*)[\"']", html, flags=re.I)
    print(f"Project links: {len(set(links))}")
    for match in re.finditer(r"[^<>]{0,100}(?:عرض واحد|عرضان|عرضين|\d+\s+عروض?|أضف أول عرض)[^<>]{0,100}", html):
        print("Proposal markup:", " ".join(match.group(0).split()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
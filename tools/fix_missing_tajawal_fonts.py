#!/usr/bin/env python3
"""
Scans every .xaml file in the project for text-bearing controls (Label, Button, Entry,
Editor, SearchBar, Picker, DatePicker, TimePicker, RadioButton, Span) that do NOT end up
rendered in the Tajawal font family, and fixes them by adding an explicit
FontFamily="Tajawal"/"TajawalMedium"/"TajawalBold" attribute.

Why this is needed (see UNITS.md / Resources/Styles/Styles.xaml comment):
  - There is intentionally NO implicit `<Style TargetType="Label">` in this project - even one
    that only sets FontFamily crashes this app's unpackaged WinUI build at startup. So Labels/
    Spans/etc. only get Tajawal if FontFamily is set explicitly, per element.
  - Buttons DO have an implicit style (Styles.xaml, TargetType="Button" ApplyToDerivedTypes="True")
    but it sets FontFamily="OpenSansRegular", NOT Tajawal. Only buttons that explicitly opt into
    the AppButtonBase style (Resources/Styles/AppButtonStyle.xaml, Style="{StaticResource
    AppButtonBase}") get TajawalMedium that way; anything else needs an explicit FontFamily.
  - Entry has an explicit AppEntryBase style (FontFamily="Tajawal") plus an implicit
    Editor/DatePicker/... style using "OpenSansRegular" - same story.

This script is "smart" about detection: an element is considered "already Tajawal" if it has
  (a) an explicit FontFamily="Tajawal*" attribute, OR
  (b) a Style/StyleClass reference to a style already known (from Resources/Styles/*.xaml) to set
      FontFamily to a Tajawal* value (e.g. Style="{StaticResource AppButtonBase}").
Everything else affecting user-visible text is flagged and (with --apply) fixed by inserting an
explicit FontFamily attribute, following the exact convention already used across the codebase:
  - "TajawalBold"   for elements with FontAttributes="Bold" (headings, bold labels/buttons)
  - "TajawalMedium" for Button elements (buttons use Medium weight per the mockups)
  - "Tajawal"       for everything else (Label, Span, Entry, Editor, SearchBar, Picker, etc.)

Usage:
    python tools/fix_missing_tajawal_fonts.py            # dry-run report only
    python tools/fix_missing_tajawal_fonts.py --apply     # apply the fixes in-place
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Directories that must never be scanned/modified.
EXCLUDED_DIR_PARTS = {"bin", "obj", "temp-illustrative", "scratch", ".git"}

# Style resource dictionaries are not scanned for missing-font *elements* (they define
# <Style>/<Setter> constructs, not literal controls), but they ARE parsed once up front to
# learn which named styles already resolve to a Tajawal* FontFamily.
STYLE_FILES = [
    "Resources/Styles/AppButtonStyle.xaml",
    "Resources/Styles/AppEntryStyle.xaml",
]

# Tags whose Text/FormattedText content is user-visible and must render in Tajawal.
TARGET_TAGS = [
    "Label",
    "Button",
    "Entry",
    "Editor",
    "SearchBar",
    "Picker",
    "DatePicker",
    "TimePicker",
    "RadioButton",
    "Span",
]

TAG_ALTERNATION = "|".join(TARGET_TAGS)

# Matches a full opening tag (self-closing or not) for any of TARGET_TAGS, across lines.
# The lookahead after the tag name requires whitespace, '/' or '>' immediately next - this
# excludes XAML "property element" syntax like <Label.FormattedText> (which is NOT a Label
# instance, just a property container) that a plain `\b` word-boundary would incorrectly match.
TAG_RE = re.compile(
    rf"<(?P<tag>{TAG_ALTERNATION})(?=[\s/>])(?P<attrs>[^>]*?)(?P<selfclose>/?)>",
    re.DOTALL,
)

FONT_FAMILY_ATTR_RE = re.compile(r'FontFamily\s*=\s*"([^"]*)"')
STYLE_ATTR_RE = re.compile(r'\bStyle\s*=\s*"\{StaticResource\s+([^}]+)\}"')
BOLD_ATTR_RE = re.compile(r'FontAttributes\s*=\s*"[^"]*\bBold\b[^"]*"')
STYLE_KEY_RE = re.compile(r'<Style\s+x:Key="([^"]+)"[^>]*TargetType="([^"]+)"', re.DOTALL)
SETTER_FONTFAMILY_RE = re.compile(r'<Setter\s+Property="FontFamily"\s+Value="([^"]+)"\s*/>')


def is_excluded(path: Path) -> bool:
    return any(part in EXCLUDED_DIR_PARTS for part in path.parts)


def find_xaml_files() -> list[Path]:
    return sorted(p for p in ROOT.rglob("*.xaml") if not is_excluded(p))


def extract_tajawal_styles_from_text(text: str) -> set[str]:
    """Return the set of named styles (x:Key) in `text` that already set FontFamily to a
    Tajawal* value, whether declared in a shared Resources/Styles/*.xaml dictionary or inline in
    a page/component's own <ContentPage.Resources>/<ResourceDictionary> (e.g.
    PipelineDashboardPanel.xaml declares DashSectionLabelStyle/DashValueLabelStyle locally)."""
    tajawal_styles: set[str] = set()
    # Split into <Style ...> ... </Style> blocks and check each one individually.
    for match in re.finditer(r"<Style\s+x:Key=\"([^\"]+)\"[^>]*>(.*?)</Style>", text, re.DOTALL):
        key, body = match.group(1), match.group(2)
        setter_match = SETTER_FONTFAMILY_RE.search(body)
        if setter_match and setter_match.group(1).startswith("Tajawal"):
            tajawal_styles.add(key)
    return tajawal_styles


def load_tajawal_styles() -> set[str]:
    """Return the set of named styles (x:Key) from the shared Resources/Styles/*.xaml dictionaries
    that already set FontFamily to a Tajawal* value."""
    tajawal_styles: set[str] = set()
    for rel in STYLE_FILES:
        path = ROOT / rel
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8-sig")
        tajawal_styles |= extract_tajawal_styles_from_text(text)
    return tajawal_styles


def classify_variant(tag: str, attrs: str) -> str:
    if BOLD_ATTR_RE.search(attrs):
        return "TajawalBold"
    if tag == "Button":
        return "TajawalMedium"
    return "Tajawal"


def already_tajawal(attrs: str, tajawal_styles: set[str]) -> bool:
    # ANY explicit FontFamily attribute (even a non-Tajawal one, e.g. FontFamily="Consolas" used
    # deliberately for monospaced URL/code-like text) means a deliberate choice was already made
    # for this element - do not fight it or append a second FontFamily attribute (which would
    # produce invalid/ambiguous duplicate-attribute XAML). Only elements with NO FontFamily at
    # all (silently falling back to a non-Tajawal default/implicit style) are real bugs.
    if FONT_FAMILY_ATTR_RE.search(attrs):
        return True
    style_match = STYLE_ATTR_RE.search(attrs)
    if style_match and style_match.group(1).strip() in tajawal_styles:
        return True
    return False


def insert_font_family(tag: str, attrs: str, selfclose: str, variant: str) -> str:
    # Insert right after the tag name, before existing attributes, matching this codebase's
    # convention of listing FontFamily early (e.g. `<Label Text="..." FontFamily="Tajawal" .../>`
    # was applied as `Text="..." FontFamily="..."` right after the first attribute in prior
    # fixes) - simplest and safest is appending it at the end of the attribute list.
    new_attrs = attrs
    if not new_attrs.endswith(" ") and new_attrs.strip():
        new_attrs = new_attrs.rstrip()
        new_attrs = f'{new_attrs} FontFamily="{variant}"'
    else:
        new_attrs = f'{new_attrs}FontFamily="{variant}" '
    return f"<{tag}{new_attrs}{selfclose}>"


def process_file(path: Path, tajawal_styles: set[str], apply: bool) -> list[str]:
    raw_bytes = path.read_bytes()
    has_bom = raw_bytes.startswith(b"\xef\xbb\xbf")
    original = raw_bytes.decode("utf-8-sig")
    text = original
    report: list[str] = []
    offset = 0

    # Merge in any Tajawal-resolving styles declared locally in this very file (e.g. a page's own
    # <ContentPage.Resources>), on top of the shared Resources/Styles/*.xaml ones.
    local_tajawal_styles = tajawal_styles | extract_tajawal_styles_from_text(original)

    for match in TAG_RE.finditer(original):
        tag = match.group("tag")
        attrs = match.group("attrs")
        selfclose = match.group("selfclose")

        if already_tajawal(attrs, local_tajawal_styles):
            continue

        variant = classify_variant(tag, attrs)
        line_no = original.count("\n", 0, match.start()) + 1
        report.append(f"  line {line_no}: <{tag}> missing Tajawal font -> {variant}")

        if apply:
            new_tag = insert_font_family(tag, attrs, selfclose, variant)
            start = match.start() + offset
            end = match.end() + offset
            text = text[:start] + new_tag + text[end:]
            offset += len(new_tag) - (match.end() - match.start())

    if apply and report:
        encoded = text.encode("utf-8")
        if has_bom:
            encoded = b"\xef\xbb\xbf" + encoded
        path.write_bytes(encoded)

    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="apply fixes in-place (default: dry-run report only)")
    args = parser.parse_args()

    tajawal_styles = load_tajawal_styles()
    files = find_xaml_files()

    total_issues = 0
    touched_files = 0

    for path in files:
        rel = path.relative_to(ROOT)
        # Skip pure style/resource dictionaries: they define <Style>/<Setter>, not literal
        # controls, so the TAG_RE never matches real UI elements there in a meaningful way,
        # but skip explicitly for clarity/perf.
        if "Resources\\Styles" in str(rel) or "Resources/Styles" in str(rel):
            continue

        report = process_file(path, tajawal_styles, args.apply)
        if report:
            touched_files += 1
            total_issues += len(report)
            print(f"{rel}")
            for line in report:
                print(line)
            print()

    action = "Fixed" if args.apply else "Found"
    print(f"{action} {total_issues} missing-Tajawal element(s) across {touched_files} file(s).")
    if not args.apply and total_issues:
        print("Re-run with --apply to fix them in-place.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

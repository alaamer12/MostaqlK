# fontawesome-icon-bundler / icon-rendering-fixer

## Goal
Resolve the outstanding "icons render as empty tofu boxes" bug in `AppSidebar` by any means
necessary, as explicitly requested ("resolve the icon problem in all possible ways").

## Root cause investigation (font approach)
The previous session's `AppIcon` implementation rendered FontAwesome 6 Free glyphs via a
`Label` + bundled `.ttf` fonts + Unicode codepoints. Despite the font files, codepoints, and
wiring all being verified correct, glyphs rendered as empty boxes. This session methodically
diagnosed why, ruling out each hypothesis in turn:

1. **Fonts corrupted?** Ruled out — built a standalone Playwright/HTML test
   (`@font-face` against the actual bundled `.ttf` files) and confirmed all icon glyphs render
   correctly in a browser.
2. **`fonts.AddFont` alias not resolving on unpackaged Windows?** Tried bypassing it with the
   documented raw `"<file>.ttf#<internal family name>"` WinUI `FontFamily` string (both
   relative and, later, absolute-path forms) — still tofu.
3. **Is the fix code even running?** Added a debug-log write inside a custom
   `LabelHandler.Mapper` hook (bypassing MAUI's font-resolution pipeline entirely, per
   Telerik's documented workaround for the same class of bug) — confirmed via log file that the
   mapper *does* fire and *does* set the correct native `FontFamily` value every time — yet the
   glyph still rendered as an empty box.

Conclusion: this is a genuine WinUI/unpackaged-app limitation for loading custom font files at
runtime that could not be worked around within a reasonable number of attempts. Documented in
`UNITS.md` and abandoned in favor of a different rendering mechanism entirely.

## Fix implemented (image approach)
1. Downloaded 6 official FontAwesome 6.7.2 Free SVG icons (list-check, magnifying-glass, bell,
   gear, circle-info, moon) from the official npm package via jsDelivr CDN into
   `Resources/Images/icon_*.svg`.
2. Since a raster PNG can't be live-tinted, pre-baked two colored variants of the 5 nav icons
   (moon excluded — never shown active) by setting an explicit `fill="#475569"` (inactive) /
   `fill="#2563EB"` (active) attribute directly on each SVG's root element, producing
   `icon_*.svg` (inactive) and `icon_*_active.svg` (active) pairs.
3. Rewrote `AppIcon` from a `Label` subclass to a `ContentView` wrapping an `Image`, keeping the
   exact same public API (`Icon`, `FontSize`, `TextColor` bindable properties) so `AppSidebar`'s
   XAML/code-behind needed zero changes.
4. `AppIconGlyphExtensions.ToImageSource(icon, textColor)` resolves which pre-baked PNG variant
   to load, comparing `textColor` against the app's known active color (`#2563EB`).
5. **Second resolution bug found and fixed**: MAUI's plain resource-name `Image.Source` string
   form (tried both `"icon_bell"` and `"icon_bell.svg"`) silently failed to load the image on
   this unpackaged Windows build — same root-cause class as the font issue. Confirmed via an
   isolated diagnostic (a known-good bundled image, `dotnet_bot.png`, rendered fine through the
   identical code path, proving the `Image` mechanism itself works; the new SVG-derived
   resources specifically did not resolve by name). The generated PNG files themselves were
   independently verified visually correct by opening them directly.
6. **Actual fix**: load via `ImageSource.FromFile(Path.Combine(AppContext.BaseDirectory,
   "icon_bell.scale-200.png"))` — an absolute file path bypassing MAUI's resource-alias lookup
   entirely. Confirmed working via diagnostic screenshot, then wired into the real
   `ToImageSource` implementation for all 6 icons with active/inactive swapping.

## Files touched
- `UI/PlatformComponents/AppIcon/AppIcon.cs` — rewritten (`Label` → `ContentView`/`Image`).
- `UI/PlatformComponents/AppIcon/AppIconGlyphExtensions.cs` — added `ToImageBaseName`/
  `ToImageSource`; kept `ToFontFamily`/`ToUnicode` for reference.
- `UI/PlatformComponents/AppIcon/AppIcon.Windows.cs` — deleted (font-handler-mapper approach,
  no longer needed).
- `Resources/Images/icon_list_check.svg`, `icon_list_check_active.svg`,
  `icon_magnifying_glass.svg`, `icon_magnifying_glass_active.svg`, `icon_bell.svg`,
  `icon_bell_active.svg`, `icon_gear.svg`, `icon_gear_active.svg`, `icon_circle_info.svg`,
  `icon_circle_info_active.svg`, `icon_moon.svg` — new, downloaded + colored.
- `UNITS.md` — `AppIcon` row rewritten to document the final mechanism and the full history.

## Verification
- `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` → 0 warnings, 0 errors, after
  every change (including a full clean rebuild to rule out stale caching).
- Launched the app and screenshotted via `tools\snip_tool.py` repeatedly across the
  investigation (`tools\temp\app_final_icons_v5.png` through `v9.png`); `v9` confirms all 5
  sidebar nav icons plus the dark-mode moon icon now render correctly, with the active
  "المشاريع" row showing its icon in the correct blue (`#2563EB`) and the rest in gray
  (`#475569`).
- Ran a standalone Playwright/HTML sanity check (`scratch/fa_test.html`) to independently
  confirm the underlying font files were valid before abandoning the font approach (deleted
  after use, per scratch-file cleanup policy).

## Notes / remaining scope
- Only the 6 icons used by `AppSidebar` have real artwork; other `AppIconGlyph` enum values
  (used nowhere yet) fall back to the "info" icon's image — documented in `UNITS.md`. Applying
  `AppIcon` to `ProjectCard`/`ProjectDetailsPage`/`SearchInputField` remains a follow-up.
- The `ToFontFamily`/`ToUnicode` extension methods and the `AppIconGlyph.ToFontFamily`-driven
  codepoint mapping are now unused dead code, kept only as historical reference per the doc
  comment; could be removed in a future cleanup pass.
- The fix hardcodes `.scale-200.png` as the loaded resolution; this is fine for this app's
  small icon sizes but is not DPI-aware — a future improvement could pick the nearest scale
  bucket based on the display's actual DPI.

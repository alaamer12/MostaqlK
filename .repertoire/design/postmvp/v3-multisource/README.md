# V3 — Source-Morph UI (Mostaql + Nafezly)

> **→ Implementation requirements live in
> [`.repertoire/.steering/v3/product/multi-source-requirements.md`](../../.steering/v3/product/multi-source-requirements.md)**
> — read it before writing any code; it supersedes improvisation.


Design for v3: the app watches multiple platforms, and **each source is a full
identity, not a filter**. A minimized icon rail (left) holds the sources; clicking
one morphs the *entire application* — brand colors, chrome, content, footer — into
that platform via a **circular reveal (radial reveal / circular clip-path
transition)** that expands from the clicked icon, with a brand-colored orbit ring
riding the wavefront. Two 1-desktop apps in one frame.

> **Status: design proposal for review.** All numbers, titles, budgets and stats
> are illustrative design data.

## Files

| File | What to try |
|------|-------------|
| [`projects.html`](./projects.html) | Click **ن** (Nafezly tile) in the rail → whole app morphs blue→orange with the circular reveal from the tile; click the moon button → same reveal for light/dark. Feeds carry source-native fields (`ر.س · مدة التسليم` vs `$ · العروض المقدمة · مشرف عليه`). |
| [`project-details.html`](./project-details.html) | Continuous full-page pattern (same as Settings): sticky section headers, no floating cards. Title = the scraped project title; decision strip (big-number facts) → الوصف → معلومات المنفذ (Nafezly-only section) → صاحب المشروع → actions. The pipeline-provenance card was removed as noise. |
| [`settings.html`](./settings.html) | Rail morphing works here too; المصادر section manages per-source state (enable, cadence, rate budget, session) — switching lives in the rail, management lives in Settings. |
| [`settings-shapes.html`](./settings-shapes.html) | **Shape catalog** for the settings plugin system: every `SettingsShape` the descriptor renderer can produce (Toggle, NumericBounded, TextEntry, EnumPicker, CookieEditor, ReadOnlyStat, ActionButton, LinkOut, InfoBanner, SectionHeader, Slider, MultiChoiceChips, SegmentedButtons, FilePicker), each in its states (valid / clamped / error / disabled / partial-override). The mono eyebrow above every Arabic title is the descriptor's enum name — one row ⇄ one descriptor. Force params: `?shape=cookie&state=error&theme=dark&source=nafezly` isolates any cell for 1280×800 capture. This page is the spec the CRAFT step (`SettingsShapesPage` in MAUI) must clone 1:1. |

## The morph mechanics (ported from the magicui `AnimatedThemeToggler`)

- `document.startViewTransition()` swaps state; the new frame is revealed with a
  `clip-path: circle()` WAAPI animation pinned to `::view-transition-new(root)`.
- **Percentage coordinates, not px** — Chrome misplaces px clip-path on the
  pseudo-element at fractional display scales (Windows 150%) for the first
  transition after load. `circle()` radius normalized by `hypot(w,h)/√2`.
- **The orbit ring**: a `.morph-ring` div (3px border + glow in `var(--brand)`)
  is created *inside* the transition callback with `view-transition-name: ring`,
  so it renders as its own VT group — excluded from the root snapshot, animated
  by `ring-grow` keyframes (scale 0.02→1, fade at the end) synced to the clip via
  `--morph-dur`. The ring's edge rides the reveal boundary and dissolves at the
  viewport corner. (Verified by screenshot: glowing brand circle mid-reveal.)
- Re-entrancy guard + `localStorage` persistence (`mostaqlk-source`,
  `mostaqlk-theme`); every page opens wearing the remembered identity.
- **Mount/demount polish (no "breaking glass" flash):** the VT group duration is
  pinned to `--morph-dur` and the collapsed clip to `--morph-clip-from` in CSS
  *before* the snapshot, so the new content can never paint one unclipped frame
  over the old (the shatter artifact). Content then lands with a transform-only
  `card-settle` (8px rise, 50ms stagger) — no opacity animation, because the
  snapshot would freeze an invisible frame. `prefers-reduced-motion` skips the
  whole morph and swaps instantly.

## Identity system

Two independent axes: `html[data-source]` = hue (which app you're in),
`.dark` = value (light/dark). All chrome reads `--brand / --brand-ink /
--brand-soft / --brand-on`; state colors (green = enriched/healthy, red = failed,
amber = pending) never change with the source — **source color is identity, state
color is truth**.

| Token | Mostaql (light/dark) | Nafezly (light/dark) |
|-------|----------------------|----------------------|
| `--brand` | `#2386C8` / `#5CA8DE` | `#FFA200` / `#FFB84D` |
| `--brand-ink` (small text) | `#1A6DA6` / `#5CA8DE` | `#9A6100` / `#FFB84D` |

Nafezly colors grabbed live from their CSS (`--bg-color-4: #ffa200`). Their
primary `#0194fe` was rejected: indistinguishable from Mostaql blue at badge size.

## Pipeline panel (reserved location)

`projects.html` reserves the home of the **shipped v1 units** `PipelineDashboardPanel`
+ `PipelineRadar` + `SplitterHandle` — right of the feed, mocked in both states:
expanded (radar, discovery/queue cards, three worker rows) and collapsed to the
44px status rail (worker dots + backlog %, never a full hide), with working
collapse toggle + persistence. v3 change: the panel shows the **active source's**
pipeline — the source chip and radar accents re-tint with the morph. The mock's
drag handle is decorative; the real unit resizes.

## Decisions

1. **Icon rail (64px, icons only, physical left as requested)** replaces the
   256px sidebar. Note: RTL convention would place it on the right — one-line
   flip if you change your mind.
2. **Clicking a source morphs everything** — no tabs, no unified feed. Each
   source is a module that can grow its own logic (the dashed "مطابقة 87٪" pill
   is a labeled concept slot for per-source ML-style features).
3. **Source management stays folded into Settings**; the rail only switches.
4. Light/dark toggle uses the same circular reveal from its own rail button.
5. `$` budgets render `dir="ltr"` so RTL doesn't mirror them.
6. **`settings-shapes.html` joins the parity matrix only when its MAUI twin
   exists.** `v3_parity.py` launches the app with `--default-page=<page>`, so
   adding the gallery to `PAGES` before the CRAFT step ships `SettingsShapesPage`
   would fail every harness run. The page is capture-able today via
   `design_screenshot.py --url .../settings-shapes.html?shape=&state=&theme=&source=`;
   the `PAGES` entry lands with step 3.

## Unit impact (for implementation; UNITS.md gets rows then)

New units implied: `SourceRail` (replaces `AppSidebar`), `SourceMorphTransition`
(Core helper: VT + clip + ring, honoring `MotionPreferences` — reduced motion =
instant swap, no ring), `BrandTokens` (extends `DesignTokens` with per-source
palettes), `SourceLinkButton` (rename of `MostaqlLinkButton`).

## Open questions

1. Rail on left (as asked) vs RTL-native right?
2. Ring weight: 3px + glow now — bolder (thicker / filled disc flash) or keep subtle?
3. Should toasts/notifications carry the source brand color, or stay neutral chrome?

## CDN note

Mockups load Tailwind/FontAwesome from CDNs without SRI, matching every existing
file in `design/mvp/`. Review artifacts only; the MAUI app renders these states
from DesignTokens, not from this HTML.

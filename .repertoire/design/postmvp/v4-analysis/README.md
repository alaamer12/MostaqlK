# V4 — Analysis surfaces (Mostaql + Nafezly)

> **→ Implementation requirements live in
> [`.repertoire/.steering/v4/competitor-analysis-requirements.md`](../../.steering/v4/competitor-analysis-requirements.md)**,
> indexed by [`.steering/v4/README.md`](../../.steering/v4/README.md) — read them before writing any code;
> they supersede improvisation.

Design for v4: the app already watches both platforms; v4 makes it **know the user** — their skills, their
band, their history — and turns every surface into analysis *relative to that profile*. Each source stays a
full identity, not a filter, and the morph grammar of v3 carries over unchanged.

> **Provenance rule for this folder (D50).** A page enters `v4-analysis/` the **first time v4 changes it**.
> Until then v4 pages link across to `../v3-multisource/<page>.html` rather than copying it — so the shipped
> design stays single-sourced and nothing drifts. `v3-multisource/` and `.steering/v3/` were restored to
> their shipped state on 2026-11-05; **no v4 work was merged back into them.**

> **Status: design proposal for review.** All numbers, titles, budgets and stats are illustrative design
> data, except where the v4 requirements doc marks a figure as measured from a captured page.

## Files (v4-native)

| File | What to try |
|------|-------------|
| [`profile.html`](./profile.html) | The personal file. Six site-read cards beside the avatar ring; `?cookies=false` shows the **key-offer** state (the camera overlay is live — clicking it opens the OS file picker) and the manual fields; `?reread=busy\|ok\|fail` renders the read as **a page of its own** — dial and per-page log while running, the gate scene + read log on failure, a fading banner on success. `?source=mostaql` swaps brand, routes and the profile's own field set. |
| [`projects.html`](./projects.html) | The v4 layer over the shipped feed: **avatar at the rail head** as the only route to the profile (`?me=photo\|initials\|none`), **score pills** on cards (`?profile=true` to compare against the shipped look), and the reversed chrome — rail on the physical right, radar panel on the left (*Chrome geometry* below). |
| [`bidders.html`](./bidders.html) | Who else is in this project. Rival rows, threat ordering, and the batch-resolution semantics of R4.13 — the «13 متقدماً لم يُحلَّل» affordance is gone by ruling. |
| [`performer-profile.html`](./performer-profile.html) | A rival's own file — **about him, never about the viewer** (reversal R1). No `?profile=true` mode exists on this surface. |

## Shipped surfaces v4 links to, deliberately not duplicated here

[`../v3-multisource/project-details.html`](../v3-multisource/project-details.html) ·
[`../v3-multisource/settings.html`](../v3-multisource/settings.html) ·
[`../v3-multisource/settings-shapes.html`](../v3-multisource/settings-shapes.html) ·
[`../v3-multisource/onboarding.html`](../v3-multisource/onboarding.html) —
their behaviour, states and force-params are documented in
[`../v3-multisource/README.md`](../v3-multisource/README.md). v4 touches three of them on purpose
(project-details for the factor breakdown, settings for the `session-cookie` row, onboarding step 5 for the
key artwork); when one is edited, it is copied here and the links are pulled back.

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

## Chrome geometry — the look, ruled 2026-11-04

The frame is: **rail on the physical right (reading-start), pipeline/radar panel on
the physical left, feed column between them.** Outer wrapper `direction: rtl`; the
rail is its first child, so it lands at the right edge; inside the content row the
visual order is set by CSS `order` — `.feed-col: 1`, `.splitter: 2`,
`.pipeline-panel: 3` — so the panel docks left **while the feed stays first in the
DOM** (tab and screen-reader order unchanged). Hairlines face content, not the
screen edge: `.rail` keeps `border-inline-end`, `.pipeline-panel` uses
`border-inline-start`. Measured at 1440×900: rail 1376–1440, panel 0–252, splitter
252–258, feed 258–1376; no horizontal overflow at 1440/1100/900.

**All four v4 surfaces now carry it** — measured 2026-11-05, rail at 1376–1440 and
`main` at 0–1376 in every one: `profile.html` already had the rail on the right; the
LTR wrapper was removed from `bidders.html` and `performer-profile.html`; `projects.html`
took the flip plus the panel re-order. Because the rail and panel hairlines are logical
(`border-inline-end` / `border-inline-start`), they kept facing content through the flip
without a single edit — verified 1px solid on all four.
The one physical property left in these pages is `.info .tipbox { right: 0 }`, which is
correct either way (it anchors the tooltip to the chip's right edge so the box opens
leftward into the column); measured inside the viewport on both pages that use it.
Reversal in the mockup is exactly three things: the wrapper direction, the three `order`
values in `projects.html`, and the panel's border side.

**The shipped app is the opposite, and matching it is not three lines.** Verified
in code, not from this document: `MainWindowWindowsLayout.xaml:15` sets the shell
root `FlowDirection="RightToLeft"`, and its column tracks
(`:42-47`) are `[0]=pipeline panel 252 · [1]=splitter 8 · [2]=feed * ·
[3]=SourceRail` — in RTL, column 0 is the **right** edge and column 3 the **left**,
so today the app renders **rail physical-left, radar panel physical-right**. The
same last-column rail pattern is in `ProjectDetailsWindowsLayout.xaml:33`
(`Grid.Column="1"`) and `SettingsPanelWindowsLayout.xaml`. What has to move:

1. re-order the column tracks in each Windows layout so the rail is column 0 and
   the panel is last — `SourceRail Grid.Column` indices change in **three** layouts;
2. the header band spans `Grid.Column="0" ColumnSpan="3"`
   (`MainWindowWindowsLayout.xaml:63-64`) and its indices shift with the tracks;
3. **the code-behind couples to those indices** — the comment at `:40-41` says
   "Code-behind keeps col 0/1 in step with the panel's live width", so the live
   resize wiring is re-indexed, not just the XAML;
4. rail width itself stays one constant (`UI/PlatformComponents/SourceRail/RailWidths.cs`,
   noted at `MainWindowWindowsLayout.xaml.cs:159`) — untouched by this.

Until that is done, the mockups assert a chrome the app does not have. That is a
deliberate gap, not an error in the mockups.

## Pipeline panel (reserved location)

`projects.html` reserves the home of the **shipped v1 units** `PipelineDashboardPanel`
+ `PipelineRadar` + `SplitterHandle` — **left of the feed** (it was right of the feed
until the 2026-11-04 chrome ruling above), mocked in both states:
expanded (radar, discovery/queue cards, three worker rows) and collapsed to the
44px status rail (worker dots + backlog %, never a full hide), with working
collapse toggle + persistence. v3 change: the panel shows the **active source's**
pipeline — the source chip and radar accents re-tint with the morph. The mock's
drag handle is decorative; the real unit resizes.

## Decisions

1. **Icon rail (64px, icons only) on the physical RIGHT — REVERSED 2026-11-04.**
   It replaced the 256px sidebar and originally sat on the physical **left**, by an
   explicit earlier request; the owner saw the flipped build and ruled the right
   edge as the new look, because that is where `profile.html` already put it and
   where Arabic reading-start expects navigation. See *Chrome geometry* above for
   the exact mechanics and the MAUI-side consequence. The old "one-line flip if you
   change your mind" note is now spent — the flip happened, and it was three lines.
2. **Clicking a source morphs everything** — no tabs, no unified feed. Each
   source is a module that can grow its own logic (the dashed "مطابقة 87٪" pill
   is a labeled concept slot for per-source ML-style features).
3. **Source management stays folded into Settings**; the rail only switches.
4. Light/dark toggle uses the same circular reveal from its own rail button.
5. `$` budgets render `dir="ltr"` so RTL doesn't mirror them.
6. **`settings-shapes.html` joined the parity matrix with its MAUI twin.**
   `Features/Settings/Views/SettingsShapesPage` (`--default-page=settings-shapes`)
   renders the same fixture through `SettingsRow` alone, and
   `v3_parity.py --shapes` runs the gallery matrix: 4 full frames (source ×
   theme) plus one isolated frame per shape, driven by `?shape=`/`?state=` on
   this side and `--parity-shape=`/`--parity-state=` on the app side. Measured
   structure similarity 94.7–95.2% (SSIM ~92%, palette ~99.5%); the raw 1:1
   blend sits at ~75% because its MSE term cannot cross text rasterizers —
   the shipping `settings` page scores 69% by that same number.

7. **The settings page split into two bands: النظام and المنصة.**
   `settings.html` used to show one source-scoped section plus a `عام` section
   whose rows were a mix of app behavior and per-site values, and the app had
   grown past both of them. The bands now state the rule the code enforces: a
   **system** row holds one value shared by every enabled platform (scan
   interval, safe-request spacing, parallel detail fetches, notification
   grouping enable/mode/threshold, startup, remember-last-platform, theme,
   close behavior, and the new «المنصات» switch list), while a **platform** row
   is a property of one site (its rate budget, query params, attachments, its
   own notifications, session cookie, and the capability read-outs). `حد
   الطلبات` staying per-platform is deliberate: each pipeline owns a private
   rate limiter, so a budget spent on one site says nothing about another.
   `الإجراءات` (the deferred-write save) is now drawn too, because the app
   ships it. Measured after the redraw: structure similarity 91.1–92.2% and raw
   70.1–71.4% across the four `settings` frames — both above the pre-split
   baseline (90.8% / 69.9%).

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

# Design system

[← Back to wiki home](./README.md)

Visual language for the app — complements [ui-ux-design.md](./ui-ux-design.md), which covers layout and interaction; this doc covers the design tokens those layouts are built from.

## Table of contents
- [Color palette](#color-palette)
- [Light / dark theme](#light--dark-theme)
- [RTL support](#rtl-support)
- [Fluid cards — content-driven sizing](#fluid-cards--content-driven-sizing)
- [Typography](#typography)
- [Icons](#icons)
- [Onboarding illustrations](#onboarding-illustrations)
- [Icon system — three tiers](#icon-system--three-tiers)
- [Component base](#component-base)

## Color palette

Two-hue system: **Mostaql blue** as the primary brand/accent color (matches the source platform, so the app feels visually related to what it's watching), paired with a **nature/health green** as a secondary accent — used for positive/success states (enriched, live, connected) so blue and green never compete for the same meaning.

| Role | Light mode | Dark mode | Usage |
|---|---|---|---|
| Primary / brand | `#2386C8` (Mostaql blue) | `#5CA8DE` (lightened for contrast) | Active nav, links, primary buttons, unread accent bar |
| Secondary / positive | `#2E9E6B` (nature green) | `#4FBF8C` | "Live" status dot, enriched badge, success toasts |
| Warning | amber | amber (lightened) | Pending enrichment badge |
| Danger | red | red (lightened) | Failed enrichment, errors |
| Surface / text | neutral grayscale | inverted neutral grayscale | Backgrounds, body text, borders |

**Rule:** blue = brand/identity/primary action, green = state/health/positive — never mixed for the same signal, so a card's accent color always means one specific thing at a glance.

## Light / dark theme

- Both themes ship at v1, togglable in settings (with a "match system" default).
- All colors are defined as semantic tokens (`--surface`, `--text-primary`, `--border`, `--accent-primary`, `--accent-positive`, etc.) — never hardcoded hex in components — so theme switching is a token-swap, not a per-component rewrite.
- Contrast checked independently per theme, not just inverted — the blue/green pair above are already tuned with lighter variants for dark mode rather than reusing the light-mode values on a dark background.

## RTL support

Arabic is the primary content language (project titles, descriptions, owner names), so RTL is a native layout mode, not a mirrored/patched-on afterthought:

- Use logical CSS properties throughout (`margin-inline-start`, `padding-inline-end`, `border-inline-start`) instead of physical ones (`margin-left`, `border-left`) — so flipping `dir="rtl"` vs `dir="ltr"` on the root re-flows correctly without per-component overrides.
- The unread accent bar (currently described as a "left edge" in [ui-ux-design.md](./ui-ux-design.md#unreadread-highlighting)) is actually an inline-start edge — appears on the right in RTL, left in LTR, automatically.
- Direction is set per-content, not globally forced: Arabic project text renders RTL, while numeric/English metadata (proposal counts, dates, category tags if in English) stays correctly embedded via Unicode bidi isolation rather than fighting the surrounding direction.
- App chrome (settings, buttons, menus) follows the user's OS/locale language direction; project card content follows the *project's* actual script.

## Fluid cards — content-driven sizing

Cards, boxes, and list rows size to their content rather than being clipped to a fixed height:

- No fixed-height truncation with `overflow: hidden` on titles/descriptions in the main feed — text wraps naturally, card height grows with content.
- Grid/list layouts use `auto` row sizing so a two-line Arabic title and a one-line English title sit naturally at their own heights in the same list, without forcing uniform card sizes that either clip long text or waste space on short text.
- Long unbroken strings (owner names, long English URLs) get `overflow-wrap: break-word` so they don't force horizontal scroll or blow out the card width.
- This applies to the [detail view](./ui-ux-design.md#main-window-layout) especially — full descriptions can be long, and the layout should expand to fit rather than scroll-boxing arbitrarily.

## Typography

- **Arabic:** Lyra El-Mesry as the primary Arabic typeface for project content (titles, descriptions) — chosen for readability at body-text sizes rather than a display/decorative face.
- **Latin/numerals:** a clean grotesque paired to sit well alongside Lyra El-Mesry at matching x-height/weight, used for English metadata, numbers, dates, and app chrome.
- Both fonts loaded with appropriate `font-display` fallback stacks so text isn't invisible during load, and a sane system-font fallback if the custom font fails to load entirely.

## Icons

**Tabler Icons** — outline style throughout, single icon library for consistency (status indicators, settings, tray-adjacent actions, category glyphs where useful). Outline weight matches the flat, low-chrome aesthetic; avoid mixing in filled variants, which would break visual consistency.

## Onboarding illustrations

First-run experience uses full-width "letterbox" illustration panels — one per step — in the Avast-style flat illustration language: a dark navy/brand-blue canvas, a single centered scene (laptop, shield, folder, or similar metaphor for what that step explains), sparkle/plus accents scattered around the subject for visual energy, and a short pill-shaped label above the headline when the panel introduces a named feature (e.g. a purple "SEARCH" pill above a search-onboarding panel, mirroring the "VIRUS SCANS" pill pattern).

- **Purpose:** walk a first-run user through the app's core loop (background polling → notification → local archive → search) as 3–5 short illustrated steps, not a wall of text.
- **Canvas:** dark navy or deep brand-blue background regardless of the app's active light/dark theme — onboarding illustrations are a fixed, branded surface, not theme-reactive.
- **Composition:** one clear subject per panel, headline below it (white, bold), one line of supporting copy, accent color (green) used sparingly for the single word/phrase that matters most in the headline (e.g. "This computer is **protected**" pattern → "New projects, **the moment they post**").
- **Accents:** small sparkle/plus glyphs placed asymmetrically around the subject, in brand blue and green — decorative only, never load-bearing for meaning.
- Each panel maps 1:1 to a real app capability already specified elsewhere (tray/background operation → [architecture-pipeline.md](./architecture-pipeline.md), notifications → [ui-ux-design.md § toast notifications](./ui-ux-design.md#toast-notifications), local archive/search → [search-and-filtering.md](./search-and-filtering.md)) — onboarding should never promise a feature that isn't in the actual scope docs.

## Icon system — three tiers

Icons are assigned to exactly one of three tiers based on context, not mixed arbitrarily:

| Tier | Style | When to use |
|---|---|---|
| **1. No color (outline, neutral)** | Tabler outline icons in `--text-secondary` / `--text-muted` | Default state for anything **not** the current focus — inactive tray menu items, secondary list actions, disabled/inert controls. Communicates "present, but not what you should look at." |
| **2. Brand color (single-hue, filled or accented outline)** | Tabler icons tinted brand blue (or green for positive states) | The **one main action or state** on a given screen — the primary CTA icon, the "live/polling" status glyph, the unread-accent icon. At most one or two per screen, so brand color keeps signaling "this is the important one." |
| **3. Conceptual, multi-color** | Distinct hue per row/item, matching the referenced concept (e.g. teal for network/Wi-Fi, orange for display, green for sound — same convention as a native OS settings list) | **Settings and listing screens** where each row represents a different, unrelated concept — e.g. the settings page ([configuration-reference.md](./configuration-reference.md)) where `poll_interval`, `query_params`, `include_assets`, and `notification_grouping` each get their own hue so the list is scannable at a glance, the way native OS settings screens use per-row color to aid quick recognition rather than requiring reading every label. |

**Rule of thumb:** if you're asking "what color should this icon be," first ask which tier the screen is — a detail/action screen (tiers 1–2, restrained) or a settings/listing screen (tier 3, per-row color is expected and useful). Mixing tier 3's multi-color approach into a focused action screen would compete with the brand-color signal from tier 2 and should be avoided.

## Component base

Built on **shadcn/ui** primitives (cards, buttons, badges, dropdowns, inputs, dialogs) as the component foundation — themeable via CSS variables (fits the semantic-token approach above), accessible by default, and easy to restyle to the blue/green palette rather than fighting a heavier opinionated UI kit. Matches the existing React/Tailwind-adjacent stack already in use elsewhere in your projects.

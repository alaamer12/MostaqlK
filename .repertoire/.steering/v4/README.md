# V4 — Analysis Documentation

[← Back to Base Product](../base/product/README.md) · [V3 (shipped)](./backend-ui-contract.md)

**V4 is the wave in progress.** Its one sentence: the app already watches both platforms; v4 makes it
*know the user* — their skills, their price band, their history — and turn every surface into analysis
relative to that profile.

## Documents

- [Competitor analysis & personalization — the full v4 record](./competitor-analysis-requirements.md)
  — identity, the three-surface split, both live investigations with the commands that ran, the
  field-availability matrix per source, requirements R1–R6, the factor inventory, decisions **D1–D52**
  and corrections **C1–C14**, and what is still open. Read it before writing any v4 code.
- [Animation safety](../v3/animation-safety.md) — still binding, unchanged by v4. Per-frame work goes
  through `TickerGuard`; nothing throws into WinUI.
- [Backend/UI contract](../v3/backend-ui-contract.md) — what the backend may hand the UI. v4 extends it
  (snapshots, bidder rows, profile fields) rather than replacing it.

## Surfaces

| Mockup | What it is |
|---|---|
| [`design/postmvp/v4-analysis/profile.html`](../../design/postmvp/v4-analysis/profile.html) | The user's own file: acquisition (session crawl, manual fallback), completeness against the canon, inline editing, the read as a page of its own, snapshot honesty |
| [`…/projects.html`](../../design/postmvp/v4-analysis/projects.html) | The feed with score pills and the avatar entry; the reversed chrome |
| [`…/bidders.html`](../../design/postmvp/v4-analysis/bidders.html) | Who else is in this project — about us |
| [`…/performer-profile.html`](../../design/postmvp/v4-analysis/performer-profile.html) | A rival's own file — about him, never about the viewer |

## What v4 declares about versions

- **v3 is shipped.** `.steering/v3/` and `design/postmvp/v3-multisource/` were restored to their shipped
  state on 2026-11-05 and hold nothing from this wave.
- **A v4 page is copied into `design/postmvp/v4-analysis/` the first time v4 edits it**; until then v4
  pages link across to `../v3-multisource/…`. That rule and its reason are in the v4 design README.
- **The chrome ruling (D41) is a v4 claim against shipped code**: the rail moves to the physical right and
  the radar panel to the left, while `MainWindowWindowsLayout.xaml` still renders the mirror image. The
  gap is deliberate and recorded, not an error in the mockups.

## Still blocking implementation

1. Whether the app adopts a **JS-capable fetch path** — Mostaql `/u/` opens for a browser engine,
   anonymously, and the profile carries a dozen fields the catalog does not (§9.2 item 7).
2. Executing nothing further of **D50** — the move is done; the v4 copies of `project-details.html`,
   `settings.html` and `onboarding.html` happen when those surfaces are actually edited.
3. The **dimmed-pill wording** on awarded/closed projects (D45) and the **small-sample rule** (§9.2).

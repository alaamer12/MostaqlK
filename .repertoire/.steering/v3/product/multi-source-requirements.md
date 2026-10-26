# V3 Requirements — Multi-Source Platform App (Mostaql + Nafezly)

> **Status: APPROVED DESIGN CONTRACT.** The mockups in
> `.repertoire/design/postmvp/v3-multisource/` are the visual source of truth,
> reviewed and finalized with the product owner. This document translates them
> into implementable requirements. Where this doc and a mockup disagree, the
> mockup wins for visuals; this doc wins for behavior, data, and constraints.

---

## 0. Read-first list (every implementing agent, no exceptions)

1. This file.
2. `.repertoire/design/postmvp/v3-multisource/README.md` (decision log) and open
   all three mockups in a browser: `projects.html`, `project-details.html`,
   `settings.html`.
3. `docs/UNITS.md` — you MUST reuse existing units and add rows for new ones (repo rule).
4. `.repertoire/.steering/base/product/DESIGN.md` (tokens, RTL, typography) and
   `.repertoire/.steering/base/structure.md`.
5. `.repertoire/.steering/v1/tech/cross-platform-ui-conventions.md` before touching UI.

**Version context:** the product owner has **skipped v2 and redefined v3** as the
multi-source release. `structure.md`'s old "V3 = Android companion" section is
**superseded for scope purposes** by this document; the Android/LAN-sync ideas
remain future work but are NOT part of this release. The `v2/` steering folder
remains out-of-scope documentation — do not implement from it and do not delete it.

---

## 1. Product concept (the one sentence)

MostaqlK v3 watches multiple Arabic freelance platforms; **each platform is a
full identity, not a filter** — clicking its icon in the rail morphs the entire
application (brand colors, chrome, content, settings) into that platform, with a
circular reveal expanding from the clicked icon. "Two 1-desktop apps in one frame."

### Locked decisions (do not reopen without the owner)

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Per-source apps via rail switching — **no unified feed, no tabs** | Each source has its own logic/schema/future features (owner's explicit call) |
| D2 | Sources are **developer-defined**. No add/remove source UI anywhere | Owner removed the "إضافة مصدر" affordance |
| D3 | Settings are **platform-scoped**: the المنصة section shows only the active source's rows; each source may expose different settings, pages, and card fields | Owner decision |
| D4 | Nafezly brand accent = **`#FFA200`** (their CSS `--bg-color-4`), NOT their primary blue `#0194fe` | `#0194fe` is indistinguishable from Mostaql `#2386C8` at badge size |
| D5 | App identity stays Mostaql-blue **only** as the default brand; there is no neutral third brand. The active source's brand IS the app chrome color | "source color is identity" |
| D6 | State colors never change with the source (green=enriched/healthy, amber=pending, red=failed) | "state color is truth" |
| D7 | Notification bell lives in the rail above the theme toggle (not in any page header) | Owner relocation |
| D8 | Scan pause/resume is a highlighted pill in the feed footer next to "آخر فحص" | Owner relocation + visibility request |
| D9 | The today-counter is a centered stat card at the far right of the page title, with the seamless chrome sheen (see §6) | Owner request, iterated to approval |
| D10 | Details page title = **the scraped project title**, big and centered — not a static "تفاصيل المشروع" | Owner request |
| D11 | Feed, details, and settings all use the **continuous page pattern** (one surface, sticky section headers, divider rows) — no floating cards, no side gutters | Owner request |
| D12 | Pipeline dashboard panel keeps its reserved location (right of feed), collapsible to a 44px status rail, scoped to the active source | Owner caught its omission |

---

## 2. Identity / token system

Two independent axes:
- `data-source` (mostaql | nafezly) → hue
- light/dark theme → value

Every chrome element reads brand tokens; **no hardcoded brand hex anywhere in
views**. Extend `DesignTokens` (`UI/DesignSystem/`) with a `BrandTokens` unit.

| Token | mostaql light | mostaql dark | nafezly light | nafezly dark | Role |
|-------|--------------|--------------|---------------|--------------|------|
| `--brand` | `#2386C8` | `#5CA8DE` | `#FFA200` | `#FFB84D` | fills, edges, focus rings, selection |
| `--brand-ink` | `#1A6DA6` | `#5CA8DE` | `#9A6100` | `#FFB84D` | small text on light surfaces (AA contrast) |
| `--brand-soft` | `#EFF6FB` | `rgba(92,168,222,.14)` | `#FFF4DE` | `rgba(255,184,77,.14)` | chip/tile backgrounds, unread wash base |
| `--brand-on` | `#ffffff` | `#0b1c2a` | `#3a2a00` | `#2a1d00` | text on solid brand fills |

Neutral palette (slate scale) is theme-driven only, never brand-driven.
**Contrast warning:** brand orange on white fails AA for small text — always use
`--brand-ink` for text ≤14px on light surfaces; `--brand` is for fills/borders/large type.

---

## 3. Shell: the SourceRail (replaces AppSidebar)

- 64px wide, icons only, **physical LEFT** (owner's explicit choice; RTL-native
  would be right — do not flip it silently; it is a one-line change if the owner
  asks later).
- Top→bottom: app mark (brand-filled rounded tile "M") · separator · **source
  tiles** (م = Mostaql, ن = Nafezly) · separator · section buttons (المشاريع،
  البحث المتقدم، الإعدادات، حول التطبيق) · spacer · **notification bell** ·
  **theme toggle**.
- Inactive source tiles: 45% opacity + desaturated. Active: full color + the
  3px inline-start indicator bar.
- Hover tooltips carry name + domain ("مستقل — mostaql.com").
- Disabled source (via Settings): grayed, non-clickable tile.
- A source added in future = one more tile + one more descriptor. No other rail change.
- `AppSidebar` is superseded; migrate its dark-mode row and stat behavior out,
  then rename/replace per §10.

---

## 4. The source morph (behavior spec)

### 4.1 What changes on morph
1. All brand tokens swap to the target source.
2. Content swaps to that source's data (feed rows, settings section, counts).
3. Every per-source figure updates atomically: cadence caption, today counter,
   footer counts/unread/last-scan, search placeholder, pipeline panel source chip.
4. The choice persists (`mostaqlk-source`) and every page opens in the remembered identity.

### 4.2 The animation (web mockup = reference, NOT the implementation)

> ⚠️ **CRITICAL WARNING:** the mockups use the browser **View Transitions API**
> (`startViewTransition` + `clip-path` WAAPI animation on
> `::view-transition-new(root)` + a `view-transition-name: ring` element).
> **None of this exists in .NET MAUI.** Do NOT attempt to port it literally, do
> NOT add a WebView to fake it, do NOT copy the JS. Implement the equivalent
> natively as a unit (§10 `SourceMorphTransition`):
> - **Windows:** composition-level radial reveal — animate a circular
>   `CompositionClip`/mask brush (or an `EllipseGeometry` opacity mask) expanding
>   from the clicked tile's center point to the farthest viewport corner,
>   ~500ms ease-in-out, with the brand ring (stroked ellipse, 3px + soft glow)
>   riding the same radius and fading out at the end. Swap theme dictionaries +
>   content at the START of the animation (the revealed layer is the new state).
> - **Fallback (any platform where a radial mask is unavailable/buggy):**
>   crossfade + slight scale (0.98→1) of the root content with the ring drawn as
>   an overlay ellipse stroke scaling 0→1. This is acceptable; the radial clip is not.
> - Theme (light/dark) toggle uses the same mechanism at 400ms from the toggle button.
> - **`MotionPreferences.IsReducedMotionRequested` → instant swap, no reveal, no
>   ring, no settle.** This is mandatory, not optional (see docs/UNITS.md radar precedent).
> - Re-entrancy: ignore morph requests while one is running (mockup's
>   `transitioning` guard).
> - Card mount settle: transform-only rise (8px, ~340ms, 50ms stagger).
>   **Never animate opacity of feed items during the reveal** — in the mockup
>   that caused the "breaking glass" artifact; natively it causes ghosting.

### 4.3 Timing values (from the approved mockups)
- Source morph: 500ms, ease-in-out. Theme morph: 400ms, ease-in-out.
- Ring: 3px brand stroke + outer glow `color-mix(brand 45%)`, fades over the last 25%.
- Settle: 8px translateY, 340ms `cubic-bezier(.22,.61,.36,1)`, 50ms stagger.

---

## 5. Page: المشاريع (feed)

Header (surface band, bottom border):
- **Today stat card** — far right of the title row: bordered rounded card
  (surface gradient + hairline border + subtle shadow), contents centered:
  label "أُضيفت اليوم" (11px, ink-3) above the number (30px extrabold) with the
  **chrome sheen** (§6). Per-source value.
- Title "المشاريع" — centered, 36px extrabold.
- Cadence caption — centered under title: "المسح كل 30 ثانية · 12 طلب/دقيقة"
  (mostaql) / "المسح كل 60 ثانية · 8 طلب/دقيقة" (nafezly). **No domain prefix**
  (owner removed it).

Content row (shares width with the pipeline panel — they squeeze each other):
- Search row: input **right-justified, full remaining width** (no max-width cap),
  surface-2 background, brand focus ring; **الفلاتر button docked at its left**.
  The search box lives INSIDE the feed column, so opening the pipeline panel
  squeezes it — they are relative to each other, never overlapping.
- **Search and active query are per-source.** The placeholder names the active
  source ("بحث في مشاريع مستقل/نفذلي…"), and each source keeps its own saved
  query + filter state. (v1's "query applies to all sources" model is retired.)
- Filters button opens a **modal** (never an inline section): dimmed backdrop +
  centered card, Esc/backdrop close, quick-filter chips (toggleable), minimum
  budget + sort fields, "مسح الكل / إلغاء / تطبيق". Per-source filter state.
- **Empty state per source:** first-run of a source with zero stored projects
  shows the existing `LabelWithSubText` empty/error treatment with
  source-appropriate copy — never a blank scroll area, never a spinner-only view.
- Feed = **continuous rows** on one surface: 4px inline-start edge (brand when
  unread, line when read), unread rows get a 6% brand wash, hairline dividers,
  24px×32px padding. No rounded floating cards.
- Row content is **schema-driven per source** (§8): Mostaql rows show
  (ر.س budget · مدة التسليم · عدد المقترحات); Nafezly rows show
  ($ budget · العروض المقدمة · حالة المشروع · "مشرف عليه" shield when present).
- Read rows may render compact (one-line description + thin meta row); unread
  rows render full. (Observed in approved mockup; keep as the default density rule.)

Pipeline panel (right): reuse `PipelineDashboardPanel` + `PipelineRadar` +
`SplitterHandle` unchanged in mechanism; v3 changes = it shows the **active
source's** pipeline, header carries a source chip, and it collapses to the 44px
status rail (worker dots + backlog %), persisted.

Footer: "{n} مشروعًا من {source}" · unread (brand ink) · "تحديد الكل كمقروء" ·
active query + "تعديل" · "آخر فحص: …" + **poll pill** (§6).

---

## 6. Small UI contracts (exact, from final mockup iteration)

- **Poll pill:** brand-outlined pill, brand-soft fill, brand-ink text+icon
  ("⏸ إيقاف الفحص"). When paused: green fill/border/text ("▶ استئناف الفحص").
  Scoped to the active source. `aria`/tooltip text swaps with state.
- **Today card chrome:** solid-metal gradient — the number is always fully
  `--brand-ink`; a brighter band (`color-mix(ink 35%, white)`) sweeps across.
  **Seamless loop requirement:** the animation must travel exactly one gradient
  period per cycle (web: `0 → 260%` with `background-size: 260%`), then hold —
  last frame pixel-identical to first. If your native equivalent produces a
  visible snap/reset, it is wrong. Reduced motion: static solid ink, no sweep.
- **Details decision-strip facts (final approved set):** الميزانية · مدة التسليم ·
  العروض المقدمة · الحالة · نُشر — label + big value ONLY, **no sub-labels**
  (owner stripped: "عند التسليم", "منافسة منخفضة", "يستقبل العروض", skill lists,
  and the merged delivery/publish line; "نُشر" is its own column).

---

## 7. Page: تفاصيل المشروع (details)

- Header band: title = **scraped project title** (centered, 30px extrabold);
  caption chip row under it: source chip · "مشرف عليه" (nafezly when flagged) ·
  enrichment status · "· اكتُشف {date}" (no "أُضيف خلال…" tail — removed);
  back + breadcrumb row + "عرض في {source}" brand button (this button is the
  renamed `MostaqlLinkButton` → `SourceLinkButton`).
- Continuous page pattern (like settings): full-width surface, sticky section
  headers, divider blocks. No floating cards. **No pipeline-provenance section** (removed).
- Section order = decision order: **decision strip** (§6) → **الوصف**
  (+ عرض المزيد + المهارات chips) → **معلومات المنفذ** (nafezly-only, §8) →
  **صاحب المشروع** (identity + 3 stat cells inline on one row) → actions footer
  (نسخ الرابط ghost + تحديد كمقروء brand, right-aligned).
- معلومات المنفذ: executor identity line (initials avatar, name, verified,
  "منفّذ معتمد · على نفذلي منذ {year}") + exactly TWO inline stat cells:
  **التقييم** and **مشاريع ناجحة**. **No bid-related fields** — no "السعر المتفق
  عليه", no "مدة التنفيذ" (we scrape, we don't bid; owner removed them).
  Omit the whole section for Mostaql (its schema has no executor block).
  When the nafezly project has no executor assigned yet (common — we catch
  projects fresh), hide the section; do not show placeholders.
- Design-annotation pills from the mockup (purple labels, dashed "مطابقة 87٪"
  ML concept slot) are **review artifacts — do not implement** the ML pill.

---

## 8. Data & scraping requirements

### 8.1 Source registry (code-defined)
A `SourceDescriptor` per platform: id (`mostaql`|`nafezly`), Arabic name, domain,
brand token set (light+dark), rail glyph, cadence default, rate budget default,
schema map (which fields exist + render order), feature flags
(`hasExecutorInfo`, `hasSupervisedFlag`, `needsSessionForFullEnrichment`).

### 8.2 Database (additive migration only)
- `projects` gains `source_id` (NOT NULL, default 'mostaql' for existing rows).
- Source-specific columns or a JSON detail blob per existing DetailParser pattern:
  nafezly needs at least: budget range (USD numeric + display string), proposals
  count, project state (open/closed), supervised flag, publish time, executor
  name/rating/successful-projects (nullable).
- **WARNING:** keep the store-and-forget policy — no re-fetching stored projects.
- Per-version data rules still apply (shared `%LocalAppData%\MostaqlK\` data,
  per-version logs, `AppPaths` migration). The schema migration must run on first
  launch of the new tag without data loss; write it as `ALTER TABLE ADD COLUMN`
  style additive steps. Test against an existing v1 database file.

### 8.3 Nafezly scraper (verified facts from live investigation)
- `https://nafezly.com/projects?page=N` is **fully server-rendered** — plain
  HTTP + HTML parsing (BeautifulSoup-equivalent / the app's existing HTML parser
  stack). **Playwright/WebView is NOT needed and must not be added for this.**
- Send a realistic browser `User-Agent`; default tool UA risks 403.
- Encoding: force UTF-8 (Arabic content mojibakes otherwise).
- Listing exposes: project URL `https://nafezly.com/project/{id}-{slug}` (id in
  the slug), title, description snippet, poster avatar/name.
- Detail exposes: budget ("10 - 25 $" style — render `dir=ltr`), العروض المقدمة,
  حالة المشروع, تاريخ النشر, skills, "سعر الخدمة", supervised flag. Some detail
  pages require a session → enrichment may fail with 403: store summary-only,
  surface the failed-enrichment row state with "إعادة المحاولة", never crash the queue.
- Respect their site: conservative default cadence 60s / 8 req-min (slower than
  Mostaql), and rate limiting per source, not global.

### 8.4 Pipeline services
Polling, discovery, enrichment queue, and notifications become **per-source
instances** (same worker-pool mechanics, one budget per source).
`GlobalAppStatusService` exposes per-source state; tray reflects aggregate
(any source scanning → polling state; any error → error state).

---

## 9. Settings (full page)

- Continuous full-width page, sticky section headers, `set-row` label/value/edit
  rows. No floating cards, no max-width gutters.
- Header: "الإعدادات" centered big title + caption "إعدادات المنصة الحالية:
  {tile} {name}" that swaps with the morph.
- **Section المنصة** — renders ONLY the active source's rows (`data-src-panel`):
  - mostaql: فاصل الفحص · حد الطلبات · تنبيهات هذه المنصة · أنواع المشاريع
    المتابَعة (عادية + مراقبة) · آخر فحص · الأرشيف
  - nafezly: فاصل الفحص · حد الطلبات · تنبيهات هذه المنصة · جلسة الإثراء
    (مرتبطة/غير مرتبطة + ربط كوكيز/إلغاء الربط) · تخطي المشاريع المغلقة ·
    عملة الميزانية (كما في الموقع $) · آخر فحص · الأرشيف
  - Different sources may legitimately show different rows/sections — drive from
    the descriptor, do not fork page code per source.
- **Section عام** (global): تشغيل مع النظام · تجميع التنبيهات · تذكر آخر منصة مفتوحة.
- **No add/remove source UI.** Session cookie import is the only credential flow
  (no auth screens exist in this product — see v2 identity doc note, still holds).
- **Edge case — disabling the ACTIVE source:** the rail tile goes gray and the
  app immediately morphs to the other enabled source (never leave the app in a
  disabled identity; never show a dead feed).
- Persist per-source settings separately (same keys namespaced by source id).

---

## 10. Units: new, renamed, updated (docs/UNITS.md is part of the task)

| Unit | Kind | Notes |
|------|------|-------|
| `SourceRail` | PlatformComponent (replaces `AppSidebar`) | icons-only, tooltips, source tiles, bell, theme toggle |
| `SourceMorphTransition` | Core/Platform helper (partial per platform) | `MorphAsync(origin, brandSwap, duration)`; Windows composition reveal + reduced-motion instant; Android/iOS safe-default stubs per barrel convention |
| `BrandTokens` | DesignSystem | per-source token sets + swap API; theme-change safe (Event lifetime rule: subscribe on Loaded, release on Unloaded) |
| `SourceLinkButton` | rename of `MostaqlLinkButton` | label/target from descriptor; update all call sites + tests |
| `FilterModal` | via existing `ModalPresenter` | do NOT build a new overlay mechanism |
| `TodayStatCard` | PlatformComponent | the chrome-sheen counter card; honors MotionPreferences |
| `PipelineDashboardPanel` | update | source-scoped content + chip; mechanism unchanged |
| `ProjectCard` | update | continuous-row variant (no rounded card), brand edge/wash, schema-driven stat slots |

**Repo rule:** every new/renamed unit above gets its `docs/UNITS.md` row in the same
task that implements it. Renames must update the old row, not duplicate it.

---

## 11. Hard warnings (agent guardrails)

1. **Do not port web tech literally** — no View Transitions, no CSS clip-path
   WAAPI, no Tailwind, no HTML in the app. The mockups are references only.
2. **No `#if PLATFORM`** outside the two canonical exempt files; platform variance
   via `X.Windows.cs`/`X.Android.cs` partials (structure.md rule).
3. **RTL:** use logical properties everywhere; the rail-left placement is the ONE
   approved physical-left exception (LTR wrapper), per owner instruction.
4. **Arabic-first strings**; Latin/numeric runs (budgets "$", dates) render LTR-isolated.
5. **Never animate opacity of content under a reveal**; transform-only settle.
6. Existing UI-test AutomationIds (e.g. `Projects_LastScanLabel`) must keep
   working or be migrated deliberately with test updates.
7. Do not edit `.env` directly (global rule); no secrets in the repo; imported
   cookies go to the existing secrets/session storage path.
8. Do not implement v2-folder features or the old "V3 = Android sync" scope.
9. Versioning: every rebuild-worthy change bumps the tag
   (`MAJOR.MINOR.PATCH.TAG{n}`) per AGENTS.md; v3 base bump (to `3.0.x`) only if
   the owner says so — until then, keep bumping the current base's tag.
10. If a requirement here conflicts with reality (API changed, MAUI limitation
    discovered), **stop and report the conflict** — do not silently improvise a
    different UX. The mockups were owner-approved; deviations need approval.
11. Accessibility: brand-ink contrast rule (§2), focus rings brand-tinted,
    tooltips have aria labels, modal traps focus and Esc-closes, reduced-motion
    honored everywhere (§4.2).
12. **Icons:** the rail needs glyphs the app does not have yet (bell, sun/moon,
    sliders, angles-left/right, shield, satellite, wand, asterisk…). Follow the
    established `AppIcon` pipeline: pre-baked PNG artwork via the existing
    generation scripts + `AppIconGlyph` enum entries. **Never swap
    `ImageSource`/`Button.ImageSource` at runtime** — that exact pattern caused
    the onboarding first-paint crash and the invisible-spinner bug (see docs/UNITS.md
    `AppIcon` history). State changes (e.g. moon↔sun) = toggle visibility of two
    pre-baked icons, not re-resolution.
13. **Tray + toasts:** tray states remain aggregate (any source scanning →
    polling; any source error → error). Toasts must name their source
    ("مشروع جديد على نفذلي") — the deep-link opens the app **morphed into that
    source** first, then scrolls to the project.

---

## 12. Acceptance criteria (condensed checklist)

- [ ] Fresh install + upgrade from v1 DB: no data loss, existing projects show as mostaql.
- [ ] Clicking ن morphs: all chrome orange, feed shows nafezly rows only, cadence/
      today/footer/panel chip all swap, ring + reveal visible, no flash/glass artifact,
      reduced-motion = instant.
- [ ] Reload/relaunch restores last source + theme (both windows and pages).
- [ ] Nafezly projects discovered, enriched, stored, notified within configured budget.
- [ ] 403-enrichment failure shows failed state + retry, queue unaffected.
- [ ] Filters modal: opens from button, Esc/backdrop close, per-source state kept.
- [ ] Poll pill pauses/resumes the ACTIVE source only.
- [ ] Details page: title = project title; nafezly shows معلومات المنفذ (only rating+
      successful projects), mostaql omits it; decision strip has exactly the 5 approved facts.
- [ ] Settings: platform section swaps rows on morph; no add-source UI anywhere.
- [ ] Disabling the active source auto-morphs to the enabled one; no dead feed state.
- [ ] Toast deep-link morphs the app to the toast's source before scrolling to the project.
- [ ] Pipeline panel scopes to source; collapse state persists; 44px rail shows dots + backlog.
- [ ] docs/UNITS.md updated for all new/renamed units; no dead references to AppSidebar/
      MostaqlLinkButton remain.
- [ ] Light + dark parity for every new surface; AA contrast on all small brand text.

---

## 13. Suggested implementation phases

1. **Backend abstraction:** descriptor registry, DB migration, per-source polling/
   rate/notifications, nafezly scraper (discover + enrich + failure states).
2. **Identity:** BrandTokens + SourceMorphTransition (Windows) + persistence.
3. **Shell:** SourceRail + bell/theme relocation.
4. **Feed page:** continuous rows, today card, poll pill, filters modal, panel scoping.
5. **Details page:** schema-driven sections + executor info.
6. **Settings:** platform-scoped section + general.
7. **Hardening:** reduced-motion pass, contrast pass, AutomationId migration,
   docs/UNITS.md, upgrade test from a real v1 database.

Each phase ends runnable; do not defer the morph (phase 2) past phase 3 — the
shell and identity are the product.

---

## 14. Open questions (owner to decide; defaults if unanswered)

| # | Question | Default if no answer |
|---|----------|----------------------|
| Q1 | Rail physical-left (current) vs RTL-native right? | Keep left (owner's explicit ask) |
| Q2 | Toast/notification chrome: source-branded or neutral app chrome? | Neutral chrome, source name in text only |
| Q3 | Reveal ring weight: 3px+glow vs bolder filled-disc flash? | 3px + glow (mockup-approved) |
| Q4 | Advanced search page: not redesigned in v3 mockups — ship v1 page with per-source queries, or defer? | Ship with per-source query scoping, no visual redesign |
| Q5 | Onboarding copy mentions single-platform polling — refresh wording for multi-source? | Minor wording pass in phase 7, no new steps |

## 15. Explicitly OUT of scope for v3

- Any ML/scoring feature (the dashed "مطابقة 87٪" pill in the mockup is a
  **labeled concept slot** for review only — do not implement, do not reserve
  code for it).
- Android/iOS/LAN-sync (the old "V3" plan in `structure.md`), any `v2/` folder features.
- Adding new sources beyond mostaql+nafezly (the descriptor pattern must make it
  cheap, but do not build a generic source-management UI).
- Unified/cross-source feed, global search across sources, cross-source stats page.
- Auth/login screens, in-app bidding/proposal submission of any kind.

---

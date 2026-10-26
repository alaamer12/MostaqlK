# b1b-palette-sweep-implementer

## Goal

B1b — replace every inline brand-family hex in `UI/` + `Features/` with an app-level
DynamicResource palette (`Brand`, `BrandInk`, `BrandSoft`, `OnBrand`, `BrandLink`) fed by
`IBrandTokenProvider.CurrentFor(currentTheme)` and re-applied on BOTH `TokensSwapped` and theme
change, keeping mostaql LIGHT value-identical and letting mostaql DARK unify onto the locked
dark values. Builds on (does not touch) the uncommitted B1-core working-tree changes.

## Skill note

`.cursor/skills/` does **not exist** in this Linux codespace (AGENTS.md skill mandate
unfulfillable); proceeded with repo-pattern adherence instead. No MAUI build was run (parent
owns it; `MostaqlK.csproj` targets net10.0-windows and cannot build here). Validation = the two
headless harnesses + gate greps + line-level self-review of every diff.

## What was done

### 1. New unit: `UI/DesignSystem/BrandTokens/BrandPalette.cs` (static helper)

- Five DynamicResource keys written into the ROOT `Application.Resources` dictionary (root — not
  merged — because only app-root dictionary mutation propagates to MAUI `DynamicResource`
  consumers). Values are MAUI `Color`s parsed from `BrandTokenSet` hexes (memoized cache).
- `Initialize(Application app, IServiceProvider services)` — called once from the `App` ctor
  AFTER the `UserAppTheme` eager apply and the `mostaqlk-source` restore (B1-core block, left
  untouched), so the first projection already reflects theme + persisted source. Idempotent;
  guards re-subscription.
- Subscribes `IBrandTokenProvider.TokensSwapped` **and** `Application.RequestedThemeChanged`;
  both funnel into one `Apply()` (the single re-apply hook). `RequestedThemeChanged` is the
  existing repo pattern (AppSidebar/NewRibbonBadge/AppCard/PipelineRadar already use it) and is
  raised by BOTH theme entry points (`App.xaml.cs` startup `UserAppTheme` and
  Settings/AppSidebar toggles which set `UserAppTheme`) — no duplicated logic added.
- `Apply()` re-enters via `MainThread` if fired off-thread (defensive), rewrites the 5 keys in
  place (value replacement = repaint trigger), and raises `PaletteChanged`.
- Cached color reads for draw-only code: `Brand/BrandInk/BrandSoft/OnBrand/BrandLink`
  (effective theme), `AccentLight/AccentDark` (brand fill per theme, theme-independent),
  `LinkLight/LinkDark` (theme-explicit link ink). Transparent until initialized (fails visible;
  impossible in the real app since Initialize precedes any window).

### 2. Wiring

- `App.xaml.cs` (+6 lines): `BrandPalette.Initialize(this, services);` right after the B1-core
  source-restore block. Nothing else in the file touched.

### 3. XAML sweep → `{DynamicResource …}` (31 replacement sites, 13 files)

Every hit below was `{AppThemeBinding Light=#2386C8, Dark=#5CA8DE}` → `Brand`,
`{AppThemeBinding Light=#2563EB, Dark=#60A5FA}` → `BrandLink`, or a static gate-hex attr →
same keys. All were direct element attributes (never `Style`/`DataTrigger` Setter values, where
MAUI DynamicResource is unreliable).

| File | Replacements |
|---|---|
| `UI/PlatformComponents/AppSidebar/AppSidebar.xaml` | 5× BrandLink (4 active bars were static `#2563EB`, stat-card number was the pair) |
| `UI/PlatformComponents/SourceLinkButton/SourceLinkButton.xaml` | 2× BrandLink (icon + label) |
| `UI/PlatformComponents/PipelineDashboard/PipelineDashboardPanel.xaml` | DiscoveryDot static `#5CA8DE` → Brand; DetailTitleLabel pair → BrandLink |
| `Features/Settings/Views/Layouts/SettingsPanelWindowsLayout.xaml` | Gear icon pair, save-cookie Button `#2563EB`, Moon icon `#2563EB` → BrandLink |
| `Features/Settings/Views/Layouts/SettingsPanelMobileLayout.xaml` | back link pair, save Button `#2563EB`, Moon `#2563EB` → BrandLink |
| `Features/Notifications/Views/RecentNotificationsFlyout.xaml` | unread dot pair → Brand |
| `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml` | 4× pair → Brand (edit icon/label, unread count/label) |
| `Features/Projects/Views/Layouts/MainWindowMobileLayout.xaml` | 2× pair → Brand |
| `Features/Projects/Views/Layouts/ProjectCardMobileLayout.xaml` | title pair → BrandLink; unread dot + label pairs → Brand |
| `Features/Projects/Views/Layouts/ProjectCardWindowsLayout.xaml` | same shape as mobile (1 BrandLink + 2 Brand) |
| `Features/Projects/Views/Layouts/AboutPageMobileLayout.xaml` | back link pair → BrandLink |
| `Features/Projects/Views/Layouts/ProjectDetailsMobileLayout.xaml` | back link pair + tag-chip name pair → BrandLink |
| `Features/Projects/Views/Layouts/ProjectDetailsWindowsLayout.xaml` | tag-chip name pair → BrandLink |

### 4. Code/drawable sweep → provider reads through BrandPalette

| File | Before | After |
|---|---|---|
| `UI/DesignSystem/DesignTokens.cs` | `static readonly Color AccentPrimary/AccentPrimaryDark = FromArgb("#2386C8"/"#5CA8DE")` | expression-bodied live reads `BrandPalette.AccentLight/AccentDark` (zero code consumers today — verified by grep — so the field→property change is safe; doc updated) |
| `UI/DesignSystem/NewRibbonBadge.cs` | `isDark ? "#5CA8DE" : "#2386C8"` | `BrandPalette.Brand`; `PaletteChanged` sub on Loaded / unsub on Unloaded (same lifetime rule as its theme sub) |
| `UI/PlatformComponents/AppSidebar/AppSidebar.cs` | consts `ActiveTextLight="#2563EB"`, `ActiveTextDark="#60A5FA"` | `BrandPalette.LinkLight/LinkDark` in `SetRowState`; `PaletteChanged` sub on Loaded / release Unloaded |
| `UI/PlatformComponents/SplitterHandle/SplitterHandle.cs` | `ActiveColor = isDark ? "#5CA8DE" : "#2386C8"` | `BrandPalette.Brand` (read lazily per hover — no subscription needed) |
| `UI/PlatformComponents/AppCard/AppCard.cs` | unread stroke `isDark ? "#5CA8DE" : "#2386C8"` | `new SolidColorBrush(BrandPalette.Brand)`; `PaletteChanged` sub/unsub |
| `UI/PlatformComponents/AppIcon/AppIconGlyphExtensions.cs` | `ActiveTextHex="#2563EB"` const + 2 comment hexes | match against `BrandPalette.LinkLight`; comments reworded to "brand-link blue". Raster "_active" PNGs are baked and cannot re-tint → matching deliberately pinned to the LIGHT link value: mostaql identical pixels both themes (dark previously never matched `#60A5FA≠#2563EB`, still doesn't), non-morphing artwork surfaced below |
| `UI/PlatformComponents/AppIcon/AppIcon.cs` | doc comment `#2563EB`/`#475569` | hexes reworded (gate hygiene) |
| `UI/PlatformComponents/PipelineRadar/PipelineRadarDrawable.cs` | `static readonly DiscoveryDark = FromArgb("#5CA8DE")` | `DiscoveryColor => IsDarkTheme ? BrandPalette.AccentDark : DiscoveryLight` — static field would freeze at class-init, hence property |
| `UI/PlatformComponents/PipelineRadar/PipelineRadar.xaml.cs` | — | + `PaletteChanged` → `RadarCanvas.Invalidate()` (attach/detach lifetime, mirrors its `RequestedThemeChanged` wiring) so an idle radar repaints after a morph |
| `UI/PlatformComponents/PipelineDashboard/PipelineDashboardPanel.xaml.cs` | `IsDark ? FromArgb("#5CA8DE") : FromArgb("#1D6FA5")` | `IsDark ? BrandPalette.AccentDark : FromArgb("#1D6FA5")` (light hue left — see kept-inline list) |
| `UI/PlatformComponents/SourceLinkButton/SourceLinkButton.xaml.cs` | `ApplyPalette()` set `LinkIcon/LinkLabel.TextColor` from `Current.LinkLight/Dark` on Loaded+Swap | **removed** — a local set would displace the new `DynamicResource BrandLink` refs and break theme repaint; the `TokensSwapped` subscription stays (now re-applies the descriptor-driven LABEL, which was the subscription's other job) |

Per-hex semantic calls (all value-checked against the B1-core locked seeds):
- `#2386C8/#5CA8DE` pairs → `Brand`: identical light AND dark, zero pixel change.
- `#2563EB/#60A5FA` theme-aware pairs → `BrandLink` (= locked `LinkLight/LinkDark`): identical
  light AND dark, zero pixel change. These are true link/ink semantics (chip labels, back links,
  rail selection the mockups later re-home on `SourceRail`).
- **Static** `#2563EB` (no theme awareness: sidebar bars, settings save buttons, moon icons) →
  `BrandLink`: light stays `#2563EB` (value-identical); dark `#2563EB → #60A5FA` — this is the
  designed dark unification (those spots rendered the LIGHT link blue on dark chrome = the
  legacy drift). Only intended pixel changes in the whole sweep, and only in mostaql dark.
- Pair-adjacent `#1D6FA5`/`#1A2A44`/`#EFF6FF`/`#3B82F6`/`#1D4ED8`/`#93C5FD`/`#0EA5E9` are NOT in
  the locked set → left inline (below).

## Kept inline deliberately (with reasoning)

| Where | Hex | Why kept |
|---|---|---|
| `Resources/Styles/Colors.xaml` `AccentPrimary/AccentPrimaryDark` | `#2386C8/#5CA8DE` | Outside the `UI/`+`Features/` gate; these are StaticResource mirrors consumed via `AppThemeBinding Light={StaticResource AccentPrimary}` inside `AppButtonStyle/AppToggleStyle/AppCardStyle` setters — MAUI style-setter values don't repaint from app-dictionary mutation, so re-pointing them is a style-mechanism change (surfaced: Primary buttons/toggle OnColor do NOT yet re-tint on morph — B-follow-up). |
| `Sources/Mostaql/MostaqlSource.cs:31`, `Sources/Nafezly/NafezlySource.cs:35` | locked seeds | Source of truth; Core is not a gate dir; B1-core (parent's) — untouched. |
| `Platforms/Windows/NativeSplashScreen.cs:358` | `#2386C8` comment | Product identity (splash) stays fixed per HARD RULES; outside gate dirs. |
| `AppSidebar.cs` `ActiveBackgroundLight/#EFF6FF`, `ActiveBackgroundDark/#1A2A44` | Tailwind wash | Not locked-set members; BrandSoft would move light pixels; row bg replaced wholesale by SourceRail (B2/B3). |
| `PipelineRadarDrawable` / `PipelineDashboardPanel.xaml.cs` `DiscoveryLight/#1D6FA5` | deeper light hue | Not a locked token; converging to `#2386C8` would change LIGHT pixels (violates the light-identity rule). |
| Radar/dashboard state colors `#F59E0B #22C55E #38BDF8 #EF4444 #B45309 #15803D #0369A1 #B91C1C #7C8CA5 #64748B` | state vocabulary | State colors are NEVER brand-retinted (hard rule; §7). |
| Layout neutrals (`#0F172A`, `#F1F5F9`, `#E2E8F0`, `#1E293B`, slate text, `White`/`#ffffff` on-brand text, `#66FFFFFF` shimmer…) | — | Neutral chrome, not gate hexes. |
| `AppButtonStyle` etc. Primary-fill consumers (see Colors.xaml row) + Settings `#1D4ED8/#93C5FD` info callout + `#0EA5E9` Gauge icon + `#3B82F6` logo/avatar tiles | — | Info-blue concept colors & product-identity tiles, none are locked-set members; avatar-tile mockup-Brand convergence flagged as design follow-up (would move light pixels). |
| `AppSidebar`/`DashboardPanel` drill-in card bg `#EFF6FF/#1A2A44/#132237/#132239/#0B2540` | wash blues | Not locked set, light-identity rule; SourceRail/unread-wash restyle is B-scope. |

## Gates — exact outputs

- `dotnet run --project tools/ParserTests` → tail: `TOTAL: 229 passed, 0 failed` / `All checks passed.`
- `dotnet run --project tools/DbMigrationTests` → tail: `ALL DB MIGRATION CHECKS PASSED`
- Gate grep `#2563EB|#60A5FA|#2386C8|#5CA8DE|#1A6DA6|#EFF6FB` (case-insensitive, *.cs/*.xaml) under
  `UI/` + `Features/`: **zero hits** (exit 1, no output).
- Repo-wide same grep leaves only the 4 whitelisted entries in the table above.

## Docs updated

- `.repertoire/.steering/v3/backend-ui-contract.md` §3: DynamicResource row PLANNED→**REAL (B1b)**
  with 4 honest caveats (styles not morphed, raster icon gap, dark-drift change, smoke pending);
  `BrandTokenSet` row's "consumers still read Current + inline hexes" note flipped.
- `UNITS.md`: new `BrandPalette` row; `DesignTokens` row updated (live accent reads).
- `CHANGELOG.md` [Unreleased] Added: B1b sweep bullet. No commit, no tag bump.

## Assumptions

1. "LIGHT value-identical + DARK unifies onto the locked dark set" ⇒ pair-valued spots keep their
   pair, and only DARK-mode legacy drift (static light-hexes painting dark chrome) moves to the
   locked dark value of the same token family (`#2563EB → #60A5FA`). Any reading where dark
   pair-spots also move to `#5CA8DE` would break light identity and needs a new token (record is
   frozen) — parent may re-point `BrandLink`→`Brand` for specific spots once the mockup comparison
   is reviewed; each spot is a one-line change now.
2. App-root ResourceDictionary mutation repaints MAUI DynamicResource consumers (CommunityToolkit
   theming pattern). Not verifiable headless — top Windows-smoke item.
3. `RequestedThemeChanged` fires for every `UserAppTheme` write (repo components already depend on it).

## Windows smoke list (parent, cannot verify headless)

1. **Palette repaint**: toggle light↔dark from Settings row AND sidebar switch — sidebar active
   bar/text, stat number, link chip ink, unread dots/labels ("غير مقروء", unread count), edit
   link, back buttons, skill-pill chips, ribbon badge, unread card stroke, splitter hover, radar
   discovery ring + discovery dot, save-cookie buttons, moon icon — all flip without page reload
   (proves root-dictionary mutation propagates to DynamicResource consumers).
2. **Light pixel parity**: with mostaql active, every swept control in LIGHT must equal the
   pre-B1b screenshot pixel-for-pixel.
3. **Dark designed diffs** (expected changes vs pre-B1b): sidebar active bars `#2563EB→#60A5FA`;
   Settings "حفظ الكوكي/حفظ" button fill `#2563EB→#60A5FA`; Moon icons `#2563EB→#60A5FA`.
   Everything else dark-identical (bars/moon/save-button dark before was the un-themed light hex).
4. **Swap path (headless provider test)**: `SwapTo(nafezly)` at runtime (or Data dir with
   `mostaqlk-source=nafezly`) → every swept control turns orange-family; `BrandInk/BrandSoft/
   OnBrand` keys observable in `Application.Resources` even though no consumer uses them yet.
5. **SourceLinkButton**: chip ink still `#2563EB` light / `#60A5FA` dark on startup (dynamic ref,
   not the removed local paint); label follows the source name on swap.
6. **AppIcon nav active PNGs**: light shows baked blue "_active" icons as before; dark shows base
   gray as before (unchanged pre-existing gap).
7. First-run/onboarding unaffected (no brand hexes there); tray/diagnostics visuals untouched.

## Surfaced items (not touched, per autonomy bounds)

1. `Resources/Styles` Primary button/toggle/card-stroke styles still pin `AccentPrimary`
   StaticResource → they do NOT re-tint on morph (nafezly would show blue CTAs). Style-setter
   conversion (or style re-apply on `PaletteChanged`) is the natural next sweep; needs setter
   DynamicResource support verification in MAUI 10.
2. Pre-baked `icon_*_active` PNG variants can't re-tint — nafezly active nav icons render gray
   (or the wrong-blue bake). Needs asset-pipeline decision (per-source bakes vs. font/tintable
   glyphs).
3. Mockup-vs-app divergences kept honest: rail wash `#EFF6FF/#1A2A44`, radar light discovery
   `#1D6FA5`, info callouts `#1D4ED8/#93C5FD`, avatar/logo `#3B82F6`, skill-pill bg — the
   SourceRail/unread-wash design work (B2–B4) should converge these onto `BrandSoft`/`BrandInk`
   deliberately, not silently through this gate.
4. `BrandInk`, `BrandSoft`, `OnBrand` keys are seeded + maintained but have ZERO consumers yet —
   first consumers arrive with B2/B3 rail + chips.
5. Dashboard `DiscoveryDot` repaint after a morph while pipeline is dead-quiet relies on the next
   `RefreshReadout` (status events are frequent; no subscription added to avoid panel churn) —
   radar canvas itself invalidates on `PaletteChanged`.
6. Provider's only two consumer families found: `SourceLinkButton` (name + previously ink) and
   `App.xaml.cs` startup restore; the "enrichment badge" the parent hinted at does NOT subscribe
   to the provider today — the brand-painted badges are `NewRibbonBadge` (swept to the palette).
7. UITests (`MostaqlK.UITests`) untouched; any color-value assertions there need Windows run
   verification.

## Verification performed

- Re-grep inventory matched parent's counts (~71 gate-hex occurrences incl. comments) before; zero
  after under the gate dirs; full diff review of all 25 changed files + 1 new file.
- No git add/commit/checkout/stash/reset executed. B1-core changes and parent's staged entries
  left exactly as found. `scratch/` untouched (not mine).

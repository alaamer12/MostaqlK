# B3 — Typed facet vocabulary + FilterModal + nafezly feed rendering

Agent: `b3-facet-filters-implementer` · branch `v3` · snapshot HEAD `75f93a3` (dev17) + concurrent B4 wave in tree.
Brief: `scratch/brief-b3.md` + parent task block (parent block took precedence where they differed).

## What was asked

1. `ISourceDescriptor` += `FilterFacets` / `BuildQueryFromFacets` / `ParseFacetSelection`, implemented in BOTH
   modules (nafezly real driving `NafezlyQueryVocabulary` incl. %2C-safe commas; mostaql `[]` + byte-identical
   legacy-suffix passthrough) and in the DbMigrationTests stub.
2. New shared unit `FilterModal` rendering the active descriptor's facets generically, prefilling via
   `ParseFacetSelection`, Applying through the SAME single settings-mutation path SettingsViewModel uses.
3. Verify nafezly feed-card rendering against the v3 mockups for fields `ProjectSummary` carries; no invented
   fields; STOP/surface mockup data gaps.
4. Gates: ParserTests (+facet checks) + DbMigrationTests green; grep gate (facet/site literals zero outside
   `Sources/**`); contract §1/§8 flips; UNITS row; CHANGELOG bullet; no commit, no tag bump.

## Files created

| File | Purpose |
|---|---|
| `UI/PlatformComponents/FilterModal/FilterModal.cs` | The new unit (code-built `ContentView`, mockup-faithful overlay: scrim `#8C020617` = mockup `rgba(2,6,23,.55)`, 480px card, chips from `Options`, two numeric `AppEntry` bounds for `Range`, footer مسح الكل/إلغاء/تطبيق + per-platform footnote verbatim from the mockup). Brand chrome via `BrandPalette` statics; neutral pairs via the standard slate literals. Tree rebuilt per `Open()` → no app-lifetime subscriptions (event-lifetime rule). Host-managed `IsVisible` (same mechanic as `RecentNotificationsFlyout`). |
| `Core/Sources/FilterFacetSpec.cs` | (pre-existing, parent-placed; NOT edited — see "spec fields" below: nothing extra was needed.) |
| `.repertoire/agents/b3-facet-filters-implementer.md` | This report. |

## Files edited (all additive; no other agent's content reverted)

| File | Change |
|---|---|
| `Core/Sources/ISourceDescriptor.cs` | 3 new members with contract docs (keys opaque; module owns grammar; round-trip obligation stated). Placed before `BuildListingUrl`. |
| `Sources/Nafezly/NafezlyQueryVocabulary.cs` | `using MostaqlK.Core.Sources;` + `FacetSpecs` (specialize/skills/pricing, captions = the site's own headings), `BuildQueryFromFacetSelection` (delegates to the existing `BuildQuery` for recognized facets — zero grammar duplication; unknown keys echoed after the canonical order), `ParseFacetSelection` (`%2C`-safe: `Uri.UnescapeDataString` before comma split; positional blanks kept so `pricing=,100` parses min-blank; leading `?` dropped; dup keys merged). |
| `Sources/Nafezly/NafezlySource.cs` | Implements the 3 members by delegating to the vocabulary (module data stays single-sourced). |
| `Sources/Mostaql/MostaqlSource.cs` | `FilterFacets => []`; Parse = opaque echo of each `&`-segment (`Split('&')` WITHOUT `RemoveEmptyEntries`), Build = re-emit segments → **byte-identical passthrough for every string, including leading `?`, `a&&b`, `a=1&`, `=weird`, dup segments**; `BuildListingUrl` output provably unchanged (harness-asserted per case). |
| `tools/DbMigrationTests/Program.cs` | My stub members collided with the sibling B4 agent's concurrently-added empty/passthrough versions (CS0111) → removed MY duplicate block, kept theirs (it satisfies the parent's "stub returns empty/passthrough"). |
| `tools/DbMigrationTests/DbMigrationTests.csproj` | Linked `Core/Sources/FilterFacetSpec.cs`; de-duplicated the same include the sibling agent also added (identical item, non-destructive). |
| `tools/ParserTests/ParserTests.csproj` | Linked `FilterFacetSpec.cs`. |
| `tools/ParserTests/Program.cs` | New `--- B3 ---` section: `TestB3NafezlyFacetVocabulary`, `TestB3NafezlyQueryRoundTrip`, `TestB3MostaqlLegacyPassthrough` (**51 new checks**). B4 sibling's section/Main-calls untouched; inserted before the Tiny-harness region. |
| `Features/Projects/ViewModels/ProjectFeedViewModel.cs` | `HasFilterFacets` + `ActiveQueryText` observable; `FilterDescriptor`/`FilterStoredQuery` seams; both refreshed inside the existing `RefreshHeaderStatus()` (rides every load + the B2 `SwitchSourceAsync→LoadAsync` path); `ApplyFilterSelection(selection)` = `descriptor.BuildQueryFromFacets` → `ISettingsRepository.Set(source, SettingsKeys.QueryParams, built)` → `IPollService.QueryParams = built` (the exact Settings mutation pair). Added `using Microsoft.Extensions.DependencyInjection;`. |
| `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml` | (1) Header search row now `*,Auto` — mockup's **الفلاتر** button (`Projects_FiltersButton`) docks at the box's left, `IsVisible="{Binding HasFilterFacets}"`. (2) Active-query card: static "كل المشاريع" → `{Binding ActiveQueryText}`; **تعديل** + its pencil icon now open the modal (`IsVisible=HasFilterFacets`). (3) `filtermodal:FilterModal x:Name="FiltersModal" Grid.ColumnSpan="4"` overlay (always last in the root Grid = top z-order). |
| `Features/Projects/Views/Layouts/MainWindowWindowsLayout.xaml.cs` | Open handler (guards: no descriptor / empty vocabulary → no dialog), `ApplyRequested → feed.ApplyFilterSelection + hide`, `CloseRequested → hide`. |
| `Features/Projects/ViewModels/ProjectCardViewModel.cs` | `PublishTimeText` now prefers the site's scraped relative text with `ArabicRelativeTime.Since(DiscoveredAt)` as fallback (the UNITS-documented intended design; fixes nafezly rows reading "منذ دقيقة" for a project scraped as "منذ يوم"); new `StatusText`/`HasStatusText` (scraped `project_status` **verbatim**, trimmed; null → block hidden) + `NotifyPropertyChangedFor` on the existing `Project` swap attribute. |
| `Features/Projects/Views/Layouts/ProjectCardWindowsLayout.xaml` | Stats grid `Auto×5,*` → `Auto×6,*`; new **حالة المشروع** stat (label = site's own meta-panel label, value = raw scraped text, plain ink per mockup — no state-color retint) before the unread marker (which moved to the new last column). |
| `UNITS.md` | New `FilterModal` row in Platform Components (mechanism, base type, purpose, status **Implemented (B3)**), incl. unit-reuse note (AppIcon/AppEntry/chip buttons; no `SearchInputField` reuse — modal inputs are numeric `AppEntry` bounds). |
| `CHANGELOG.md` | One B3 bullet in Unreleased → Added (placed before the sibling's B4 bullet, which was preserved verbatim). |
| `.repertoire/.steering/v3/backend-ui-contract.md` | §1: new `FilterFacets / BuildQueryFromFacets / ParseFacetSelection` row → **REAL (B3)**. §8: nafezly row flipped **REAL (A3 backend + B3 UI)** with honest caveats (skills renders no chips; Esc-to-close + pixels pending Windows smoke). §8 mostaql row untouched (still REAL, behavior unchanged). |

## Commands run (exact results)

| Command | Result |
|---|---|
| `dotnet run --project tools/ParserTests` (first run, my descriptor edits, before my tests) | `TOTAL: 246 passed, 0 failed` — **note: parent's stated baseline 229 predates the sibling B4 wave's +85 lines / extra checks; 246 was the actual in-tree baseline at my add-time** |
| `dotnet run --project tools/ParserTests` (final) | `TOTAL: 297 passed, 0 failed` → **297 = 246 + my 51** (verified: `awk '/\[TestB3NafezlyFacetVocabulary\]/,0' \| grep -c PASS` → 51) |
| `dotnet run --project tools/DbMigrationTests` (final, twice) | `ALL DB MIGRATION CHECKS PASSED` (incl. sibling B4's new v3→v4 executor-migration checks compiling against my extended interface + stub) |
| grep gate: `specialize\|nafezly\|نفذلي\|التصنيفات\|photoshop` under `UI Features Core Services Models Infrastructure` | Only prose/comment mentions (documented rationale) — zero executable facet/site literals, zero source-id strings, zero `#if`, zero `if (source == <id>)` branches in my files (same comment-only shape as already-committed B2/B4 code) |
| API-surface verification (no app build allowed): `strings` on `~/.nuget/packages/microsoft.maui.controls.core/10.0.110/.../Microsoft.Maui.Controls.dll` | Confirmed `FlexWrap` enum name (my `Wrap = FlexWrap.Wrap`), `Shadow` setter, `ColumnDefinitions`; cross-checked `AppIcon.FontSize/TextColor` bindables, `Keyboard="Numeric"` usage, `AppEntry` chain, `ColumnDefinitions.Parse` (public MAUI API), Transparent-Button-overlay + AutomationId convention from `SourceSwitchTiles.cs` |

## Raw Evidence (key snippets)

- Interface added verbatim per parent: `IReadOnlyList<FilterFacetSpec> FilterFacets { get; }`,
  `string BuildQueryFromFacets(IReadOnlyDictionary<string, IReadOnlyList<string>> selections)`,
  `IReadOnlyDictionary<string, IReadOnlyList<string>> ParseFacetSelection(string? queryParams)`.
- Nafezly facets seeded (keys via `NafezlyQueryVocabulary` constants — no literals): `specialize` MultiChoice
  "التصنيفات" + the 7 existing `SpecializeFacets` (options built by projecting that SAME list — harness asserts
  `SequenceEqual` against it); `skills` MultiChoice "المهارات" Options `[]`; `pricing` Range "الميزانية" defaults null.
  Captions: التسميات headings quoted from fixtures — `التصنيفات` verbatim from `listing-filtered.html` sidebar
  group heading (line ~1295), `المهارات`/`الميزانية` from capture-manifest §3 headings.
- Manifest-canonical round-trip proven by harness:
  `?specialize=business%2Cdevelopment%2C…%2Cother&skills=photoshop%2Cillustrator&pricing=100%2C10000&page=1`
  parses to the exact 7/2/2/1-value selection, `","`-form parses identically (%2C-safe), and
  `Build(Parse(canonical)) == canonical` byte-for-byte (raw commas, specialize→skills→pricing→page order);
  one-facet edit rewrites only that facet; facet removals leave passthrough (`page=1`) alive; empty selection → `""`.
- Mostaql byte-identity proven over 14 adversarial suffix forms (incl. `?category=…`, `a&&b`, `a=1&`, `=weird`,
  dup segments) AND `BuildListingUrl(Parse→Build(s)) == BuildListingUrl(s)` for each — the poll URL cannot change.
- Mutation path: `SettingsViewModel.OnQueryParamsChanged` (re-read after sibling touched the file, lines 611-616)
  = `_settings.Set(_source, SettingsKeys.QueryParams, value)` + `ApplyPollSettings()` → `_pollService.QueryParams`.
  `ApplyFilterSelection` performs the same two writes. **No `RequestCheckNow()`** — Settings today doesn't re-poll
  immediately either, and the autonomy block said conservative default = today's Settings behavior.
- Layout diff points: `FiltersModal.Open(descriptor, feed.FilterStoredQuery)` passes descriptor + stored string;
  selection comes back as `IReadOnlyDictionary<string, IReadOnlyList<string>>` only. XAML/code-behind name no
  facet, no key, no site.

## Findings

1. **Concurrent B4 wave is live in this tree** (executor migration 0004, `DetailSchemaRenderer`, contract/CHANGELOG/UNITS
   rows, +85 ParserTests lines). Two real collisions hit me: duplicate `FilterFacetSpec.cs` csproj link and duplicate
   stub-descriptor members (CS0111). Both resolved additively (kept theirs, removed mine). The 229 baseline in my
   dispatch block is stale — in-tree baseline at add-time was 246; final 297.
2. **FilterFacetSpec needed no extension** — `Key/Kind/LabelAr/Options/MinDefault/MaxDefault` covers both render
   paths as-is (Range entries: Text=parsed value, Placeholder=spec default). No redesign attempted (parent fixed it).
3. **`skills` has no anonymous option vocabulary** (select2 loads per-page; capture-manifest §3) — seeded as
   `MultiChoice Options=[]`, and the modal *skips rendering* option-less choice facets (a caption with zero chips
   would be fake UI). Skills values still round-trip untouched through Parse→Build. Surfaced: a skills *search*
   facet needs either a scraped option source or a new kind (out of B3's fixed shape).
4. **Mostaql UX decision (presence-by-data)**: with `FilterFacets=[]` the modal would open showing only chrome —
   so the الفلاتر button + تعديل affordance hide for mostaql (`HasFilterFacets`), exactly preserving today's
   Settings-only preset-string flow. The mockup shows the button for mostaql, but the mockup's modal content
   (quick chips "غير مقروء فقط/تم الإثراء/ميزانية مفتوحة/جديد اليوم", sort select) is NOT facet vocabulary and
   NOT backed by `QueryParams` — it's illustrative local-DB/state filtering (see Open Questions).
5. **`تعديل` was previously bound to `SearchCommand` with no parameter** (a silent full reload — pre-existing
   mis-wire). Now opens the modal (when facets exist).
6. **ActivePipeline subtlety**: `MauiProgram` registers `IPollService` as a *transient* over `ActivePipeline.Poll`,
   but the Shell may construct the feed VM once at startup — the exact stale-pipeline gap B2's `SwitchSourceAsync`
   remarks surfaced. My Apply therefore resolves `ActivePipeline` fresh at click time (`GetService<ActivePipeline>().Poll`,
   the sanctioned accessor) and falls back to the injected `_pollService` in isolated hosts. Settings-behavior parity
   preserved (same property write), just correctly re-pointed on morph.
7. **Feed card status**: desktop card now renders scraped `project_status` verbatim when the row carries it.
   Both sources CAN carry it (nafezly at listing-parse time; mostaql only after enrichment writes the column) —
   so mostaql enriched rows will also show the stat (mockup mostaql cards don't show it; presence-by-data wins
   over per-source template branching since branching is the hard-rule violation). Windows-smoke visual caveat noted.
8. **Relative-time**: nafezly listings carry site-native `منذ …` text into `PublishTimeText`; the card now prefers
   it (fallback = computed from `discovered_at`). This also matches the UNITS-documented original design intent.
9. **Budget**: `BudgetFormatter.Format("10 - 25 $", "$")` → `"10 - 25 $"` verified by reading the regex/suffix logic —
   no double-$; nafezly budget string renders correctly. LTR isolation of `$` runs in the feed is **not implemented**
   (contract §7 assigns it to B4; B4 landed `BoolToFlowDirectionConverter` for details — feed card isolation is the
   exact same need: the budget `Label` in `ProjectCardWindowsLayout` should bind `FlowDirection` LTR whenever the
   source's `CurrencySuffix` is non-Arabic; behavior: mockup `dir="ltr"` on the nafezly budget span, `10 - 25 $`
   renders RTL-scrambled otherwise). Recorded here for the parent; B4's converter + a card-side suffix check could
   close it, but the card layout is the feed surface and the details renderer is theirs — left un-fought.
10. Esc-to-close (mockup JS) needs platform keyboard hooks or a native dialog (see Open Q2) — not in the overlay unit.

## Open Questions / Blockers (surfaced, not guessed)

1. **Mockup modal content ≠ typed facets.** The v3 mockup's dialog shows quick-state chips (unread/enriched/
   open-budget/new-today), a min-budget number, and a sort dropdown. None of those is `QueryParams`-expressible
   (they're local-DB feed filters; sort has no nafezly query support per capture-manifest — pagination is
   prev/next only). Per the parent block ("renders active descriptor's FilterFacets"), I rendered facets ONLY.
   Decision needed: do the quick-chips become a *local* filter feature (would touch feed query, not descriptors)
   or get dropped from the mockup?
2. **Modal host mechanics.** Chosen: in-layout overlay (proven flyout pattern, zero platform code, works across
   MAUI targets). Alternatives: (a) `ModalPresenter`+native `ContentDialog` (its own UNITS note forbids
   Shell/navigation dependence and today only hosts confirmation wording — hosting arbitrary MAUI content in
   WinUI `ContentDialog` is a sizeable platform task), (b) CommunityToolkit.Maui `Popup` (package NOT referenced —
   would add a dependency). Esc/backdrop: backdrop-tap cancels ✓, Esc ✗ pending (a) or platform hooks.
3. **Apply-then-poll-now semantics**: conservative default applied (no immediate `RequestCheckNow`), matching
   Settings-today. If product wants the new query to fire a scan instantly, it's a one-line addition to
   `ApplyFilterSelection` — owner call.
4. **Partial pricing band** (min xor max): dropped rather than guessed (site grammar is always `min,max`;
   seeding nafezly's Min/MaxDefault wasn't justified by any capture fact). Acceptable? (Also: مسح الكل clears only
   the rendered facets; `page`/unknowns intentionally survive.)
5. **Mockup data gaps on nafezly cards → not invented**: "مشرف عليه" chip (`HasSupervisedFlag=false`, session-gated
   per manifest §1.10 → correctly never rendered), "مطابقة 87٪" (self-labelled design concept), owner sub-line
   `صاحبة مشروع "لبسك علينا"` (no field on `ProjectSummary`), retry/403 banner rows (contract §9 UI = B4).
   Label wording: nafezly mockup uses "العروض المقدمة" where the shared card label is "عدد المقترحات" — a
   per-source card-label template is schema/UI-phase (B4) work; card keeps the single shared label.
6. **Mobile feed** (`MainWindowMobileLayout`) hosts no filters affordance/modal yet — v3 mockups are desktop-only;
   the unit itself is platform-neutral so mobile wiring is a call-site addition.
7. **Windows on-device smoke list** (headless-unverifiable here): dialog open/close/backdrop; chip pressed colors
   (brand-soft/brand border/brand-ink) + Apply → poll follows (esp. nafezly cadence); mostaql shows NO filters
   button; active-query line shows the stored string; nafezly card: `حالة المشروع` text, `10 - 25 $` budget
   (RTL-scramble check per Finding 9), relative-time from scrape; CollectionView item recycling with the new
   stats column (hidden status leaves a small ColumnSpacing gap for mostaql pre-enrichment rows — cosmetic).
8. Parent's dev17 commit landed mid-flight (tree confirmed clean at my start apart from `scratch/`, then B4
   arrived) — CHANGELOG/tag: **no commit made, no tag bump**, per brief.

## Next Actions (suggested)

1. Parent: run the app build (net10.0-windows) — my UI files (`FilterModal.cs`, both layouts, VM) are the first
   compile they've never had; everything else is harness-verified.
2. Owner calls on Open Q1/Q2/Q3 (quick-chips scope, native-dialog host, apply-then-poll-now).
3. B4/parent: reuse `BoolToFlowDirectionConverter` + a suffix-driven flag for the feed card budget LTR isolation
   (Finding 9) and decide the per-source stat-label template question (Open Q5).
4. When a skills option source exists (or a `SkillsSearch` kind is approved), remove the modal's option-less skip.
5. Windows smoke per list 7; flip §8's remaining caveat wording once pixels agree with the mockups.

## Skills used

None available as configured (`.cursor/skills/` absent in this Linux codespace — parent block acknowledged);
followed repo patterns (`UNITS.md` conventions, flyout-host mechanic, SourceSwitchTiles code-component style) instead.

# details-visual-implementer

## Goal
Restyle the Windows **Project Details** page to the v3 mockup design truth
(`.repertoire/design/postmvp/v3-multisource/project-details.html`): continuous full-page surface
(no floating cards), a big scraped-title header, a 5-fact **decision strip**, schema-driven
description/skills/executor sections, a dedicated **owner card**, attachments, and a footer
**copy-link + mark-as-read-and-return** action row. Bound to existing VM/descriptor data; no
`if (source == …)` branches; brand family only via `{DynamicResource}`; state colors fixed, never
brand; no new animations.

## Files touched
- `Features/Projects/Views/Layouts/ProjectDetailsWindowsLayout.xaml` — full content-column restyle.
- `Features/Projects/Views/Layouts/ProjectDetailsWindowsLayout.xaml.cs` — added `_viewModel`/`ViewModel`
  accessor + `OnCopyLinkClicked` (clipboard) + `OnMarkAsReadClicked` (mark-read then navigate to feed).
- `Features/Projects/ViewModels/ProjectDetailsViewModel.cs` — new data-driven members (see below).
- `Features/Projects/ViewModels/DetailSchemaRenderer.cs` — added the `DecisionFact` record (co-located
  with the other UI records).

`ProjectDetailsMobileLayout.xaml` and `ProjectDetailsPage.xaml` were **NOT** changed (mobile layout
still binds the unchanged `MetaSections`; no binding glue forced a page change).

## Structure mapping (mockup → implementation)
- **Header band** (surface + bottom hairline): title = `Details.Title` (centered, big); caption chip
  row = source chip (`SourceNameAr`, `BrandSoft`/`BrandInk`) + the existing enrichment badge (kept) +
  `· اكتُشف {DiscoveredAtText}`; then a row with a round ~36px back control (`ChevronRight` glyph over
  a transparent `PressableButton` so `Details_BackButton` stays reliably invokable for the UI test) +
  breadcrumb, and the brand CTA (`SourceLinkButton`, `Details_OpenOnSourceButton`) at the inline-end.
  CTA label "عرض في {source}" is the SourceLinkButton's own descriptor-driven text.
- **Decision strip** (`DecisionFacts`, fixed approved order الميزانية · مدة التسليم · العروض المقدمة ·
  الحالة · نُشر): tiny `ink-3` label above a 22px bold value; budget + status render in the fixed
  accent-positive state green (`#2E9E6B`/`#4FBF8C`) — never brand; budget gets `FlowDirection` LTR
  isolation via `BudgetIsMoney` (descriptor currency-suffix driven, reuses `DetailSchemaRenderer.MoneyNeedsLtr`).
  Each fact is emitted only when its value is present (data-driven omission, no "—").
- **الوصف**: `Details.Description` clamped to `DescriptionMaxLines`=2 (`TailTruncation`) with a
  brand-ink "عرض المزيد"/"عرض أقل" toggle (`ToggleDescriptionCommand` → `IsDescriptionExpanded`);
  toggle only shows when the text is long enough to actually clamp (`ShowDescriptionToggle`). Skills
  chips reuse the existing treatment, recolored to `BrandSoft`/`BrandInk` per v3.
- **Remaining schema sections** (`SchemaSections`): `MetaSections` minus the `project` and `owner`
  groups (which now have dedicated treatments). In practice this is nafezly's `executor` group, still
  fully capability-gated by `DetailSchemaRenderer` (`HasExecutorInfo` + a present name) — kept schema-driven.
- **Owner card** (`ShowOwnerCard`): brand-avatar initials circle (`{DynamicResource Brand}` fill +
  `OnBrand` text), name (`OwnerName`), sub-line (`OwnerSubLine` = scraped `Owner.RegisteredAt`,
  `HasOwnerSub`-gated), and inline stat cells (`OwnerStats` = owner-section rows minus name/registration;
  nafezly's name-only owner schema → no cells).
- **المرفقات**: existing attachment rows kept intact (browse/resolve/reveal `AppButton`s + all
  `Details_Attachment*` AutomationIds).
- **Retry** (`Details_RetryButton`, `ShowRetryAffordance`) kept in-flow before the footer.
- **Footer actions** (border-top): `نسخ رابط المشروع` (`GhostButtonStyle` → `Clipboard.Default.SetTextAsync(Details.Url)`)
  + `تحديد كمقروء والعودة` (brand-filled `AppButton` → `MarkAsReadAsync()` then `AppRoutes.NavigateAsync(AppRoutes.Projects)`).

## VM properties added (all data-driven, no source-id)
`SourceNameAr`, `ShowSourceChip`, `DiscoveredAtText`, `BudgetIsMoney`, `DecisionFacts` + `HasDecisionFacts`,
`SchemaSections` + `HasSchemaSections`, `OwnerStats` + `HasOwnerStats`, `ShowOwnerCard`, `OwnerInitials`,
`OwnerSubLine`, `HasOwnerSub`, `HasDescription`, `IsDescriptionExpanded` + `DescriptionLineBreakMode` /
`DescriptionMaxLines` / `DescriptionToggleText` / `ShowDescriptionToggle`; commands `ToggleDescription`,
`MarkAsReadAsync`; rebuild helpers `RebuildDecisionStrip` + `ResetDescriptionState` (called from `LoadAsync`).

## Removed vs the old layout (mockup-driven)
1. The two-column grid (main `2*` + side meta `*`) → single continuous full-width surface.
2. All floating rounded `Border` "cards" (`Border`/`RoundRectangle`) → flat in-flow sections separated
   by hairline `BoxView` dividers (mockup = no floating cards).
3. Static header label "تفاصيل المشروع" + static "المشاريع" crumb → scraped title + live breadcrumb.
4. `· آخر تحديث: {EnrichedAt}` line → replaced by `· اكتُشف {DiscoveredAt}` per mockup (requirements §7
   removed the "added-at" tail).
5. The generic `MetaSections` "بطاقة المشروع" + "صاحب المشروع" groups → replaced by the decision strip +
   owner card (the `executor` group remains, via `SchemaSections`). Mobile keeps rendering full `MetaSections`.

## Deliberate deviations (reported, not silently improvised)
- **Back/copy/mark-as-read icons**: the mockup uses a long-arrow, a copy, and an eye-slash glyph. Those
  `AppIconGlyph` entries have **no pre-baked PNG artwork** (they fall back to the info icon), and
  `UI/PlatformComponents/**` (AppIcon enum + artwork + generation scripts) is **off-limits to this agent**
  per the concurrency boundary. I used the closest existing glyph (`ChevronRight`) for back and **omitted
  the copy/eye-slash icons** (text-only buttons) rather than render a wrong "info" icon or edit a
  forbidden file. The header CTA icon is the SourceLinkButton unit's own artwork.
- **Owner "verified" check**: the mockup owner row shows none (the verified check belongs to the
  executor block), and there is **no `Owner.IsVerified`/verification field scraped** — so per the
  "honor nullability, no data → omit" rule it is omitted (no fabrication).
- **Section-head icons**: the mockup's `section-head` blocks carry no icons, so section titles are
  text-only (also sidesteps the missing-artwork problem).
- No animations added (honors `animation-safety.md`); sticky headers rendered in-flow (MAUI has none —
  pre-decided).

## AutomationId changes
**All preserved**: `Details_BackButton`, `Details_ScrollView`, `Details_AttachmentsList`,
`Details_AttachmentBrowseButton`, `Details_AttachmentResolveButton`, `Details_AttachmentRevealButton`,
`Details_RetryButton`, `Details_OpenOnSourceButton`. **None added.** (Back/copy/mark-as-read carry no new
AutomationId; the mark-as-read + copy buttons are new controls but the task did not require ids for them.)

## New UNITS.md row required?
**No.** No new reusable PlatformComponent/Concept/DesignSystem unit was introduced — the decision strip
and owner card are page-specific arrangements of existing units (AppButton, AppIcon, SourceLinkButton,
PressableButton, `GhostButtonStyle`, ShimmerBox, LabelWithSubText). The `DecisionFact` type is a
view-model data record, not a UI unit. Nothing to add to `UNITS.md` (held by W1).

## Verification (gate results)
1. `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` → **Build succeeded, 0 Warning(s), 0 Error(s)**
   (3 consecutive attempts). Note: an earlier attempt hit a transient `MSB3027/MSB3021` file-lock
   (a running `MostaqlK.exe` held the output — another agent's instance); per instructions I did **not**
   "fix" the lock and it cleared on retry. During that locked build, no compile error or warning
   referenced my files (`grep` of warnings for `ProjectDetails|DetailSchema` = none).
2. `dotnet build MostaqlK.csproj -f net10.0-android` → **blocked by environment, not code**:
   `XA0030: Building with JDK version 22.0.2 is not supported. Please install JDK version 21.0`. Only
   JDK 17/18/22 are installed on this machine (no 21), so the Android target cannot be built here at all
   (fails before compilation). My changed files are platform-neutral (shared VM + XAML already
   compile clean on the Windows target; `ProjectDetailsMobileLayout` untouched), so there is no
   Android-specific code path to break.
3. `dotnet run --project tools/ParserTests` → **TOTAL: 297 passed, 0 failed — All checks passed.** (unaffected)
4. Grep of my edited files: no inline brand hex (all brand via `{DynamicResource Brand/BrandInk/BrandSoft/OnBrand}`;
   only ink + fixed state-green hexes inline, matching the existing `AppThemeBinding` pattern);
   no `mostaql`/`nafezly` literals and no source-id branch — the only `== SourceId` is the existing
   deep-link default in `ProjectDetailsPage.xaml.cs` (not my edit); the `spec.Key == "executor_name"`
   test is a field-key gate, not a source-id branch.

# Nafezly Parser Stress-Audit — Report (scratch agent)

Scope: read-only on tracked files; writes limited to `scratch/nafezly-audit/`, `scratch/nafezly-audit-out/`, this file.
Gates run: `dotnet run --project scratch/nafezly-audit`, `dotnet run --project tools/ParserTests` (297/297 PASS — read-only gate, untouched by this audit).

## 0. Tool fixes made (scratch only — no parser file touched)

**(a) rows=0 root cause.** It was NOT the hard-coded listing filenames (those four — `listing-page1/2`, `listing-filtered`, `listing-page3-probe` — are exactly the files present, verified against `.meta.json` siblings). The real bug: `Path.GetFullPath("../../tools/ParserTests/Fixtures/nafezly")` resolves relative to `dotnet run`'s cwd (the project dir `scratch/nafezly-audit`), landing on `/workspaces/tools/...` → every `File.Exists` silently failed → 0 details, 0 rows, empty histogram. Fixed with the absolute fixture dir; filename enumeration kept explicit + verified. Also removed a dead nonsense line (`long.Parse(Regex.Match(...,"x") ? "0" : "0")`).

**(b) LastProvenance coverage — verified BROKEN/incomplete; tier re-derived in-harness.**
`NafezlyDetailParser.LastProvenance` (diagnostic API at `NafezlyDetailParser.cs:40-44,103`):
- Records only the 5 label-loop fields (`project_status, published_date, duration, budget, proposal_count`). `title/description/skills/owner/executor` are resolved **outside** the loop and are absent entirely (`reportedTier=None` in dump).
- Collapses the structural and label tiers: the loop accepts a value from `ExtractMetaFields` **or** `LabelDrivenExtract` and stamps both `"structural"` (lines 74-94).
- Does not mirror the two post-loop overrides: `duration`→jsonld (lines 150-155) and `proposal_count`→offer-row count (lines 164-173). Empirically all 3 pages show `LastProvenance='structural'` for `duration` while `FieldProvenance` (the model, `d.FieldProvenance`) correctly says `jsonld`. **Recommendation to parent: read `ProjectDetails.FieldProvenance` instead of `LastProvenance` for anything beyond the loop fields; `LastProvenance` is safe only as a loop-tier + inference-confidence probe.**

**Tier-derivation method used instead (documented for reproducibility):** re-invoked the exact public APIs in the parser's order (`NafezlyDetailParser.cs:51-100`): duplicate `FindMetaCard` via `HtmlStructureEngine.FindLabelElements("بطاقة المشروع")` + ancestor climb requiring both `الميزانية`+`عدد المتقدمين` → `ExtractMetaFields(metaCard, NafezlyParseProfile.Profile, NafezlySelectors.Selectors)` = **structural**; else `LabelDrivenExtract(root, KnownLabels)` = **label**; else duplicate `SanityOk` (NumericFields + NotCalculatedMarkers) and `FieldInferenceEngine.InferFields(root, FieldProfiles)` = **inference** (with `Confidence`+`Strategy`); then apply the parser's documented post-loop overrides from `NafezlyJsonLd.Extract` (**jsonld** for duration when `workHours` present) and `SelectByClassContains(div,"offer-order")` count (**structural(offer-rows)** when >0). Cross-checked against both `LastProvenance` and `FieldProvenance`; divergences in §3.

## 1. Volume

Command output (verbatim): `parsed: details=3 (failures=0) listingPages=4 rows=80 uniqueProjects=80`

| Page | Rows parsed | project-box divs | id span |
|---|---|---|---|
| detail-open (56540) | — (true state **تحت التنفيذ**) | — | — |
| detail-in-progress (56515) | — (true state **مكتمل**) | — | — |
| detail-extra (56573) | — (true state **مفتوح**, no executor — open) | — | — |
| listing-page1 | 20 | 20 | 56580→56609 |
| listing-page2 | 20 | 20 | 56544→56579 |
| listing-filtered | 20 | 20 | 26549→56437 |
| listing-page3-probe | 20 | 20 | 56515→56542 |

**50-project sample: EXCEEDED from fixtures alone** — 80 unique listing projects (4 pages × 20, zero cross-page duplicate ids) + 3 enriched detail pages. **Ceiling statement:** fixtures cap at **80 listing rows + 3 detail enrichments**. More detail-level coverage (only 3 detail pages exist) or further listing pages requires the owner's residential-network paced scrape — this codespace's IPs draw Cloudflare JS-challenge 403s (contract §11 "Live-polling precondition"; capture-manifest rate-limit incident §Method). No network fetch was attempted.

## 2. JSON output

- `/workspaces/mostaql.k/scratch/nafezly-audit-out/dump.json` — per detail project: all parsed fields with `{tier, confidence, strategy, value, reportedTier}` per field; per listing page: pagination + per-field resolution counts + all 20 rows with per-field tier map.
- `/workspaces/mostaql.k/scratch/nafezly-audit-out/stats.json` — volume, re-derived detail tier histogram, `LastProvenance` as-reported histogram, listing field-resolution counts, provenance divergences, anomalies.

## 3. Inference hotspot? — NO. 0%.

Detail tier histogram (5 panel fields × 3 pages = 15 resolutions, re-derived final tiers):

| Tier | Count | % |
|---|---|---|
| structural (ExtractMetaFields label→value rows) | 9 | 60% |
| jsonld (LD `workHours` override of `duration`) | 3 | 20% |
| structural(offer-rows) (deterministic `div.offer-order` count) | 3 | 20% |
| label (WalkToValue) | 0 | 0% |
| **inference (FieldInferenceEngine)** | **0** | **0%** |
| none | 0 | 0% |

`FieldInferenceEngine.InferFields` is lazily invoked **only when a panel field fails sanity** (`NafezlyDetailParser.cs:97`); every field passed on all 3 fixtures, so **the engine never even executed** on this corpus. Field-by-field (identical pattern on all 3 detail pages):

| Field | Tier | Conf | Value (56540 / 56515 / 56573) |
|---|---|---|---|
| project_status | structural | 1.0 | تحت التنفيذ / مكتمل / مفتوح |
| published_date | structural | 1.0 | منذ 23 ساعة / منذ يوم / منذ 11 ساعة |
| duration | jsonld | 1.0 | 3 / 1 / 7 (days) |
| budget | structural | 1.0 | `10 - 25 $` / `10 - 25 $` / `50 - 100 $` |
| proposal_count | structural(offer-rows) | 1.0 | 72 / 43 / 30 `عروض` (offer rows == عدد المتقدمين label on all 3 — exact match, manifest §1.6 confirmed) |
| title / description / skills | jsonld | 1.0 | JobPosting primary on all 3 |
| owner / executor | structural(direct-anchor) | 1.0 | Anas Osama·Abdelrahman Moneer / دليل الخير·عبدالرحمن الزرقانى / Saudisader Sader·null (open → absent, per manifest §1.9) |

Listing rows (80 rows × 5 meta fields = 400 resolutions): **100% icon-span (structural), 0% inference** — the listing parser never references `FieldInferenceEngine` at all. Heuristic-Contains vs icon breakdown per row:
- **Class-Contains structural:** row detection (`div.project-box`), snippet (`h3.naskh`), title link (`a[contains @href '/project/']` + regex `/project/(\d+)`) — all succeeded (20/20 rows/page; the identifier-blind redesign fallback never triggered: `detectedProjectBoxes == parsedRows` everywhere).
- **Icon structural:** budget `fa-usd-circle` 80/80, duration `fa-business-time` 80/80, proposals `fa-ballot` 80/80, publish `fa-clock` 80/80, state `fa-check-circle` 80/80. No field on any row fell back to anything.

Provenance divergences (only 3, all the same class): `duration` final tier jsonld but `LastProvenance='structural'` on all 3 pages (override not mirrored into the diagnostic; `FieldProvenance` is correct). `proposal_count` shows `LastProvenance='structural'` value `'72'` while final value is the offer-row count — coincidental agreement, but the same non-mirrored-override class.

## 4. Tuning recommendation

**Trigger test: inference = 0% (< 15% threshold) and zero confidence < 0.5 on any core field (budget/state/proposals/duration all conf 1.0).** → **No `NafezlyParseProfile.KnownLabels` / `NafezlySelectors` change is justified for tiering.** The label rows pair structurally (`text-right` label / `font-1` value inside the scoped بطاقة المشروع card), so `ExtractMetaFields` resolves all five facts without ever needing the label tier or inference.

Prioritized proposals (data-level and parent-side, none touching engine logic):

| P | Proposal | Exact edit (parent applies) |
|---|---|---|
| P1 | De-entity pagination hrefs — `NafezlyListingParser.ParsePagination` returns raw `&amp;` from the echoed filtered next-URL (confirmed live on `listing-filtered.html`; a poller fetching that URL sends a literal `&amp;` param) | Inside `ParsePagination.Read`: `href = HtmlEntity.DeEntitize(href)` before returning (parser-level, one line; capture-manifest §2 explicitly expects `&laquo;/&raquo;/&amp;` entities) |
| P2 | Fix `LastProvenance` mirroring OR deprecate it in favour of `ProjectDetails.FieldProvenance` (already correct: duration→jsonld, proposal_count→structural): instrument the two post-loop overrides and stamp label-tier distinctly (`"structural"` vs `"label"` at NafezlyDetailParser.cs:89-94), else docs/consumers will keep trusting a mis-throttled diagnostic | parser-diagnostic-side, parent-owned |
| P3 | Listing rows never populate `ProjectSummary.ClientName`/country although manifest §2 says the desktop owner row is server-rendered (avatar + name + `fa-map-marker-alt` country) | data-level: add `ListingOwnerRowClassSubstring`-style entries to `NafezlySelectors` (e.g. owner-row wrapper marker `d-lg-flex d-none`, `data-profile-role=owner` already known) and read name/country in `ParseRow` — only if product wants client on the card; otherwise mark capability "owner-on-listing = not parsed" in the contract |
| P4 | (Watch item, no edit) `مغلق` vocabulary: `NafezlyListingParser.StateVocabulary` defensively lists it but it was never observed (manifest §5). Current behavior is correct — unknown state text is scraped verbatim, and the anomaly sweep flags any new string | none |

## 5. Anomaly sweep — results

Checks run across all 7 pages (values from the actual parser output, entity/RTL sweep over every string in dump.json):

| Sweep | Result |
|---|---|
| States outside مفتوح/تحت التنفيذ/مكتمل/مغلق | **0** — listings: مفتوح 47, مكتمل 27, تحت التنفيذ 6; details cover the 3 live states exactly; مغلق never seen |
| Budget shape `N - N $` | **0** — all 83 values match `^[\d.,]+\s+-\s+[\d.,]+\s+\$$` (bands: 10-25 ×42, 100-250 ×16, 25-50 ×8, 250-500 ×7, 500-1000 ×3, 50-100 ×3, 2500-5000 ×1) |
| Proposal text/number mismatches | **0** rows; details: عدد المتقدمين == offer-row count (72/43/30) exactly |
| ids ≤ 0 | **0** |
| Duplicate ids across pages | **0** (per-page dedupe set + cross-page map both clean) |
| Titles empty/huge | **0** (len 11-70; **11/80 listing titles are site-side truncated with `...`** — manifest §2 documents this server-side truncation; the detail JSON-LD title is full — expected, not a defect) |
| Descriptions missing on listings | **0** — every row has a non-empty `h3.naskh` snippet (expected: manifest §2 lists snippet as row grammar) |
| Encoding artifacts | **1: pagination `next` URL on `listing-filtered.html` leaks `&amp;`** (see P1). No entities/RTL-control-marks in any title/status/budget/proposal/publish/owner/executor/skill/snippet value; Arabic-Indic digits never appeared (all ASCII) |
| EnrichmentStatus=Failed on any detail fixture | **0** — all 3 Enriched |
| Parser-recorded `Mismatches` (structural vs inferred disagreement) | **0** |
| Duplicate skills | **0** |
| Row count vs `project-box` div count per page | equal (20=20 on all 4 pages) |
| Detail parse failures / listing parse throws | 0 / 0 |

## Findings
- 80 unique projects sampled from fixtures (≥50 target met); all 7 fixtures parse with zero throws, zero anomalies except the one entity leak.
- Inference engine: **0% of resolutions, never executed** — nafezly's tiering (jsonld → structural meta rows → deterministic offer count → icons) fully covers every field on real pages.
- `LastProvenance` is real but incomplete: 5 loop fields only, structural/label collapsed, 2 post-loop overrides unmirrored (duration, proposal_count); `FieldProvenance` on the model is the accurate surface.
- One genuine parser-output defect: raw `&amp;` in `ParsePagination` hrefs.

## Open Questions
- Does the product need owner name/country on listing cards (ClientName is structurally available in the DOM but never parsed — P3)?
- Expected behavior for site-truncated listing titles is assumed ("store as displayed, detail view carries full jsonld title") — confirm against the UI contract if the card should prefer a full title from any source.
- `مغلق` state remains unverified (never observed, session-gated site behavior possible) — needs a closed-project fixture from the owner's scrape.

## Actions for Parent
1. Apply P1: `HtmlEntity.DeEntitize` inside `NafezlyListingParser.ParsePagination`.
2. Decide P2: mirror the jsonld/offer-count overrides into `LastProvenance` (and split `"label"` from `"structural"`), or standardize diagnostics on `ProjectDetails.FieldProvenance` and document `LastProvenance` as loop-fields-only.
3. Product call on P3 (listing ClientName/country extraction — data-level selectors + `ParseRow` addition).
4. Schedule the owner-side paced scrape for >80 rows / more detail pages / a `مغلق` fixture (codespace blocked by Cloudflare, contract §11).
5. No profile/selector tuning required for tiering (Q4 threshold not triggered).

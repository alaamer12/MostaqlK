# parsers-detail-worker — Wave B5 Report

## Goal

Port `StructuralExtractor.cs` and `DetailParser.cs` to Python with behavioral
parity (attachments extraction DROPPED per scope lock), per the frozen plan
§3.C.2 / §3.D / §4.1 / §8.

## Files created/modified (all within mandate)

| File | Status |
|---|---|
| `.repertoire/python/src/mostaql/scraping/parsers/structural.py` | implemented (~480 lines) |
| `.repertoire/python/src/mostaql/scraping/parsers/detail.py` | implemented (~770 lines) |
| `.repertoire/python/tests/test_structural_extractor.py` | new (36 tests) |
| `.repertoire/python/tests/test_detail_parser.py` | new (43 tests) |
| `.repertoire/python/tests/regression/fixtures/detail/*.html` | 12 new fixtures |
| `.repertoire/agents/parsers-detail-worker.md` | this report |

Untouched as required: pyproject.toml, inference.py, listing.py, parsers/errors.py,
scraper.py, all barrels.

## What was ported

### structural.py (C# StructuralExtractor.cs)

- `KNOWN_LABELS` — 17 labels verbatim (cs:31-50). The C# asymmetry is preserved:
  `"مشاريع منجزة"` is a known label while its synonym `"المشاريع المنجزة"`
  (which exists only in DetailParser's table) is NOT.
- `normalize` / `to_ascii_digits` / `normalize_label` re-exported from
  `mostaql.text.normalization` (identity-aliased, asserted in tests).
- `normalize_multiline(element)` — faithful port of the own-text walk
  (cs:185-218): text runs → `node.text` + child `tail`s; `<br>` → `\n`; block
  tags append `\n` AFTER recursing (verified against C# semantics: the boundary
  newline separates the block from FOLLOWING siblings); then `[ \t]+`→" ",
  around-newline trim, `\n{3,}`→`\n\n`, strip. DeEntitize pass omitted (ENTITY RULE).
- `extract_meta_fields(root)` — panel `#project-meta-panel` else class-token
  `meta-container`; `meta-row`/`meta-label`/`meta-value` via per-token contains;
  owner stats: Path A exactly-2-td rows of first token-contains-`table` table;
  else Path B `justify-between` with exactly 2 element children (RAW-substring
  XPath predicate, cs:287) + Path C gap-filler ALWAYS running in that branch:
  exact-match → next sibling ELEMENT; else concatenated repair removing ALL raw
  label occurrences then trimming LabelTrimChars (trap §4.1-17). ContainsKey
  guards never overwrite.
- `find_owner_card(root)` — 3-step cascade (cs:366-411).
- `_find_label_elements` / `_walk_to_value` / `label_driven_extract` — OwnText +
  leaf-full-text matching; 5-rung ladder with method strings
  (`next_sibling_of_label`, `next_td`, `parent_next_sibling`,
  `parent_text_minus_label`, `grandparent_sibling_cell`).
- Class-matching helpers in three distinct styles per trap §4.1-10:
  `_by_exact_token` (concat-trick XPath), `_by_token_contains` (per-token
  substring), `_raw_class_contains` (whole-attribute substring).

### detail.py (C# DetailParser.cs)

- Title chain h1 → og:title(property|name) → `<title>`; LAST-separator suffix
  cut when suffix contains مستقل/Mostaql/Mostaqlk (OrdinalIgnoreCase), then
  bare trailing-keyword second pass (cs:361-390). Empty ⇒ PARSE-002.
- Description: `text-wrapper-div` inside `#projectDetailsTab` (NormalizeMultiline),
  og:description single-line fallback, densest block preferred ONLY when strictly
  longer than og teaser (>200-char floor) (cs:398-472).
- Skills: exact-token `ul.skills` → substring-token → identifier-blind href sweep
  (`/skills/`, `skill=`, `/tag/`, case-insensitive; len 1..60; OrdinalIgnoreCase
  dedupe) (cs:480-538).
- Field combinator EXACT: verbatim `LABEL_TO_FIELD` (20 entries) → order-preserving
  `FIELD_TO_LABELS` grouping; structural-then-label-driven lookup per synonym;
  `SanityOk` (placeholders VALID; numeric fields need any digit incl Arabic);
  lazy inference ONCE per page; conditional cross-validation (null either side
  agrees; mismatch records appended whenever inference exists AND s_val non-null;
  override ONLY on failed sanity); placeholder finals nulled WITH provenance kept.
- Gates: completed-only label-presence pass on inference-sourced values
  (whole-page NormalizeLabel scan); bid-count override `count(//*[@data-bid-item])`
  > 0 ⇒ "{n} عروض" structural 1.0 EVEN n==1 (trap §4.1-4), else null when no
  proposal label present; completion-status gate nulls started_since/deal_date/
  delivery_date but NEVER completed_projects_count, preserving source/confidence.
- Owner assembly: name chain ×5 + صاحب المشروع ≤80 + scoped /u/-link 1..60;
  profile URL absolutization (slash inserted unless leading "/");
  id cascade data-user-id (card or descendant, TryParse fall-through) → username
  segment after "u" (ordinal split) → wrapping signed-64-bit h*31+ord hash over
  username then display name → 0 (trap §4.1-3).
- Numeric: `parse_percent` (`\d+(?:[.,]\d+)?`, ToAsciiDigits first, comma→dot,
  invariant float); `parse_leading_int` (grouped alternative preferred, separators
  deleted, Int32 TryParse bound ⇒ None on overflow) — nullable fields default None,
  counts/times default 0 via parse_proposals/parse_relative_number, mirroring C#.
- Result: `url=""` (scraper fills later, trap §4.1-2), ENRICHED, ONE
  `datetime.now(UTC)` stamped to both discovered_at/enriched_at (trap §4.1-22),
  provenance dict + mismatches list. Attachments: NONE (dropped scope).

### ENTITY RULE (documented in both module docstrings)

lxml decodes entities at parse time ⇒ every C#
`Normalize(HtmlEntity.DeEntitize(InnerText))` became plain
`normalize(text_content())`; NO double-unescape anywhere (protects literal
`&amp;amp;` payloads). Same for the page-wide gate text (cs:164).

## Verification (final sweep, from .repertoire/python)

| Gate | Result |
|---|---|
| `uv sync` | ok |
| `uv run ruff format --check .` | PASS (62 files) |
| `uv run ruff check .` | MY FILES CLEAN; 1 remaining finding in **sibling** inference.py (RUF022 `__all__` sort) |
| `uv run mypy src` | PASS — 43 files, 0 errors (baseline had 3; my DetailParser fixed the 2 scraper attr-defined/no-any-return ones) |
| `uv run xenon src -b B` | PASS (5 blocks initially rank-C were decomposed into ≤B helpers) |
| `uv run lint-imports` | pipeline-free + pure-leaves KEPT; `httpx-only-in-http-layer` BROKEN by **sibling-owned** `scraper.py → mostaql.http → httpx` chain (pre-existing, not my modules) |
| `uv run pytest -q` | **377 passed**, coverage 93.46% |

## Decisions & deviations (reported, none behavioral)

1. **Lazy inference seam**: `detail._infer_fields_once` imports the
   sibling-owned module at call time via `importlib` + `getattr`
   (`# noqa: B009`) instead of a module-level import. Rationale: inference.py
   was still a 4-line stub when I started; this decouples my module's import
   health from sibling timing. Production shape matches the frozen contract
   exactly (`InferenceEngine.infer_fields(root).fields`), verified live after
   the sibling landed it (real engine returned budget='250 دولار.' @0.9 through
   the seam). Tests patch the seam (autouse stub) — they never depend on real
   engine behavior.
2. **etree.ParserError guard**: junk HTML that makes lxml throw maps to
   PARSE-002 (HAP yields an empty DOM whose title chain exhausts → same outcome).
3. **Faithful sequencing quirk pinned by test**: cross-validation can never fire
   for the FIRST field (project_status) because inference isn't computed yet at
   its turn — true in C# too (trap §4.1-11). Documented in
   `test_first_field_is_never_cross_validated`.
4. Fixtures needed 3 corrections during bring-up (wrong synonym label not among
   the extractor's 17 KnownLabels; ladder-rung interference between adjacent
   demo blocks; missing `<br/>` before a `<p>` — C#'s block newline goes AFTER
   the block content). All fixture-side; no code changes.

## Concerns for the master

- `lint-imports` httpx contract is broken by sibling-owned scraper.py (Wave C):
  either the contract's source_modules should exempt `mostaql.scraping.scraper`,
  or scraper should receive the fetcher differently. Not mine to fix.
- `ruff check` has one RUF022 finding inside sibling-owned inference.py
  (`__all__` ordering) — one-line fix on their side.
- Owner synthetic ids use a WRAPPING signed-64-bit hash to mirror C# unchecked
  long overflow; usernames longer than ~60 chars diverge from naive unbounded
  Python ints — intended parity, worth knowing during golden comparisons.

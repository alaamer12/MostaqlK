# parsers-listing-worker — Wave B4 Report

## Goal

Port C# `Infrastructure/Http/Parsers/ListingParser.cs` to `mostaql.scraping.parsers.listing`
with behavioral parity, per frozen plan §3.C.1 / §4.1 traps / §8 signatures. lxml.html backend.

## What was done

### Files created/modified

| File | Action |
|---|---|
| `src/mostaql/scraping/parsers/listing.py` | Implemented `ListingParser.parse` + private helpers |
| `tests/test_listing_parser.py` | New suite — 14 tests |
| `tests/regression/fixtures/listing/table_rows.html` | 3 tr.project-row cards (meta order variants, brief w/ & w/o inner link, icon signals i.fa-users + span.hsoub-file-signature-icon, entity+newline brief, trailing-slash URL) |
| `tests/regression/fixtures/listing/div_cards.html` | Tier-2 variant incl. loose anchor that must be ignored |
| `tests/regression/fixtures/listing/link_sweep.html` | Tier-3 anchors: slug-year id trap (`12345-canva-2024`→12345), dup id → first kept, id=0 skip, non-numeric `/project/` skip, blank-title skip |
| `tests/regression/fixtures/listing/mixed_and_garbage.html` | Anchorless row skipped silently; footer anchor ignored when tier-1 produced a summary; Arabic-Indic "٦١ عرض"→61; whitespace collapse; class-token decoys |
| `tests/regression/fixtures/listing/empty_body.html` | PARSE-003 fixture |
| `tests/regression/fixtures/listing/blank.html` | PARSE-001 fixture |

No scratch files created. No shared/barrel files touched (`parsers/__init__.py` already had the
needed docstring-only content; no export changes required).

### Implementation decisions (parity-critical)

- **ENTITY RULE** documented in module docstring: C# `Normalize(HtmlEntity.DeEntitize(InnerText))`
  → Python `normalize(node.text_content())`, NO extra unescape (lxml decodes at parse time;
  double-decoding would corrupt literal `&amp;amp;` payloads).
- Tier cascade order exact: tr.project-row exact-token concat trick → div.project-item same trick
  → anchor sweep raw-substring href. Tier-3 gate = ZERO SUMMARIES (not empty node set) — covered
  by `test_zero_summary_tier_falls_through_to_anchor_sweep`.
- Per-card ParseRow port: `.//h2/a` → `.//a` else silent skip; url raw; id regex FIRST
  `/project/(\d+)`; fallback rstrip('/') + split '/'/'-' first all-digit segment else 0.
- Brief uses SUBSTRING `contains(@class,'project__brief')` (NOT token trick) with inner-a-else-p;
  meta ul exact-token; DIRECT children li only.
- Classification priority preserved: empty-text skip BEFORE icon check (icon-only empty li is
  discarded — pinned by test); proposal (icon OR عرض/عروض) → time (منذ/ساعة/يوم/لحظات) → client-if-empty.
- Client-with-time-word misrouting quirk PRESERVED + asserted as expected-quirk
  ("عميل اليوم" → time bucket, number 1 via singular يوم).
- Defaults per card: budget/delivery_days/project_status None, skills_text "", is_unread True,
  EnrichmentStatus.PENDING, discovered_at fresh `datetime.now(UTC)` PER CARD (monotonic test).
- Tiers 1–2 never dedupe; tier 3 dedupes by id keeping first (ordered dict set).
- Junk-input guard: `document_fromstring` ParserError → treated as node-less DOM → falls through
  to PARSE-003, mirroring HAP's never-throwing LoadHtml.
- Every XPath kept verbatim from C# with a comment citing the C# line number.

## Verification (gates)

| Gate | Result |
|---|---|
| `uv sync` | OK |
| `uv run ruff format .` | Ran; my files conform |
| `uv run ruff check` (my 2 files) | All checks passed |
| `uv run mypy src\...\listing.py` | Success (strict) |
| `uv run xenon src -b B` | Clean (parse decomposed into `_parse_card_tiers`/`_sweep_anchor_links` after initial rank-C) |
| `uv run pytest tests\test_listing_parser.py -q --no-cov` | 14 passed |
| Coverage of `listing.py` | 95% |

### Unrelated concurrent failures (NOT fixed by this agent, per protocol)

Stable across a 60s-later rerun — all in sibling agents' files:

- `ruff check .` repo-wide: E501 `storage/schema.py`, UP017+S101 `storage/timestamps.py`,
  I001 `tests/test_http_client.py`.
- `uv run mypy src`: 5 errors in `scraping/parsers/inference.py` (2), `http/client.py` (1),
  `scraping/scraper.py` (2).
- `lint-imports`: contract `httpx-only-in-http-layer` BROKEN via `scraping/scraper.py -> mostaql.http`.
- Full `pytest`: 11 failed / 233 passed — `test_inference_engine.py` ×8,
  `test_scraper.py` ×2 (`DETAIL_URL_FORMAT.format(project_id=...)` KeyError 'id' vs `{0}`),
  `test_http_client.py` ×1 ("15s" vs "15.0s" message).

## Deviations from plan/instructions

None material. Notes:
- lxml has no type stubs in the venv; added targeted `# type: ignore[import-untyped]` on the two
  lxml import lines (pyproject untouched, per boundary).
- Fixture `blank.html` initially contained stray markup (my authoring bug, caught by the PARSE-001
  test) — corrected to whitespace-only before final green run.

## Concerns

- `scraper.py` currently formats the detail URL with named placeholder while plan §8 freezes
  `DETAIL_URL_FORMAT = ".../{0}"` — sibling owner should reconcile (their file).
- Sibling gate failures above will need resolution before the wave-level all-green checkpoint;
  none interact with ListingParser behavior.

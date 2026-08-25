# http-scraper-worker — Wave B2: HTTP Layer + Scraper

**Date:** 2026-08-25 · **Scope:** plan §8 (http/scraping signatures), §3.B (scraper row), §12 (ledger)

## Goal

Implement `mostaql.http` (`PageFetcher` + `build_default_client`) and `mostaql.scraping.scraper`
(`MostaqlScraper`), plus MockTransport-based tests, as the Python port of C#
`MostaqlScraper.GetStringAsync` / `FetchListingAsync` / `FetchProjectDetailsAsync`.

## Files created/modified (boundaries respected)

| File | Action |
|---|---|
| `.repertoire/python/src/mostaql/http/client.py` | Implemented (was empty stub) |
| `.repertoire/python/src/mostaql/http/__init__.py` | Re-exports `PageFetcher`, `build_default_client` |
| `.repertoire/python/src/mostaql/scraping/scraper.py` | Implemented (was empty stub) |
| `.repertoire/python/tests/test_http_client.py` | New — 13 tests |
| `.repertoire/python/tests/test_scraper.py` | New — 8 tests |

Not touched: `pyproject.toml`, models/text/errors, `scraping/__init__.py`, `scraping/parsers/**`.
No scratch files created.

## What was built

### client.py

- `PageFetcher(client, timeout_seconds=15.0)`; `get_html(url)` sends GET with per-request
  `httpx.Timeout(15)` (mirrors C# linked-CTS 15s) and `follow_redirects=True`
  (HttpClient auto-redirect default); returns decoded `response.text`.
- Error mapping: `httpx.TimeoutException` → `NetworkTimeoutError` (HTTP-002);
  other `httpx.TransportError` and non-2xx status → `HttpRequestError` (HTTP-001,
  mirrors `EnsureSuccessStatusCode`→RequestFailed); anything else → `HttpUnexpectedError`
  (HTTP-003). The three codes are documented in the module docstring as Python-side
  extensions of the C# taxonomy (C# built messages inline without stable codes).
- `asyncio.CancelledError` never swallowed (only `except Exception` used).
- `build_default_client()` sets UA / Accept / Accept-Language byte-identical to
  MauiProgram.cs:95-104, with a load-bearing comment (bot filter 403s header-less requests).
- Local `DomainError` construction only (errors.py untouched, per task).

### scraper.py

- Constants `LISTING_URL = "https://mostaql.com/projects"`,
  `DETAIL_URL_FORMAT = "https://mostaql.com/project/{id}"`.
- Query-param normalization exactly like C# `FetchListingAsync`: trim; prepend `?` if
  missing; None/whitespace-only → bare URL.
- `fetch_listing`: fetch → `ListingParser.parse(html)`; `ParseException` propagates AS-IS
  (typed-exception carrier; documented parity note vs C#'s `HttpErrors.ParseFailed`
  Result wrapper — plan §12 ledger item 1).
- `fetch_project_details`: fetch → `DetailParser.parse(project_id, html)` → URL stamped
  AFTER parse via `dataclasses.replace(details, url=url)` because Python models are
  frozen+slots (C# mutates `details.Url`; plan §4.1 trap 2 preserved semantically).

### Tests

- `test_http_client.py`: success body; 403/500 → HTTP-001 with exact Arabic external
  message; `ConnectTimeout`/`ReadTimeout` → HTTP-002 ("exceeded 15.0s." /
  "exceeded 2.5s."); plain `ConnectError` → HTTP-001 with cause; generic exception →
  HTTP-003; redirect followed; default-client headers asserted verbatim; caller-cancel
  task → `CancelledError` observed.
- `test_scraper.py`: constants; listing parse (ids/titles/urls); parametrized query
  normalization (`category=dev`, `?category=dev`, `"  ?x=1  "`, `""`, `"   "`, `None`);
  canonical detail URL filled post-parse while parser returns `url=""`; ParseException
  passthrough for both parsers (PARSE-003/PARSE-002, correct raw HTML/project_id handed
  to parser). Dual-mode shims (documented-API doubles injected into `sys.modules`) were
  active while Wave 7 stubs lacked the classes and auto-disable now that the REAL
  parsers landed — final run: all 21 tests pass against real `ListingParser` + `DetailParser`.

## Verification (trimmed)

| Gate | Result |
|---|---|
| `uv sync` | OK (env note: hardlink→copy warning, filesystem-related) |
| `ruff format` (my files) | 5 files already formatted |
| `ruff check` (my files) | All checks passed! |
| `mypy src/mostaql/http/client.py src/mostaql/scraping/scraper.py` | Success: no issues found in 2 source files |
| `xenon src/mostaql/http src/mostaql/scraping/scraper.py -b B` | Pass (silent) |
| `pytest tests/test_http_client.py tests/test_scraper.py -q` | **21 passed** |
| Coverage (full-suite run) | `http/client.py` **100%**, `scraping/scraper.py` **100%**; repo total 76.63% ≥ 60 |

Whole-repo gates at time of report contain ONLY parallel-wave noise:

- `mypy src`: 3 errors in `scraping/parsers/detail.py` (parser agent's brand-new file, in-flight).
- `xenon src -b B`: rank-C blocks in `parsers/detail.py` + `parsers/structural.py` (their wave).
- `pytest -q` full suite: 8 failures — contract-store ×2, inference ×5, timestamps ×1
  (storage/parser agents' in-flight TDD reds); zero failures in my files.
- `lint-imports`: see finding F1 below.

## Deviations from goal/plan (all deliberate, none silent)

1. **DETAIL_URL_FORMAT call site** — the goal's snippet is internally inconsistent as
   Python: constant `"...{id}"` cannot be formatted with positional `.format(project_id)`
   (raises `KeyError: 'id'`). Kept the goal's public constant verbatim (`{id}`) and call
   `format(id=project_id)`. Behavior identical to C# `string.Format(DetailUrlFormat, id)`.
   (Plan §8 had `{0}`; goal superseded it.)
2. **`get_html` signature** — goal's `get_html(self, url: str)` implemented; plan §8's
   optional `cancel: asyncio.Event` kwarg omitted (goal is stricter/later; caller
   cancellation flows via task cancellation, mirroring the C# rethrow branch).
3. **HTTP codes consolidated** — per goal: HTTP-001 (request failed incl. non-2xx),
   HTTP-002 (timeout), HTTP-003 (unexpected). This intentionally collapses C#'s separate
   `UnexpectedStatusCode`(002)/`NotFound`(004) codes into 001; documented in module docstring.
4. **`fix_message` omitted on HTTP-00x** — goal specified none; C# RequestFailed/Timeout
   carried Arabic fix hints. Easy later enrichment if master wants them.
5. **Post-parse URL fill via `dataclasses.replace`** — forced by frozen+slots models
   (models wave decision, outside my boundaries).

## Findings for master

- **F1 — import-linter contract contradicts plan architecture (needs pyproject change).**
  `httpx-only-in-http-layer` forbids `mostaql.scraping` from importing `httpx`, and
  import-linter forbidden contracts are TRANSITIVE: my plan-mandated
  `scraper.py → mostaql.http` (plan §6 dependency direction; §8 signature
  `fetcher: PageFetcher`) trips it via `mostaql.http.client → httpx`. Suggested fix in
  `[tool.importlinter]`:
  ```toml
  ignore_imports = ["mostaql.scraping.scraper -> mostaql.http"]
  ```
  (or exempt scraping sources). Note: pipeline waves will hit the same transitive trap
  when poller imports the scraper. I did not touch pyproject.toml per boundaries.
- **F2 — parallel-wave state:** during this task the parser agent landed `listing.py`,
  `inference.py`, then `detail.py`, and the storage agent landed `tests/contract/`;
  several of their own gates are red mid-flight (see Verification). My deliverables are
  green in isolation and integrate cleanly with the real parsers.

## Concerns

- httpx charset detection vs .NET `ReadAsStringAsync` default decoding can differ for
  non-UTF-8 responses lacking headers; mostaql serves UTF-8, so risk accepted.
- No cookies by scope (§2): authenticated-only pages (attachments) stay out of reach,
  same as C# anonymous mode.

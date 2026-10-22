# phase-a-plan-auditor — black-box Phase-A falsification audit (snapshot 32d8fd3 / dev13)

Dispatched by parent after the A3 wave; read-only audit in an isolated detached worktree (`/tmp/phase-a-audit`), harnesses offline, no network (Cloudflare precondition), no edits to any tree.

## Verdict table (all PASS)

| Req | Verdict | Key evidence |
|---|---|---|
| A0 living contract, rows flip only with proof | PASS | every REAL row traced to code/tests; PLANNED rows withheld for B1–B5 |
| A1 engines source-agnostic in Core/Parsing | PASS | 0 site literals in engines (only `MostaqlK` namespaces); JSON-LD kept module-side |
| A1 Mostaql thinned, 135 goldens behavior-identical | PASS | harness Mostaql count = 135 PASS, 0 fail |
| A2 fixtures + manifest truth | PASS | fixture-name shift documented/asserted per manifest, not file names |
| A3 descriptor owner-locked identity | PASS | `TestNafezlyDescriptorIdentity` green on every locked value |
| A3 JSON-LD → label-rows → inference precedence | PASS | `NafezlyDetailParser.cs:51-147`, shared-engine-only calls |
| A3 paced fetcher / 429 Retry-After / 403 split | PASS (nuance: pacing = pipeline limiter 8rpm/60s, not in-fetcher throttle) | `NafezlyFetcher.cs:60-78,111-120` |
| A3 gated→Ok(Failed), hard-err→Err capped retry, no queue poison | PASS | `DetailParser:188-189`; `EnrichmentWorker.cs:124-164` |
| A3 one-line DI, contribution seam first consumers | PASS | `MauiProgram.cs:117`; `TestNafezlyContributionRegistry` green; page-contribution honestly PLANNED(B2) |
| A3 no 2nd IListingStrategy/IDetailStrategy/HttpClient registration | PASS | counts 1/1/1; nafezly uses concrete strategies + factory client |
| A3 shared UI source-blind | PASS | only default-vs-nondefault branches; nafezly literals in shared dirs are doc-comments only |
| A4 FTS source-scoped composite identity | PASS | `source_id` filter + band-subtract mapper; migration suite green |
| dev13 version fields / CHANGELOG / UNITS rows | PASS | csproj internally consistent; UNITS §10 + NafezlyModule family rows honest |

Harness tails (independent): `TOTAL: 229 passed, 0 failed`; `ALL DB MIGRATION CHECKS PASSED`.

## Open items carried forward
1. **Standing precondition (NOT met by environment):** nafezly live polling must not ship enabled until one paced residential-network pass verifies listing row counts + slug-less `BuildProjectUrl` resolution (this codespace's IPs get Cloudflare JS-challenges; recorded in contract §11).
2. Cosmetic: `NafezlySelectors.ProjectIdUrlPattern` `/project/(\d+)` looser than manifest `^…/project/(\d+)-` — test-green either way; tighten opportunistically (queued for B5).
3. Pacing nuance for future tuning: rate control lives in the pipeline `TokenBucketRateLimiter`, fetcher only backs off on 429.
4. Engines intentionally have no UNITS row (UNITS scopes UI builder blocks).

No corrective Phase-A work required.

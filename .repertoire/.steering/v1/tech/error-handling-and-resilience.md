# Error handling & resilience

[← Back to wiki home](../../base/tech/README.md)

## Table of contents
- [Failure points in the pipeline](#failure-points-in-the-pipeline)
- [Listing poll failures](#listing-poll-failures)
- [Detail enrichment failures](#detail-enrichment-failures)
- [Parser failures specifically](#parser-failures-specifically)
- [Retry policy](#retry-policy)
- [What must never happen](#what-must-never-happen)

## Failure points in the pipeline

| Stage | Can fail because of |
|---|---|
| Listing poll | Network error, HTTP 429/5xx, Mostaql markup change breaking the parser |
| Detail fetch | Same as above, per-project |
| Enrichment parse | Markup change, unexpected field format (e.g. a budget string that doesn't match the expected pattern) |
| DB commit | Disk full, corrupt DB file, constraint violation (should only happen if [concurrency safeguards](concurrency-model.md) are somehow bypassed) |

## Listing poll failures

- A failed poll simply does not produce a new candidate set — it does **not** clear or corrupt the in-flight tracker or queue, which are independent of any single poll cycle's success.
- Logged, and the tray icon reflects an error state (see [ui-ux-design.md](../product/ui-ux-design.md#tray-icon) — MVP only needs a minimal error indicator, not the full polished state set).
- Next scheduled poll simply tries again — no special retry-faster logic needed, since the next poll is already only `poll_interval_seconds` away.

## Detail enrichment failures

- A single project's enrichment failing must not affect any other project in the queue — each worker's `try/finally` around one ID ([concurrency-model.md § lifecycle](concurrency-model.md#lifecycle-of-an-id-through-the-set)) already guarantees this in isolation.
- On failure, the ID is released from the in-flight tracker (via `finally`) so it becomes eligible for [retry](#retry-policy) or is correctly picked up fresh on the next poll if not retried immediately.

## Parser failures specifically

Distinct from network failures — a parse failure means the request succeeded but the HTML didn't match what the parser expected (most likely cause: Mostaql changed their markup).

- Parser code should be isolated to a single module/class per page type (listing parser, detail parser) so a markup change is a one-file fix — this was already a design goal in [overview.md](../../base/product/overview.md#core-idea) and should be enforced as an actual module boundary, not just a convention.
- A parse failure on one project's detail page should be treated the same as a fetch failure (retryable, doesn't affect other projects) — but a parse failure on the **listing page itself** is more serious (it likely means every project this cycle is unparseable) and should escalate the tray error state more visibly, since it suggests the whole pipeline is stale until fixed, not just one item.
- Consider a lightweight schema-sanity check on the listing parse (e.g. "did we get at least 1 project card with a numeric ID and a title") — if that check fails, treat it as a parser failure even if no exception was thrown, since a silently-empty parse is worse than a loud one (it looks like "no new projects" instead of "something is broken").

## Retry policy

- Exponential backoff per failing ID (or per listing poll), not a fixed retry interval — avoids hammering Mostaql harder specifically when something's already going wrong (e.g. their site returning 429s).
- A reasonable default: retry after 1 min, then 2, then 4, capping at some max (e.g. 15 min) and max attempt count (e.g. 5) before giving up on that specific ID for the current process lifetime.
- Retries still go through the shared rate limiter like any other request — a retry does not get priority or a separate budget.
- Giving up on an ID after max attempts should mark it in a way that doesn't cause it to be silently retried forever nor silently vanish — e.g. `enrichment_status = 'failed'` persisted (a genuine DB write, unlike the in-flight state) so it's visible and could be manually retried later, rather than just dropped from memory with no trace.

## What must never happen

- A duplicate `project_id` committed to the DB — prevented by [in-flight tracking](concurrency-model.md) plus the [DB-level `PRIMARY KEY` backstop](concurrency-model.md#db-level-backstop).
- An ID permanently stuck as "in flight" with no worker actually processing it — prevented by the `finally`-guaranteed release on every code path, success or failure.
- A partial project row (e.g. inserted without its skills/assets in v2+) — prevented by the [single-transaction commit boundary](concurrency-model.md#transaction-boundary).
- The pipeline silently stalling with no visible indication — every failure class above has a corresponding tray/log signal, even in MVP's minimal form.

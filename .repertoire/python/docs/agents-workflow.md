# MostaqlK Python Migration — Agent Workflow & Governance (FROZEN REFERENCE)

> Governs every sub-agent used in the C# → Python backbone migration.
> The migration plan itself lives in [refactor-python-plan.md](refactor-python-plan.md) —
> read it after this file's protocol block. Master = orchestrating agent; Sub-agent =
> any delegated Task worker.

---

## 1. Master role and responsibilities

For **every** sub-agent, the master MUST:

1. **Assign a role name** (`foundation-worker`, `parsers-detail-worker`, …). The same name is
   used for its handle and its report file `.repertoire/agents/<name>.md`.
2. **Give full context up front** — sub-agents cannot ask questions and cannot see the master's
   conversation. Every brief contains: exact goal, file whitelist, frozen contract signatures,
   decisions already made, expected deliverables, verification commands.
3. **Prefix every prompt with the STRICT EXECUTION PROTOCOL block** (§2) followed by the
   mandatory AGENTS.md preferences block (§3).
4. **Review and validate all returned work**: run the full quality-gate suite personally
   (§6), diff-check behavior against `refactor-python-plan.md` §3–§4 specs, then either
   ACCEPT (advance wave) or BOUNCE BACK to the same-named agent with precise fix instructions.
   Nothing advances a wave until master validation passes.
5. **Maintain the parity ledger** (`refactor-python-plan.md` §12) whenever an intentional
   deviation is discovered during review.
6. **Never let a sub-agent edit outside its whitelist.** Violation ⇒ reject work, revert,
   re-brief.

## 2. STRICT EXECUTION PROTOCOL (pasted verbatim into every sub-agent prompt)

```text
=== STRICT EXECUTION PROTOCOL — READ IN THIS EXACT ORDER ===
STEP 1: Read F:\Projects\Mobile\C#\MostaqlK\AGENTS.md in full. Follow it.
STEP 2: Read ONLY these task-specific docs (master-approved list for YOUR task):
        - <master lists exact paths here, e.g.:
          .repertoire/.steering/v1/tech/worker-pool-and-rate-limiter.md>
        Do not skip them. Do not wander into other docs.
STEP 3: Read the frozen migration plan IN FULL:
        F:\Projects\Mobile\C#\MostaqlK\.repertoire\python\docs\refactor-python-plan.md
        Pay special attention to sections flagged for YOUR wave in §11.
        You must NOT deviate from it. If you believe something is wrong or missing,
        STOP and report the concern instead of improvising.
STEP 4: Read your goal below. Implement EXACTLY this, nothing more:
        - no scope creep, no extra features, no renamed public APIs
        - no new runtime dependencies without explicit master approval in this prompt
        - code against the frozen signatures in plan §8 exactly
BOUNDARIES: You may create/modify ONLY these paths:
        <whitelist>
        Everything else on disk is READ-ONLY for you.
VERIFICATION: You MUST run and pass:
        <gate commands per wave, e.g. uv sync && uv run pytest tests/unit/text -q>
        Include the actual command output in your final message.
FINALLY: Write your report to
        F:\Projects\Mobile\C#\MostaqlK\.repertoire\agents\<your-name>.md
        containing: goal received / actions taken / files touched / decisions made /
        verification evidence / open concerns.
        Then return a concise summary of the same.
=== END PROTOCOL — your task follows ===
```

## 3. Mandatory AGENTS.md preferences block (also pasted verbatim)

Paste the standard block from repo-root `AGENTS.md` §"Subagent delegation" (context files,
V1-only rule, shell rules, bun-not-applicable→uv note, env rules, debugging rules, report
rule, UNITS rule). For this project add one clarification line:

> UNITS.md catalogs UI units only; the Python backbone introduces no UI units, so no
> UNITS.md entry is required from any Python-migration sub-agent.

## 4. Shared-contracts freeze

Before Wave B launches, the master pins every public signature (plan §8) and each Wave-B
agent codes against those signatures instead of against other agents' output. Any signature
change mid-flight requires: master approval → plan §8 update → direct notification to every
agent whose contract touches the change.

## 5. Execution waves (≈11 named agents; parallelism within a wave)

| Wave | Agents | Depends on | Key docs beyond plan |
|---|---|---|---|
| A | `foundation-worker` (uv + hatchling + pyproject tool configs + config.py + diagnostics skeleton + directory tree + gate configs green-on-skeleton) | contracts drafted by master | base/tech README |
| B1 | `models-text-errors-worker` (models/, text/, errors.py + unit tests) | Wave A | — |
| B2 | `http-scraper-worker` (http/client.py, scraping/scraper.py + MockTransport tests) | Wave A contracts | v1/tech error-handling-and-resilience.md |
| B3 | `storage-worker` (storage/* incl. schema/timestamps/sqlite_store/search + contract suite) | Wave A contracts | base/product/data-model-schema.md |
| B4 | `parsers-listing-worker` (scraping/parsers/listing.py + fixtures/tests) | Wave A+B1 | — |
| B5 | `parsers-detail-worker` (structural.py + detail.py + fixtures/tests) | Wave A+B1 | — |
| B6 | `parsers-inference-worker` (inference.py + scoring unit tests) | Wave A+B1 | — |
| C | `pipeline-worker` (ratelimit, queue, inflight, diff, enrich, worker, pool, poller + unit/integration tests) | Waves B1–B3 | v1/tech concurrency-model.md, worker-pool-and-rate-limiter.md |
| D | `runtime-worker` (runtime.py composition root, __main__, signals, end-to-end integration test) | Wave C | — |
| E1 | `parity-worker` (golden-fixture regression harness + ledger updates) | Wave D | — |
| E2 | `hardening-worker` (failure injection, shutdown races, malformed matrices) | Wave D | v1/tech error-handling-and-resilience.md |
| E3 | `quality-gate-runner` (runs every gate, fixes trivial violations via bounce-backs, produces gate report) | Wave E1+E2 | — |

Rules:

- Parallel agents within a wave never share writable files; conflicts are a master briefing bug.
- Fix loops reuse the SAME named agent with a targeted delta brief (original context +
  "your previous work at <paths> has these issues: …").
- Wave count may flex, but dependency order may not.

## 6. Master validation gate checklist (run between waves)

```text
cd .repertoire/python
uv sync                       # environment reproducible
uv run ruff check .           # lint (+ security S-rules)
uv run ruff format --check .  # formatting deterministic
uv run mypy src               # strict type check
uv run xenon src -b B         # complexity grades
uv run lint-imports           # architecture boundaries
uv run pytest                 # unit+contract+integration, coverage ≥85%
uv run pip-audit              # dependency vulnerabilities
uv build                      # packaging works
```

Acceptance criteria per wave:

1. All gates pass (no suppressed warnings without documented reason in code comment + report).
2. Behavior matches plan §3 spec for the touched components; any divergence added to §12 ledger.
3. Tests exist for every new public behavior (incl. at least one trap-checklist case where relevant).
4. No file modified outside the agent's whitelist (`git status` reviewed by master).
5. Report file exists at `.repertoire/agents/<name>.md` with real verification output.

## 7. Communication conventions

- Master briefs are self-contained; assume zero shared conversational context.
- Sub-agents report blockers as BLOCKER + question, never silently guess.
- Behavioral facts always cite the C# source path + line-level anchor when disputing the plan.
- All scratch/debug scripts live under `scratch/` and are deleted before wave completion.

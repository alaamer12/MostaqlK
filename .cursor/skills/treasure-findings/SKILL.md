---
name: treasure-findings
description: Orchestrates blind multi-agent orthogonal discovery where N subagents (N >= 2) explore any task, codebase, research topic, or problem domain via distinct cognitive perspectives, methodologies, and advanced prompt engineering disciplines with zero peer knowledge, followed by master synthesis into an authoritative findings report.
---

# treasure-findings — Blind Orthogonal Multi-Agent Discovery & Synthesis

Maximize analytical yield, error detection, and strategic discovery by dispatching **N isolated subagents (N &ge; 2)** engineered with **mutually orthogonal cognitive lenses, distinct prompt engineering disciplines, and independent investigative methodologies**. Subagents operate in total isolation with zero knowledge of their peers, eliminating cognitive anchoring, shared bias, and collective blind spots. Upon completion, the master orchestrator cross-examines, triangulates, and fuses all discoveries into a single authoritative master findings document.

---

### Core Architecture: Universal Orthogonal Discovery

```
                               ┌──────────────────────────────────────────────┐
                               │             MASTER ORCHESTRATOR              │
                               │  [Phase 0: Clarify Ambiguities with User]   │
                               │  - Formulates N orthogonal cognitive lenses  │
                               │  - Applies advanced prompt disciplines       │
                               │  - Dispatches blind subagents in parallel    │
                               └──────────────────────┬───────────────────────┘
                                                      │
                       ┌──────────────────────────────┼──────────────────────────────┐
                       │ (Lens A: Empirical/Automated)│ (Lens B: Formal/Structural)  │ (Lens C: Adversarial/Chaos)
                       ▼                              ▼                              ▼
          ┌─────────────────────────┐    ┌─────────────────────────┐    ┌─────────────────────────┐
          │      SCOUT AGENT A      │    │      SCOUT AGENT B      │    │      SCOUT AGENT C      │
          │  - Mental Model: Scripter│   │  - Mental Model: Auditor│   │  - Mental Model: Breaker│
          │  - Zero knowledge of B/C │   │  - Zero knowledge of A/C│   │  - Zero knowledge of A/B│
          │  - Own dedicated report  │   │  - Own dedicated report │   │  - Own dedicated report │
          └────────────┬────────────┘    └────────────┬────────────┘    └────────────┬────────────┘
                       │                              │                              │
                       │ Writes raw findings          │ Writes raw findings          │ Writes raw findings
                       ▼                              ▼                              ▼
          ┌─────────────────────────┐    ┌─────────────────────────┐    ┌─────────────────────────┐
          │  .repertoire/agents/    │    │  .repertoire/agents/    │    │  .repertoire/agents/    │
          │   scout-lens-a.md       │    │   scout-lens-b.md       │    │   scout-lens-c.md       │
          └────────────┬────────────┘    └────────────┬────────────┘    └────────────┬────────────┘
                       │                              │                              │
                       └──────────────────────────────┼──────────────────────────────┘
                                                      │
                                                      ▼
                               ┌──────────────────────────────────────────────┐
                               │          MASTER SYNTHESIS & FUSION           │
                               │  - Ingests all independent reports           │
                               │  - Triangulates consensus vs solitary value  │
                               │  - Reconciles anomalies & false positives    │
                               │  - Generates unified authoritative document  │
                               │  - Optional lifecycle cleanup of scouts      │
                               └──────────────────────┬───────────────────────┘
                                                      │
                                                      ▼
                               ┌──────────────────────────────────────────────┐
                               │   .repertoire/findings/<topic>-findings.md   │
                               │       (Concrete Authoritative Master)        │
                               └──────────────────────────────────────────────┘
```

---

### Advanced Prompt Engineering Disciplines Applied in `treasure-findings`

To ensure maximal cognitive diversity and prevent subagents from converging on identical reasoning pathways, the master orchestrator employs four foundational prompt engineering disciplines:

#### 1. Epistemic Persona & Mental Model Inoculation
Rather than generic instructions, each subagent is inoculated with a distinct cognitive identity that dictates its epistemic filter, evaluation criteria, and investigative bias:
- **The Empirical Mechanist (Deterministic / High-Recall)**: Relies on programmatic tooling, AST traversal, regex mining, metrics, and quantitative evidence. Focuses on exhaustive syntactic coverage.
- **The Formal Systems Architect (Invariant / Deductive)**: Traces contracts, state machines, data flow boundaries, and architectural hierarchies. Focuses on structural coherence and boundary leaks.
- **The Adversarial Red-Teamer (Pessimistic / Edge-Case Hunter)**: Seeks failure modes, race conditions, malicious inputs, untrusted boundary crossings, and hidden regression risks.
- **The Teleological Pragmatist (Product / UX / Efficiency)**: Focuses on user outcomes, operational friction, dead weight, resource waste, and cross-platform fidelity.

#### 2. Cognitive Framing & Constraint Inversion
Each prompt enforces contrasting optimization objectives:
- Agent A is instructed to optimize for **Recall** (flag every anomaly, accept higher candidate counts for verification).
- Agent B is instructed to optimize for **Precision** (only report provable invariant breaches with complete failure proofs).
- Agent C is instructed to search for **Counter-Factual Absences** (what is missing, unhandled, or assumed without verification).

#### 3. Radical Anti-Anchoring (Zero Knowledge)
- Subagents are given **zero visibility** into other active scouts or potential pre-conceived hypotheses.
- Each scout believes it is the sole investigator tasked with uncovering the truth through its dedicated lens.

#### 4. Structured Output Contract & Evidence Grading
Every subagent is bound to a strict deliverable format requiring concrete proof: file/line locations, reproduced steps, input-output proofs, or verified citations.

---

### Universal Scope: Domain Archetypes

`treasure-findings` operates across any analytical domain:

| Domain | Scout 1 (Empirical / Mechanist) | Scout 2 (Formal / Structural) | Scout 3 (Adversarial / Chaos) |
|---|---|---|---|
| **Codebase & Layer Audit** | AST / regex parsing, pattern matching scripts, candidate scans. | Manual layer-boundary tracing, domain invariants, MVVM purity. | Concurrency hazards, silent fallbacks, broken error boundaries. |
| **Open Web & Research** | Exhaustive data aggregation, quantitative stats, technical docs. | Synthesis of core paradigms, comparative taxonomies, tradeoffs. | Critical counter-arguments, failure reports, edge criticisms. |
| **Algorithmic & Logic Design** | Brute-force & benchmark scripting, time/space complexity analysis. | Formal correctness proofs, inductive proofs, invariant checks. | Degenerate inputs, worst-case scaling, memory leak vectors. |
| **System & Architecture Design** | Concrete component mapping, data schemas, API payload models. | Decoupling, authority matrices, clean dependency graphs. | Single points of failure, bottleneck simulations, network splits. |

---

### Command Syntax & Shorthand Grammar

```
treasure-findings                              → 2 default orthogonal scouts (Empirical Scanner + Formal Architect)
treasure-findings N                            → N orthogonal scouts (N >= 2)
treasure-findings 3 {task}                     → 3 scouts exploring {task} with distinct prompt lenses
treasure-findings {task} --lenses "A, B, C"    → Custom cognitive lens definitions
treasure-findings {task} --cleanup             → Purge intermediate scout artifacts after master synthesis
treasure-findings {task} --keep-artifacts      → Retain individual scout reports for auditability (default)
```

---

### End-to-End Orchestration Workflow

#### Phase 0: Upfront Clarity & User Engagement (Disambiguation Gate)
*Before dispatching subagents, the master orchestrator must evaluate task clarity:*
1. **Detect Ambiguity / Vague Requirements**: If the scope, target boundaries, architectural assumptions, or expected criteria are unclear or underspecified, the master agent **must pause and ask the user targeted clarifying questions** first.
2. **Clarification Checkpoints**:
   - *Target Scope & Boundaries*: Which layers, subsystems, files, or platforms are in/out of scope?
   - *Depth & Constraints*: Is static structural auditing preferred, or dynamic runtime/stress fuzzing?
   - *Hypotheses & Suspicions*: Are there specific bug classes or historical hotspots the user suspects?
   - *Artifact Preferences*: Should intermediate scout reports be retained (`--keep-artifacts`) or cleaned up (`--cleanup`)?
3. **Proceed with Concrete Scope**: Only once the objective is crisp and unambiguous does the master orchestrator proceed to lens formulation and dispatch.

#### Phase 1: Cognitive Lens & Prompt Formulation
1. Define the task and determine optimal scout count `N` (&ge; 2).
2. Select orthogonal cognitive lenses (e.g., Empirical Tooler vs. Formal Auditor vs. Adversarial Breaker).
3. Draft dedicated prompts applying the **Subagent Multi-Perspective Prompt Template**.
4. Assign distinct output paths: `.repertoire/agents/<scout-name>.md`.

#### Phase 2: Parallel Blind Dispatch
1. Dispatch all `N` subagents simultaneously via parallel subagent tools.
2. Ensure subagents run independently without communication or shared state.

#### Phase 3: Triangulation & Anomaly Reconciliation
Upon completion of all scouts:
1. Read all independent reports from `.repertoire/agents/`.
2. Construct the **Triangulation Matrix**:
   - **Consensus Discoveries**: Corroborated by multiple independent methodologies (Highest confidence).
   - **Specialized Treasures**: Uncovered exclusively by one scout due to its unique cognitive lens.
   - **Anomalies / Direct Conflicts**: Disagreements between scouts resolved via master tie-breaker inspection.
   - **False Positives**: Candidate hypotheses evaluated and dismissed.

#### Phase 4: Master Authoritative Synthesis
Write the concrete master findings report to `.repertoire/findings/<topic>-findings.md` covering:
- **Executive Summary & Risk Assessment**
- **Methodology Matrix & Cognitive Lenses Deployed**
- **Ranked Findings by Severity & Utility**
- **Triangulation Table (Corroboration vs Specialized Discovery)**
- **Actionable Execution Roadmap**

#### Phase 5: Lifecycle Cleanup
- Purge scratch scripts and scout markdown reports if `--cleanup` is enabled.
- Retain reports if `--keep-artifacts` is set.

---

### Multi-Perspective Prompt Engineering Template

```markdown
=== MANDATORY AGENT PREFERENCES (read and follow) ===
1. CONTEXT:
   - Target System / Subject: {TASK_SUBJECT}
   - Reference Documents: {RELEVANT_DOCS}

2. EPISTEMIC IDENTITY & COGNITIVE LENS:
   - ROLE: {COGNITIVE_PERSONA} (e.g., Adversarial Red-Team Auditor / Empirical Tooler / Invariant Verifier)
   - PRIMARY OBJECTIVE: {OPTIMIZATION_GOAL} (e.g., Find unhandled boundary failures / Prove structural violations / Quantify performance friction)
   - MENTAL MODEL: {MENTAL_MODEL_INSTRUCTIONS}
   - INVESTIGATIVE CONSTRAINT: Rely strictly on your designated cognitive approach. Avoid adopting secondary perspectives.

3. EXECUTION DISCIPLINE:
   - Step 1: Conduct deep exploration using your designated methodology ({e.g., custom Python script / rigorous manual trace / boundary fuzzing}).
   - Step 2: Directly inspect every candidate anomaly to eliminate false positives and establish verifiable proof.
   - Step 3: Record exact evidence (file paths, lines, payloads, execution traces, or source citations).

4. DELIVERABLE ARTIFACT:
   - Output File: `.repertoire/agents/{SCOUT_AGENT_NAME}.md`
   - Required Structure:
     * Executive Analysis from Assigned Perspective
     * Itemized Findings (Severity, Proof/Evidence, Failure Mechanism)
     * Dismissed Candidates (False positives verified and rejected)
     * Strategic Recommendations

=== END PREFERENCES ===

INVESTIGATION TASK:
{DETAILED_TASK_DESCRIPTION}
```

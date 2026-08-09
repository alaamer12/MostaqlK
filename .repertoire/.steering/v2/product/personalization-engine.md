# Personalization Engine — Feature Specification

> **Status:** Planned — v2 implementation
> **Interaction tracking:** Begins in v1 (collect data early, before the engine exists)
> **Algorithm version:** v1 = weighted feature scoring (pure .NET, no ML library)
> **Algorithm v2+:** ML model (logistic regression or similar via ML.NET) — documented in a future ADR

---

## Table of Contents

1. [Overview](#1-overview)
2. [Interaction Signals](#2-interaction-signals)
3. [Feature Space](#3-feature-space)
4. [Scoring Algorithm](#4-scoring-algorithm)
5. [Schema Additions](#5-schema-additions)
6. [Service Design (C# Interfaces)](#6-service-design-c-interfaces)
7. [Background Recompute Strategy](#7-background-recompute-strategy)
8. [UI Integration](#8-ui-integration)
9. [Cold Start Handling](#9-cold-start-handling)
10. [Privacy Notes](#10-privacy-notes)
11. [Phase Delivery Plan](#11-phase-delivery-plan)
12. [Future: ML Upgrade Path](#12-future-ml-upgrade-path)

---

## 1. Overview

The personalization engine observes how the user interacts with projects over time and uses those signals — along with all available project metadata — to compute a **relevance score** for every project. This score drives two UI surfaces:

1. **Recommendation side panel** — a ranked list of top-N projects the user is most likely to be interested in, opened on demand from the main window.
2. **Feed score badges** — subtle relevance indicators on each project card in the main feed, giving the user a at-a-glance sense of which new arrivals are worth reading first.

### Philosophy

- **Local-first, privacy-first.** All interaction data and all computation stay on the user's machine. Nothing is sent to any server.
- **Transparent.** Every recommendation shows an explanation ("Recommended because: matching skills, similar budget range"). The user is never shown a black box score.
- **Non-intrusive.** The engine works in the background. The user never has to configure it or wait for it.
- **Graceful degradation.** With no interaction history, the engine is silent. Recommendations appear only once enough signals have been collected.

### What the engine is NOT

- It is not a collaborative filter — there is only one user, no shared user pool to derive patterns from.
- It is not a content recommender that reaches out to an API. All inference is done locally over the local SQLite database.
- It does not require any explicit user ratings or preferences. Everything is derived from implicit behavioral signals.

---

## 2. Interaction Signals

Three interaction types are tracked. Each contributes differently to the preference profile.

### 2.1 — `opened` (strong positive signal)

**Trigger:** The user clicks a project card to open the detail page.
**Meaning:** Active intent. The user chose this project out of all visible projects. This is the strongest available positive signal.
**Base weight:** `1.0`

When the user eventually closes/leaves the detail page, the time they spent is recorded as a duration modifier (see §2.2).

### 2.2 — `time_spent` (weight modifier on `opened`)

**Trigger:** Recorded when the user navigates away from a project detail page.
**Meaning:** Scales the `opened` signal up or down based on engagement depth.
**Duration modifier function:**

```
duration_modifier = clamp(log₂(duration_seconds + 1) / log₂(60), 0.5, 2.0)
```

| Time spent | Modifier | Interpretation |
|---|---|---|
| < 5 seconds | ~0.5 | Bounced — project was not what was expected |
| 15 seconds | ~0.8 | Brief glance |
| 60 seconds | 1.0 | Baseline — read the description |
| 120 seconds | ~1.2 | Engaged reading |
| 300+ seconds | ~2.0 (capped) | Deep engagement |

So the effective weight of an `opened` interaction = `1.0 × duration_modifier`.

### 2.3 — `scrolled_past` (weak negative signal)

**Trigger:** A project card was visible in the feed for ≥ 3 seconds without the user opening it.
**Meaning:** The user saw this project and chose not to act on it. Weak signal — the user may have been busy, or it may be irrelevant.
**Base weight:** `-0.1`

**Implementation note:** The `CollectionView` item appearance time is tracked in the ViewModel. The 3-second threshold filters out cards that were briefly in the viewport during fast scrolling.

### Signal Decay

Older interactions contribute less than recent ones. Exponential decay is applied when building the preference profile:

```
effective_weight = base_weight × e^(−λ × days_since_interaction)
```

Where `λ = 0.05` (decay constant). This gives the following half-life:

| Days ago | Remaining weight |
|---|---|
| 0 (today) | 100% |
| 7 days | ~71% |
| 14 days | ~50% |
| 30 days | ~22% |
| 60 days | ~5% |

This ensures the engine adapts to changing preferences over time rather than being permanently anchored to the first project the user ever opened.

---

## 3. Feature Space

The preference profile is computed across six independent feature dimensions. For each dimension, the engine learns a "preferred value range" or "affinity distribution" from the interaction history, then scores new projects against that learned preference.

### Feature 1 — Skill Affinity (weight: 40%)

**What it measures:** Which skills appear in projects the user opens vs. scrolls past.

**How it's learned:**
For each skill, compute an affinity score:
```
affinity(skill) = Σ effective_weight_i   for all interactions where project_i has this skill
```
Normalize so the top skill has affinity = 1.0. Skills with affinity < 0 are "negative" skills (the user consistently ignores projects with this skill).

**How a project is scored:**
```
skill_score = mean(affinity(skill_j))   for all skills in this project
```
If the project has no skills in common with the user's history, `skill_score = 0`.

**Why 40%?** Skills are the primary signal for professional relevance. A developer who consistently opens React projects but ignores Android projects has a clear, stable preference that deserves the highest weight.

---

### Feature 2 — Budget Range Proximity (weight: 20%)

**What it measures:** The user's preferred budget range, derived from the budget range of projects they've opened.

**How it's learned:**
Compute the weighted mean and standard deviation of `budget_max` values across opened projects:
```
preferred_budget_center = weighted_mean(budget_max, effective_weights)
preferred_budget_spread = weighted_stddev(budget_max, effective_weights)
```

**How a project is scored:**
Use a Gaussian (bell-curve) proximity score:
```
budget_score = e^(−0.5 × ((budget_max − preferred_center) / preferred_spread)²)
```

This gives:
- Projects matching the preferred range: score ≈ 1.0
- Projects slightly outside: score ≈ 0.6
- Projects far outside: score ≈ 0.0

Projects with NULL budget (unspecified) receive a neutral score of `0.5`.

---

### Feature 3 — Category Affinity (weight: 20%)

**What it measures:** Which project categories the user tends to open.

**How it's learned:**
For each category, compute a weighted interaction count (same formula as skill affinity but per category).

**How a project is scored:**
```
category_score = affinity(project.category) / max_affinity
```
Projects in a category the user has never seen → `category_score = 0.3` (mild positive to avoid penalizing new categories the user hasn't had a chance to explore yet).

---

### Feature 4 — Freshness Preference (weight: 10%)

**What it measures:** Does the user tend to open recently posted projects or older ones?

**How it's learned:**
Compute the average age (in hours) of projects the user opens:
```
preferred_age_hours = weighted_mean(age_at_open_hours, effective_weights)
```

**How a project is scored:**
```
freshness_score = e^(−age_hours / preferred_age_hours)
```

Most users on a real-time feed prefer fresh projects. The default preferred age (cold-start value) is 24 hours.

---

### Feature 5 — Proposal Count Preference (weight: 5%)

**What it measures:** Does the user prefer less competitive projects (fewer proposals) or more established ones (many proposals indicating real client engagement)?

**How it's learned:**
Weighted mean and spread of `proposal_count` on opened projects.

**How a project is scored:**
Gaussian proximity, same approach as budget scoring.

Projects with 0 proposals receive a neutral score (they are genuinely new and may be highly relevant despite having no history).

---

### Feature 6 — Delivery Days Preference (weight: 5%)

**What it measures:** Does the user prefer short-term or long-term projects?

**How it's learned:**
Weighted mean and spread of `delivery_days` on opened projects.

**How a project is scored:**
Gaussian proximity.

Projects with NULL `delivery_days` receive neutral score = `0.5`.

---

## 4. Scoring Algorithm

### 4.1 — Preference Profile

The preference profile is **not stored as a separate table**. It is computed on-the-fly from `user_interactions` joined with project feature tables each time a recompute is triggered. This ensures the profile is always consistent with the latest interaction data and decay weights — no cache invalidation needed.

### 4.2 — Project Score Computation

For each candidate project `p`:

```
score(p) =   0.40 × skill_score(p)
           + 0.20 × budget_score(p)
           + 0.20 × category_score(p)
           + 0.10 × freshness_score(p)
           + 0.05 × proposal_score(p)
           + 0.05 × delivery_score(p)
```

Score range: `[0.0, 1.0]`. A score of `1.0` means perfect match on every dimension. A score of `0.0` means no overlap on any dimension.

### 4.3 — Score Tiers (for UI display)

| Score | Tier | Badge color | Label |
|---|---|---|---|
| ≥ 0.75 | High | Green | "موصى به بشدة" |
| 0.50–0.74 | Medium | Amber | "قد يهمك" |
| 0.25–0.49 | Low | Slate/grey | (no label, just small indicator) |
| < 0.25 | Below threshold | None | (no badge) |

### 4.4 — Score Explanation (Transparency)

For each project in the recommendation panel, the top two contributing features are identified and surfaced as an explanation string:

```
"بناءً على: مهارات متوافقة (0.92) · نطاق ميزانية مناسب (0.81)"
```

The feature scores are stored individually in `project_scores` (§5) and read by the UI to generate this string.

---

## 5. Schema Additions

### 5.1 — `user_interactions`

Raw interaction event log. The source of truth for all preference learning. Never modified after insert — append-only.

```sql
CREATE TABLE IF NOT EXISTS user_interactions (
    interaction_id   INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id       INTEGER NOT NULL REFERENCES projects(project_id),
    interaction_type TEXT    NOT NULL
                         CHECK (interaction_type IN ('opened', 'scrolled_past')),
    duration_seconds INTEGER,           -- NULL for 'scrolled_past'
                                        -- seconds on detail page for 'opened'
    interacted_at    TEXT    NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_interactions_project    ON user_interactions (project_id);
CREATE INDEX IF NOT EXISTS idx_interactions_type_time  ON user_interactions (interaction_type, interacted_at DESC);
```

#### Column Reference

| Column | Type | Nullable | NULL means |
|---|---|---|---|
| `interaction_id` | INTEGER | No | Surrogate PK. |
| `project_id` | INTEGER | No | FK → projects. Which project was interacted with. |
| `interaction_type` | TEXT | No | `'opened'` or `'scrolled_past'`. |
| `duration_seconds` | INTEGER | Yes | NULL for `'scrolled_past'`. For `'opened'`: seconds spent on the detail page before navigating away. |
| `interacted_at` | TEXT | No | ISO8601 UTC when the event occurred. |

#### Normalization

`interaction_id → {project_id, interaction_type, duration_seconds, interacted_at}`. Single-column PK. All columns depend solely on `interaction_id`. **3NF / BCNF.** Append-only — no updates ever run on this table.

---

### 5.2 — `project_scores`

Precomputed relevance scores per project. Written by the background `RecommendationEngine` service. Read by the UI for the side panel and feed badges.

```sql
CREATE TABLE IF NOT EXISTS project_scores (
    project_id      INTEGER PRIMARY KEY REFERENCES projects(project_id),
    relevance_score REAL    NOT NULL DEFAULT 0.0,   -- composite [0.0, 1.0]
    skill_score     REAL    NOT NULL DEFAULT 0.0,
    budget_score    REAL    NOT NULL DEFAULT 0.0,
    category_score  REAL    NOT NULL DEFAULT 0.0,
    freshness_score REAL    NOT NULL DEFAULT 0.0,
    proposal_score  REAL    NOT NULL DEFAULT 0.0,
    delivery_score  REAL    NOT NULL DEFAULT 0.0,
    score_version   INTEGER NOT NULL DEFAULT 0,     -- increments on each recompute
    scored_at       TEXT    NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_scores_relevance ON project_scores (relevance_score DESC);
```

#### Column Reference

| Column | Type | Nullable | Description |
|---|---|---|---|
| `project_id` | INTEGER | No | PK + FK → projects. |
| `relevance_score` | REAL | No | Composite weighted score [0.0–1.0]. The primary sort key for the recommendation panel. |
| `skill_score` | REAL | No | Skills dimension score [0.0–1.0]. Used for explanation display. |
| `budget_score` | REAL | No | Budget proximity score [0.0–1.0]. |
| `category_score` | REAL | No | Category affinity score [0.0–1.0]. |
| `freshness_score` | REAL | No | Freshness preference score [0.0–1.0]. |
| `proposal_score` | REAL | No | Proposal count proximity score [0.0–1.0]. |
| `delivery_score` | REAL | No | Delivery days proximity score [0.0–1.0]. |
| `score_version` | INTEGER | No | Monotonically increasing version number. UI can detect when scores were last updated. |
| `scored_at` | TEXT | No | ISO8601 UTC when this row was last written. |

#### Normalization

Single-column PK. All feature scores depend solely on `project_id`. Storing them as individual columns (not as a JSON blob or a key-value child table) keeps the table in **3NF / BCNF** and allows indexed ordering on `relevance_score`.

The individual feature score columns are a deliberate denormalization of the computed result — they exist for UI explanation, not for joining. This is acceptable as a presentation-layer optimization: the source of truth is the `user_interactions` log; the `project_scores` table is a derived, rebuildable cache.

---

### 5.3 — Interaction Retention Policy

Old interactions are periodically pruned to prevent unbounded table growth. The `RecommendationEngine` runs a cleanup step during each recompute cycle:

```sql
DELETE FROM user_interactions
WHERE interacted_at < @cutoffDate;   -- default: 90 days ago
```

The 90-day retention is a setting: `settings.interaction_retention_days`.

---

## 6. Service Design (C# Interfaces)

### `IInteractionTracker`

Responsible for recording interaction events. Called by the UI layer. Lightweight — writes to `user_interactions` and triggers an async score update.

```csharp
public interface IInteractionTracker
{
    /// Called when the user opens a project detail page.
    Task RecordOpenedAsync(long projectId, CancellationToken ct = default);

    /// Called when the user leaves a project detail page.
    /// durationSeconds = how long they stayed on the page.
    Task RecordTimeSpentAsync(long projectId, int durationSeconds, CancellationToken ct = default);

    /// Called when a project card was visible in the feed for >= 3 seconds
    /// without being opened.
    Task RecordScrolledPastAsync(long projectId, CancellationToken ct = default);
}
```

### `IRecommendationEngine`

Responsible for computing and persisting relevance scores.

```csharp
public interface IRecommendationEngine
{
    /// True when enough interactions exist to generate meaningful recommendations.
    /// (minimum interaction threshold: 5 opened interactions)
    bool IsWarmedUp { get; }

    /// Triggered automatically by the background strategy (§7).
    /// Recomputes scores for all projects or a subset of changed projects.
    Task RecomputeAsync(CancellationToken ct = default);

    /// Returns the top N recommended projects from the precomputed scores.
    /// Only called by the UI for the side panel.
    Task<IReadOnlyList<ScoredProject>> GetTopAsync(int count, CancellationToken ct = default);
}
```

### `ScoredProject` (Record)

The DTO returned by `GetTopAsync`. Carries the project data + scores needed to render the side panel card and the explanation string.

```csharp
public sealed record ScoredProject(
    long    ProjectId,
    string  Title,
    string  OwnerName,
    string? CategoryName,
    decimal? BudgetMin,
    decimal? BudgetMax,
    int     ProposalCount,
    string  PostedAt,
    double  RelevanceScore,
    double  SkillScore,
    double  BudgetScore,
    double  CategoryScore,
    double  FreshnessScore,
    string  ExplanationText    // e.g. "بناءً على: مهارات متوافقة · نطاق ميزانية مناسب"
);
```

### `PreferenceProfile` (Internal)

Computed on-the-fly from `user_interactions` inside `RecommendationEngine`. Not persisted. Represents the learned user preferences used to score projects.

```csharp
internal sealed record PreferenceProfile(
    IReadOnlyDictionary<long, double> SkillAffinities,   // skill_id → affinity score
    IReadOnlyDictionary<long, double> CategoryAffinities, // category_id → affinity score
    double  PreferredBudgetCenter,
    double  PreferredBudgetSpread,
    double  PreferredAgeHours,
    double  PreferredProposalCenter,
    double  PreferredProposalSpread,
    double  PreferredDeliveryCenter,
    double  PreferredDeliverySpread,
    int     TotalInteractions,
    bool    IsWarmedUp             // true when total 'opened' interactions >= 5
);
```

---

## 7. Background Recompute Strategy

The engine runs **continuously in the background** — scores update automatically. Three triggers fire a recompute:

| Trigger | Recompute scope | Reason |
|---|---|---|
| New projects committed by pipeline | Only the new projects (incremental update) | Most frequent trigger; only new arrivals need scoring |
| User records an `opened` interaction | All projects (full recompute) | A new click shifts the preference profile; all scores may change |
| App startup (if score_version is stale) | All projects (full recompute) | Ensures scores reflect latest interaction data after any app restart |

**Incremental vs. full recompute:**

- **Incremental** (new projects only): Fetch the current `PreferenceProfile`, score only the new `project_id`s, upsert into `project_scores`. Fast — O(new projects).
- **Full recompute** (preference changed): Rebuild `PreferenceProfile` from all `user_interactions`, re-score all projects. Slower but acceptable on background thread — O(all projects × features).

At 100k projects, a full recompute with 6 features takes < 1 second on a modern CPU in pure .NET arithmetic. No performance concern.

**Rate-limiting recomputes:**

If multiple triggers fire within a short window (e.g. the user opens 3 projects in quick succession), debounce the full recompute: wait 500ms after the last trigger before executing. Use a `CancellationToken` to cancel and restart the debounce window.

**Pipeline integration:**

`PollOrchestrator` calls `IRecommendationEngine.RecomputeAsync` incrementally after each successful `EnrichAndCommitAsync` call, passing the new `project_id` list. This happens **after** `InFlightTracker.MarkComplete` — not inside the enrichment transaction.

---

## 8. UI Integration

### 8.1 — Recommendation Side Panel

**Trigger:** A button in the main window sidebar — an icon (e.g. a star or sparkle icon from Tabler Icons) labeled "توصيات".

**Behavior:**
- Button shows a badge count (number of high-relevance new projects since last panel open).
- Clicking opens a slide-out panel (or a secondary column in the existing layout) positioned to the right of the main feed in the RTL layout.
- Panel shows top 10 `ScoredProject` entries ordered by `relevance_score DESC`.
- If `IsWarmedUp = false`: shows a "مزيد من التصفح لتفعيل التوصيات" placeholder card instead.

**Panel card layout (matches project card style):**
- Project title
- Owner name + category
- Budget range (or "غير محدد")
- Relevance score bar (horizontal bar, filled proportionally, color-coded by tier)
- Explanation text: "بناءً على: مهارات متوافقة · نطاق ميزانية مناسب"
- "فتح" button → navigates to `ProjectDetailPage`

### 8.2 — Feed Score Badges

A small indicator on each project card in the main `CollectionView`. Only visible when `IsWarmedUp = true`.

**Badge design:**
- A small colored dot (4px) on the card's top-inline-start corner (inside the unread accent bar column).
- Color follows the tier system (§4.3).
- No text — keeps the card clean. Tooltip/hover shows `"نقاط الصلة: 0.82"`.
- Projects with score below threshold (< 0.25) show no badge.

**Feed sort option:**
The query builder sort control gains a new option: `"الأكثر صلة بك"` (relevance). When selected, the feed is sorted by `project_scores.relevance_score DESC` (joined into the main feed query).

```sql
-- Extended main feed query with relevance sort
SELECT p.project_id, p.title, p.posted_at, ...,
       COALESCE(ps.relevance_score, 0.0) AS relevance_score
FROM   projects p
JOIN   owners o ON p.owner_id = o.owner_id
LEFT JOIN categories      c  ON p.category_id = c.category_id
LEFT JOIN project_details pd ON p.project_id  = pd.project_id
LEFT JOIN project_scores  ps ON p.project_id  = ps.project_id
ORDER  BY ps.relevance_score DESC NULLS LAST, p.posted_at DESC
LIMIT  @pageSize OFFSET @offset;
```

### 8.3 — Viewport Tracking (for `scrolled_past`)

MAUI's `CollectionView` does not natively expose item visibility duration. The implementation:

1. Attach to `CollectionView.Scrolled` event.
2. Track which `project_id` items enter/exit the viewport using item index ranges.
3. Stamp the entry time for each item. When an item exits the viewport, if `exit_time - entry_time >= 3 seconds` and the item was never opened → call `IInteractionTracker.RecordScrolledPastAsync(projectId)`.
4. This logic lives in `ProjectsViewModel` (not in the View), where it's testable.

---

## 9. Cold Start Handling

**Definition:** Cold start = fewer than 5 `opened` interactions in `user_interactions`.

During cold start:
- `IRecommendationEngine.IsWarmedUp = false`
- No score badges are shown on feed cards
- The recommendation panel shows a friendly placeholder instead of cards:

```
نصيحة: افتح بعض المشاريع التي تهمك
وسنقترح عليك مشاريع مشابهة تلقائياً.
(لديك X/5 نقاط تفاعل)
```

**Why 5 as the threshold?**
With fewer than 5 signals, the computed skill affinities and budget preferences are too noisy to be meaningful. A user might open one project by curiosity — that's not enough to characterize a preference. Five opens across varied projects give the engine enough signal to find patterns.

---

## 10. Privacy Notes

- All interaction data is stored **only in the local SQLite database** on the user's machine.
- No data is transmitted off-device at any point — no telemetry, no analytics, no cloud sync of interaction history.
- The `user_interactions` table is subject to the 90-day retention policy (configurable via `settings.interaction_retention_days`). Older data is pruned automatically.
- The user can reset all personalization data via a "مسح بيانات التوصيات" option in Settings, which runs:

```sql
DELETE FROM user_interactions;
DELETE FROM project_scores;
```

After which `IsWarmedUp = false` and the engine starts fresh.

---

## 11. Phase Delivery Plan

| Phase | What ships | Reason |
|---|---|---|
| **v1 (MVP)** | `user_interactions` table + `IInteractionTracker` recording only | Start collecting data from day one. The longer we wait, the longer until the engine is useful. No scoring or UI yet. |
| **v2** | `project_scores` table + `IRecommendationEngine` (full scoring engine) + recommendation side panel + feed badges + relevance sort option | The engine needs data to be useful; v1 interaction tracking ensures v2 ships with real data. |
| **v3+** | ML model upgrade (ML.NET logistic regression or gradient boosted trees trained on the interaction log) — documented in a future ADR when v2 data volume makes it meaningful | |

---

## 12. Future: ML Upgrade Path

The weighted scoring engine (v1) is designed to be **swapped out** without changing:
- The `IRecommendationEngine` interface
- The `IInteractionTracker` interface  
- The `user_interactions` table (it becomes the training set)
- The `project_scores` table (ML output is still written here)
- Any UI surface (side panel and feed badges read the same table)

A future ML-backed implementation of `IRecommendationEngine` would:
1. Load all `user_interactions` with their decay-weighted effective weights.
2. Build feature vectors for each project (the same 6 feature dimensions as §3).
3. Train a binary classifier (opened = positive class, scrolled_past = negative class) using ML.NET's `FastTree` or `LightGbm` trainer.
4. Predict the click probability for each uninteracted project.
5. Write predictions to `project_scores.relevance_score`.

The `IRecommendationEngine` abstraction means this upgrade can be done and A/B tested without touching any other code.

---

## Appendix: Feature Weight Summary

| Feature | Weight | Rationale |
|---|---|---|
| Skill affinity | **40%** | Most stable and reliable professional signal |
| Budget range proximity | **20%** | Clear personal preference; freelancers have well-defined rate expectations |
| Category affinity | **20%** | Second most stable signal after skills |
| Freshness preference | **10%** | Most users prefer recent projects; weight is lower because it's the least personalizable |
| Proposal count preference | **5%** | Secondary competitive preference |
| Delivery days preference | **5%** | Secondary project duration preference |

Weights are **tuneable constants** defined in `RecommendationEngine` as `const double` values. Note that the weights listed above are **initial baseline values** for the v1 implementation; they may be updated and tuned during implementation based on test cases and real-world signal analysis to find the most accurate balance. A future settings panel can expose them for power users.

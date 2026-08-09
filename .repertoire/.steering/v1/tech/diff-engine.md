# Diff engine

[← Back to wiki home](../../base/tech/README.md)

## Table of contents
- [Why a dedicated engine, not inline comparison logic](#why-a-dedicated-engine-not-inline-comparison-logic)
- [Core abstraction](#core-abstraction)
- [Local mode (v1)](#local-mode-v1)
- [Peer-sync mode (v3, future)](#peer-sync-mode-v3-future)
- [Interface sketch (C#)](#interface-sketch-c)
- [What the diff engine explicitly does not do](#what-the-diff-engine-explicitly-does-not-do)

## Why a dedicated engine, not inline comparison logic

The naive version — "compare scraped IDs against the DB" — is wrong on its own, as established in [architecture-pipeline.md § in-flight tracking](../../base/product/architecture-pipeline.md#in-flight-tracking): a project mid-enrichment isn't in the DB yet, so a plain DB diff re-discovers it and causes duplicate work.

The correct comparison needs **three states**, not two: `unseen` / `in_flight` / `committed`. That three-state comparison is the same shape of problem as the future mobile [peer-sync reconciliation](../../v2/product/roadmap-future.md#two-way-peer-sync) — "what does peer A have that peer B doesn't, accounting for what's already being handled." Rather than writing this logic once inline for local polling and rewriting it again later for sync, it's built as one reusable **diff engine** component with pluggable sources.

## Core abstraction

The diff engine's job, stripped of context: given a **candidate set** of IDs and one or more **known-state providers**, return the subset that is genuinely actionable (i.e. `unseen`).

```
DiffEngine.Resolve(candidates: Set<Id>, knownStateProviders: List<IKnownStateProvider>)
    → { unseen: Set<Id>, inFlight: Set<Id>, committed: Set<Id> }
```

Where `IKnownStateProvider` is anything that can answer "which of these IDs do you already know about, and in what state":

- **Local mode:** one provider backed by SQLite (`committed`), one backed by an in-memory set (`in_flight`).
- **Peer-sync mode:** a provider backed by a remote peer's manifest exchange.

The engine itself contains no I/O — it's pure set logic over whatever the providers report. This is what makes it reusable: swapping "local DB" for "remote peer manifest" doesn't change a single line of the engine, only which providers are wired in.

## Local mode (v1)

Used every poll cycle, exactly as described in [architecture-pipeline.md](../../base/product/architecture-pipeline.md#two-tier-request-flow):

- **Candidates:** IDs parsed from the current listing poll.
- **Provider 1 — committed:** a query against `projects.project_id` (indexed, cheap — see [data-model-schema.md](../../base/product/data-model-schema.md#projects)).
- **Provider 2 — in-flight:** the in-memory `HashSet<long>` guarded by the [concurrency model](concurrency-model.md).
- **Output used:** only `unseen` — that's what gets enqueued. `in_flight` and `committed` are discarded (nothing to do with them locally beyond confirming they're not re-enqueued).

## Peer-sync mode (v3, future)

Not implemented in MVP or v2 — documented here only so the v1 abstraction is shaped correctly from the start.

- **Candidates:** the union of both peers' manifests (lightweight `project_id` + hash/version, not full rows).
- **Providers:** each peer's own committed-ID set, exchanged over the LAN connection described in [roadmap-future.md](../../v2/product/roadmap-future.md#mobile-companion--lan-pairing).
- **Output used:** both directions matter here — `desktop_missing` and `mobile_missing` (see [roadmap-future.md § two-way peer sync](../../v2/product/roadmap-future.md#two-way-peer-sync)) are just the same `Resolve()` call run once per direction, swapping which side is "candidates" and which is "provider."
- Because the [no-update policy](../../base/product/architecture-pipeline.md#no-update-policy) makes every committed row immutable, there is no conflict-resolution step needed beyond presence/absence — which is exactly what this engine already computes.

## Interface sketch (C#)

Stack note: MAUI/C# is the likely target (superseding earlier Tauri/Rust references in [architecture-pipeline.md](../../base/product/architecture-pipeline.md) — that doc predates this decision and should be read as illustrative of the *concepts*, not the literal runtime).

```csharp
public interface IKnownStateProvider
{
    Task<HashSet<long>> GetKnownIdsAsync(IReadOnlySet<long> candidates);
}

public sealed class SqliteCommittedProvider : IKnownStateProvider
{
    // Backed by: SELECT project_id FROM projects WHERE project_id IN (...)
}

public sealed class InFlightSetProvider : IKnownStateProvider
{
    // Backed by the concurrency-safe set — see concurrency-model.md
}

public sealed class DiffEngine
{
    public async Task<DiffResult> ResolveAsync(
        IReadOnlySet<long> candidates,
        IEnumerable<IKnownStateProvider> providers)
    {
        var known = new HashSet<long>();
        foreach (var provider in providers)
            known.UnionWith(await provider.GetKnownIdsAsync(candidates));

        var unseen = candidates.Except(known).ToHashSet();
        return new DiffResult(unseen, known); // caller can further split `known`
                                               // by provider if it needs the
                                               // in-flight/committed distinction
    }
}
```

This is intentionally provider-count-agnostic — local mode wires in two providers, peer-sync mode could wire in more (e.g. a third peer later) without changing `DiffEngine` itself.

## What the diff engine explicitly does not do

- It does not fetch, parse, or enrich anything — it only decides *what's actionable*.
- It does not write to the DB or the in-flight set — those mutations happen in the [worker pool](worker-pool-and-rate-limiter.md) and [concurrency model](concurrency-model.md), triggered by the engine's output.
- It does not resolve conflicts or merge field-level data — the [no-update policy](../../base/product/architecture-pipeline.md#no-update-policy) means there's never a "same ID, different content" case to reconcile.

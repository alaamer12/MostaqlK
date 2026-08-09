namespace MostaqlK.Core;

/// <summary>
/// Central registry of error code domains used across MostaqlK's module-level Errors.cs files.
/// Each module owns a prefix and is responsible for keeping its own codes unique within it.
///
/// Domains:
///   CORE    - cross-cutting/shared infrastructure errors (Result/DomainError plumbing itself)
///   DB      - Infrastructure/Database (SQLite access, schema, repositories)
///   HTTP    - Infrastructure/Http (scraper HTTP calls, network failures)
///   PARSE   - Infrastructure/Http/Parsers (HTML listing/detail parsing failures)
///   POLL    - Services/Pipeline PollService (listing poll orchestration)
///   ENRICH  - Services/Pipeline EnrichmentService (per-project enrichment)
///   DIFF    - Services/Pipeline/DiffEngine (known-state diffing)
///   SCORE   - future relevance/scoring subsystem (reserved)
///   UI      - Features/* view-model and view level errors
///
/// This file intentionally contains no code — it documents the domain prefixes so that new
/// Errors.cs files pick a non-colliding prefix (e.g. "DB-001", "HTTP-002").
/// </summary>
public static class ErrorCodeRegistry
{
}

# Error Handling — Module Error Conventions

> **Companion to:** [`csharp-conventions.md §8`](csharp-conventions.md) (Throw / Result / Neither contracts)
> **Status:** Binding — all new modules must follow these conventions from day one.

This document covers the **structural** side of error handling: how errors are defined, named, centralized, and captured across modules. Read `csharp-conventions.md §8` first for the contract model.

---

## Table of Contents

1. [Module Error Files](#1-module-error-files)
2. [Error Code System](#2-error-code-system)
3. [Error Type Definitions](#3-error-type-definitions)
4. [Error Capture Rules](#4-error-capture-rules)
5. [Error Flattening — Aggregate Operations](#5-error-flattening--aggregate-operations)
6. [C# Attributes for Error Annotation](#6-c-attributes-for-error-annotation)
7. [Per-Module Error Registry](#7-per-module-error-registry)
8. [Anti-Patterns](#8-anti-patterns)

---

## 1. Module Error Files

### 1.1 — Convention

**Every module must contain an `Errors.cs` file.** This file is the single authoritative place where:
- All `Result<T>.Err` reason strings for the module are constructed
- All custom exception types for the module are declared
- All error codes for the module are defined as constants

No other file in the module may construct a `Result<T>.Err` with a bare string literal or `new` up a custom exception outside of this file's factory methods.

### 1.2 — Required File Per Module

```
MostaqlK.Core/
├── Errors.cs                         ← CoreErrors (base types, shared utilities)
├── ErrorAttributes.cs                ← [ErrorCode], [ErrorCategory], [NeitherContract] attributes

MostaqlK.Infrastructure.Database/
├── Errors.cs                         ← DatabaseErrors (exception types for Throw contract)

MostaqlK.Infrastructure.Http/
├── Errors.cs                         ← HttpErrors (factory methods for Result contract)

MostaqlK.Services.Pipeline/
├── Errors.cs                         ← PipelineErrors (poll, enrichment, diff factories)

MostaqlK.Services.Recommendation/
├── Errors.cs                         ← RecommendationErrors (scoring, profile factories)

MostaqlK/ (MAUI project)
└── Errors.cs                         ← ViewModelErrors (UI-layer state errors)
```

### 1.3 — Visibility Rules

| Error type | Visibility | Reason |
|---|---|---|
| `DomainError` factory methods | `internal` (default) | Errors stay inside the module; Application layer catches and wraps |
| Error code `readonly` fields | `public static readonly ErrorCode` | Callers can filter logs by code without hardcoding strings |
| Custom exception types | `public` or `internal` | Public if the Application layer catches them by type; `internal` if only logged |
| `Errors` static class itself | `internal` | The class is an implementation detail |

```csharp
// Correct — factory is internal, returns DomainError with all 4 fields
internal static class HttpErrors
{
    public static readonly ErrorCode RequestFailedCode = new("HTTP-001");

    internal static DomainError RequestFailed(string url, string detail, Exception? cause = null)
        => new(
            Code:            RequestFailedCode,
            InternalMessage: $"HTTP request to '{url}' failed: {detail}",
            ExternalMessage: "تعذّر الاتصال بالخادم.",
            FixMessage:      "تحقق من اتصالك بالإنترنت وأعد المحاولة.",
            Cause:           cause);
}

// Correct — exception type is public so Application layer can catch by type
[ErrorCode("DB-001")]
[ErrorCategory(ErrorCategory.Infrastructure)]
public sealed class DatabaseSchemaException : Exception { ... }
```

---

## 2. Error Code System

### 2.1 — Format

Every error has a code in the format **`{DOMAIN}-{NNN}`**, embedded at the start of the message string:

```
[HTTP-001] Request failed for 'https://mostaql.com/projects/123': connection refused
 ─────────  ────────────────────────────────────────────────────────────────────────
  code       human-readable context-rich message
```

The code makes it trivial to:
- Search structured logs by error code
- Track error frequency per type
- Reference errors in bug reports and release notes

### 2.2 — Domain Prefix Registry

| Prefix | Module | Layer |
|---|---|---|
| `CORE` | `MostaqlK.Core` | Shared |
| `DB` | `MostaqlK.Infrastructure.Database` | Infrastructure |
| `HTTP` | `MostaqlK.Infrastructure.Http` | Infrastructure |
| `PARSE` | HTML parsing (within Http module) | Infrastructure |
| `POLL` | Poll pipeline service | Application |
| `ENRICH` | Enrichment pipeline service | Application |
| `DIFF` | Diff engine | Application |
| `SCORE` | Recommendation engine | Application |
| `UI` | ViewModel / View layer | UI |

### 2.3 — Code Assignment Rules

- Codes are assigned sequentially within a domain: `HTTP-001`, `HTTP-002`, `HTTP-003`, …
- Once a code is assigned, it is **never reused** — even if the error is removed later. Mark removed codes as `// RETIRED`.
- New errors are always appended at the highest existing number + 1.
- Codes are `public const string` on the `Errors` class so callers can filter by code without hardcoding strings.

---

## 3. Error Type Definitions

### 3.0 — Core Types: `ErrorCode` and `DomainError`

Before defining any module error, two foundational types must exist in `MostaqlK.Core`:

```csharp
// MostaqlK.Core/DomainError.cs
namespace MostaqlK.Core;

/// <summary>
/// Strongly typed error code in <c>{DOMAIN}-{NNN}</c> format (e.g. <c>HTTP-001</c>).
/// Use <c>static readonly ErrorCode</c> fields on each module's <c>Errors</c> class.
/// </summary>
public readonly record struct ErrorCode(string Value)
{
    /// <summary>Returns the raw code string.</summary>
    public override string ToString() => Value;

    /// <summary>Implicit conversion so <c>ErrorCode</c> can be used wherever a string is expected.</summary>
    public static implicit operator string(ErrorCode code) => code.Value;
}

/// <summary>
/// A structured error carrying four pieces of information:
/// the machine code, the internal developer message, the user-facing message,
/// and an optional fix suggestion.
/// </summary>
/// <param name="Code">Machine-readable error code in <c>{DOMAIN}-{NNN}</c> format.</param>
/// <param name="InternalMessage">
/// Technical message for developers and log sinks.
/// Must include dynamic context (URL, project ID, etc.) for diagnosability.
/// </param>
/// <param name="ExternalMessage">
/// User-facing message in the application's locale (Arabic for this app).
/// Must be friendly, non-technical, and actionable without developer knowledge.
/// </param>
/// <param name="FixMessage">
/// Optional hint to help the user resolve the issue.
/// <c>null</c> when the error is self-healing or requires no user action.
/// </param>
/// <param name="Cause">The original exception, if one was captured. Always pass it — never drop the chain.</param>
public readonly record struct DomainError(
    ErrorCode  Code,
    string     InternalMessage,
    string     ExternalMessage,
    string?    FixMessage  = null,
    Exception? Cause       = null)
{
    /// <summary>Wraps this error in a <see cref="Result{T}.Err"/> for any T.</summary>
    public Result<T>.Err ToResult<T>() => new(this);

    /// <summary>Returns the code and internal message — suitable for log sinks.</summary>
    public override string ToString() => $"[{Code}] {InternalMessage}";
}
```

The updated `Result<T>` (in `MostaqlK.Core/Result.cs`) embeds `DomainError` instead of a bare string:

```csharp
// MostaqlK.Core/Result.cs
public abstract record Result<T>
{
    public sealed record Ok(T Value)            : Result<T>;
    public sealed record Err(DomainError Error) : Result<T>;

    public bool IsOk  => this is Ok;
    public bool IsErr => this is Err;

    /// <summary>Convenience factory — avoids spelling out <c>new Result&lt;T&gt;.Err(...)</c>.</summary>
    public static Result<T> Fail(DomainError error) => new Err(error);

    /// <summary>Unwraps the value or throws using the internal message.</summary>
    /// <exception cref="InvalidOperationException">Thrown when this is an <see cref="Err"/>.</exception>
    public T GetOrThrow() => this switch
    {
        Ok  ok  => ok.Value,
        Err err => throw new InvalidOperationException(
                       $"[{err.Error.Code}] {err.Error.InternalMessage}",
                       err.Error.Cause),
        _       => throw new UnreachableException()
    };
}
```

**Consuming a Result — accessing all four fields:**

```csharp
var result = await _enrichmentService.FetchDetailAsync(url, ct);

switch (result)
{
    case Result<ProjectDetails>.Ok ok:
        await _repo.SaveDetailsAsync(ok.Value, ct);
        break;

    case Result<ProjectDetails>.Err err:
        // InternalMessage → developer log (always)
        _logger.LogWarning(err.Error.Cause,
            "Error {Code}: {InternalMessage}",
            err.Error.Code, err.Error.InternalMessage);

        // ExternalMessage + FixMessage → UI (Arabic, user-friendly)
        ErrorMessage      = err.Error.ExternalMessage;
        FixSuggestion     = err.Error.FixMessage;  // may be null
        break;
}
```

---

### 3.1 — Result-Contract Modules: Static Factory Methods

Modules that use the Result contract define errors as **static factory methods** that return `DomainError`. All four fields must be populated — `FixMessage` is the only optional one.

```csharp
// MostaqlK.Infrastructure.Http/Errors.cs
namespace MostaqlK.Infrastructure.Http;

/// <summary>
/// Centralized error factory for the HTTP infrastructure module.
/// All <see cref="Result{T}.Err"/> values produced by this module originate here.
/// No other file in this module calls <c>Result.Fail</c> directly.
/// </summary>
[ErrorModule("Infrastructure.Http")]
internal static class HttpErrors
{
    // ── Error code constants ────────────────────────────────────────────────────
    public static readonly ErrorCode RequestFailedCode  = new("HTTP-001");
    public static readonly ErrorCode ParseFailedCode    = new("HTTP-002");
    public static readonly ErrorCode TimeoutCode        = new("HTTP-003");
    public static readonly ErrorCode RateLimitedCode    = new("HTTP-004");
    public static readonly ErrorCode UnexpectedCode     = new("HTTP-005");

    // ── Factory methods — return DomainError with all 4 fields ─────────────────

    /// <summary>HTTP request returned a non-2xx status or could not connect.</summary>
    internal static DomainError RequestFailed(string url, string detail, Exception? cause = null)
        => new(
            Code:            RequestFailedCode,
            InternalMessage: $"HTTP request to '{url}' failed: {detail}",
            ExternalMessage: "تعذّر الاتصال بالخادم.",
            FixMessage:      "تحقق من اتصالك بالإنترنت وأعد المحاولة. إذا استمرت المشكلة فقد يكون موقع مستقل غير متاح مؤقتاً.",
            Cause:           cause);

    /// <summary>HTML response received but could not be parsed into the expected model.</summary>
    internal static DomainError ParseFailed(string url, string selector, string detail, Exception? cause = null)
        => new(
            Code:            ParseFailedCode,
            InternalMessage: $"Parse failed for '{url}' at selector '{selector}': {detail}",
            ExternalMessage: "تعذّر قراءة بيانات المشروع من الموقع.",
            FixMessage:      "ربما تغيّر تصميم الموقع. سيتم إصلاح هذا تلقائياً في تحديث قادم — لا يلزم أي إجراء منك.",
            Cause:           cause);

    /// <summary>Request exceeded the configured timeout before a response was received.</summary>
    internal static DomainError Timeout(string url, TimeSpan elapsed, Exception? cause = null)
        => new(
            Code:            TimeoutCode,
            InternalMessage: $"Timeout after {elapsed.TotalSeconds:F1}s fetching '{url}'",
            ExternalMessage: "استغرق الاتصال بالخادم وقتاً طويلاً.",
            FixMessage:      "تحقق من اتصالك بالإنترنت. إذا كان جيداً فقد يكون موقع مستقل بطيئاً حالياً — أعد المحاولة بعد لحظات.",
            Cause:           cause);

    /// <summary>Request rejected by server's rate limiter (HTTP 429).</summary>
    internal static DomainError RateLimited(string url, TimeSpan retryAfter, Exception? cause = null)
        => new(
            Code:            RateLimitedCode,
            InternalMessage: $"Rate limited for '{url}'; retry after {retryAfter.TotalSeconds:F0}s",
            ExternalMessage: "جارٍ تحديث البيانات. التطبيق يُبطئ الطلبات تلقائياً للحفاظ على الاستقرار.",
            FixMessage:      null,   // self-healing — no user action needed
            Cause:           cause);

    /// <summary>An unexpected exception not matching any known failure category.</summary>
    internal static DomainError Unexpected(string url, string exceptionType, string message, Exception? cause = null)
        => new(
            Code:            UnexpectedCode,
            InternalMessage: $"Unexpected {exceptionType} fetching '{url}': {message}",
            ExternalMessage: "حدث خطأ غير متوقع أثناء تحديث البيانات.",
            FixMessage:      "أعد تشغيل التطبيق. إذا تكررت المشكلة يرجى الإبلاغ عنها من إعدادات التطبيق.",
            Cause:           cause);
}
```

Usage in the service — use `Result<T>.Fail(DomainError)`:

```csharp
// MostaqlK.Infrastructure.Http/MostaqlScraper.cs
catch (HttpRequestException ex)
{
    return Result<ProjectListing>.Fail(
        HttpErrors.RequestFailed(url, ex.Message, ex));
}
catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
{
    // TaskCanceledException from HttpClient on timeout — not a user cancellation
    return Result<ProjectListing>.Fail(
        HttpErrors.Timeout(url, TimeSpan.FromSeconds(30), ex));
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    return Result<ProjectListing>.Fail(
        HttpErrors.Unexpected(url, ex.GetType().Name, ex.Message, ex));
}
```

### 3.2 — Throw-Contract Modules: Custom Exception Classes

Modules that use the Throw contract (primarily Infrastructure.Database) define **typed exception classes** in `Errors.cs`. These are always public so the Application layer can catch them by type.

```csharp
// MostaqlK.Infrastructure.Database/Errors.cs
namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Thrown when the database schema version does not match the application's expected version.
/// This is a startup invariant violation — the application cannot run with an incompatible schema.
/// Uses the <b>Throw</b> error contract.
/// </summary>
/// <remarks>
/// This exception propagates through the application layer and is caught at the host startup level.
/// It is never converted to a <see cref="Result{T}"/> — it halts startup.
/// </remarks>
[ErrorCode("DB-001")]
[ErrorCategory(ErrorCategory.Infrastructure)]
public sealed class DatabaseSchemaException : Exception
{
    /// <summary>The schema version the application requires.</summary>
    public int ExpectedVersion { get; }

    /// <summary>The schema version actually present in the database file.</summary>
    public int ActualVersion { get; }

    /// <inheritdoc/>
    public DatabaseSchemaException(int expectedVersion, int actualVersion, Exception? inner = null)
        : base(
            $"[DB-001] Schema version mismatch: expected v{expectedVersion}, found v{actualVersion}. " +
            $"Run migrations before starting the application.",
            inner)
    {
        ExpectedVersion = expectedVersion;
        ActualVersion   = actualVersion;
    }
}

/// <summary>
/// Thrown when a schema migration script fails to execute.
/// Uses the <b>Throw</b> error contract.
/// </summary>
[ErrorCode("DB-002")]
[ErrorCategory(ErrorCategory.Infrastructure)]
public sealed class MigrationException : Exception
{
    /// <summary>The version number of the migration that failed.</summary>
    public int MigrationVersion { get; }

    /// <inheritdoc/>
    public MigrationException(int version, string reason, Exception? inner = null)
        : base($"[DB-002] Migration to v{version} failed: {reason}", inner)
    {
        MigrationVersion = version;
    }
}
```

### 3.3 — Rule: One String Per Error Shape

Every distinct failure mode gets its own factory method or exception class. Never pass raw computed strings to `Result<T>.Err` outside of `Errors.cs`:

```csharp
// WRONG — bare string constructed at the call site
return new Result<ProjectDetails>.Err($"Could not parse budget from '{rawText}'");

// WRONG — calls Errors.cs but adds extra formatting outside
return new Result<ProjectDetails>.Err(
    HttpErrors.ParseFailed(url, selector, detail) + " (retried 3 times)");

// CORRECT — all context is captured in the factory call
return new Result<ProjectDetails>.Err(
    HttpErrors.ParseFailed(url, selector, $"{detail} (after 3 retries)"), ex);
```

---

## 4. Error Capture Rules

### Rule 1 — Always Pass the Original Exception as `Cause`

`DomainError.Cause` must contain the original exception whenever one was caught. This preserves the full stack trace and inner exception chain. The `Cause` is passed as the last argument to every factory method.

```csharp
// WRONG — exception chain completely lost; Cause is null
catch (HttpRequestException ex)
{
    return Result<ProjectDetails>.Fail(HttpErrors.RequestFailed(url, ex.Message));
}

// CORRECT — original exception preserved as DomainError.Cause
catch (HttpRequestException ex)
{
    return Result<ProjectDetails>.Fail(HttpErrors.RequestFailed(url, ex.Message, ex));
}
```

### Rule 2 — Always Log Before Discarding (Neither Contract)

The Neither contract swallows failures, but they must always be logged at `Warning` or higher. **An error must never disappear without a trace.**

```csharp
// WRONG — error silently disappears, completely undetectable
catch (Exception) { }

// WRONG — catches but doesn't log
catch (Exception ex) when (ex is not OperationCanceledException) { return; }

// CORRECT — log before discarding; the neither contract still leaves a trace
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogWarning(ex,
        "Failed to record interaction {Type} for project {ProjectId}. " +
        "Error is non-critical and has been discarded.",
        interactionType, projectId);
}
```

### Rule 3 — Log the Full Exception Object, Not `ex.Message`

Structured logging sinks (e.g. Serilog, OpenTelemetry) serialize the exception object including the full stack trace and inner exception chain when passed as the first argument. Never pass only `ex.Message`.

```csharp
// WRONG — loses stack trace, loses inner exceptions
_logger.LogError("Enrichment failed: {Message}", ex.Message);

// CORRECT — full exception object is serialized by the logging sink
_logger.LogError(ex, "Enrichment failed for project {ProjectId}", projectId);
```

### Rule 4 — Flatten `AggregateException` Before Processing

When catching `AggregateException` (from `Task.WhenAll`, `Parallel.ForEach`, etc.), always call `.Flatten()` before extracting inner exceptions.

```csharp
// WRONG — AggregateException may contain nested AggregateExceptions
catch (AggregateException agg)
{
    var msg = string.Join("; ", agg.InnerExceptions.Select(e => e.Message));
    return new Result<T>.Err($"Multiple errors: {msg}", agg);
}

// CORRECT — .Flatten() recursively unwraps nested AggregateExceptions
catch (AggregateException agg)
{
    var flat     = agg.Flatten();
    var messages = flat.InnerExceptions.Select(e => e.Message);
    _logger.LogError(flat, "Multiple failures: {Messages}", string.Join("; ", messages));
    return new Result<T>.Err(
        $"[MULTI] {flat.InnerExceptions.Count} failure(s): {string.Join("; ", messages)}",
        flat);
}
```

### Rule 5 — Exclude `OperationCanceledException` from Catch-All Clauses

`OperationCanceledException` must always propagate. Wrapping it in a Result or logging it as a failure is wrong — cancellation is a normal cooperative shutdown signal, not an error.

```csharp
// WRONG — catches cancellation as if it were an error
catch (Exception ex)
{
    return new Result<T>.Err("Operation failed", ex);
}

// CORRECT — exception filter excludes cancellation
catch (Exception ex) when (ex is not OperationCanceledException)
{
    return new Result<T>.Err(SomeErrors.OperationFailed(ex.Message), ex);
}
// OperationCanceledException propagates up — no catch needed
```

### Rule 6 — Include Context in Every Error Message

An error message without context forces the reader to correlate multiple log entries. Include the subject (project ID, URL, etc.) in every message.

```csharp
// WRONG — no context; impossible to diagnose from logs alone
return new Result<ProjectDetails>.Err(HttpErrors.ParseFailed("", "", ex.Message), ex);

// CORRECT — full context; a single log entry tells the whole story
return new Result<ProjectDetails>.Err(
    HttpErrors.ParseFailed(project.Url, ".budget-range", ex.Message), ex);
```

---

## 5. Error Flattening — Aggregate Operations

The pipeline processes batches of projects. **Never use fail-fast for batch operations.** Collect all failures and return them alongside the successes.

### 5.1 — `BatchResult<T>` Pattern

```csharp
// MostaqlK.Core/Domain/BatchResult.cs

/// <summary>
/// The result of an operation applied to a collection of items.
/// Contains both successful results and itemized failures.
/// </summary>
/// <typeparam name="T">The type of a successful result.</typeparam>
public sealed record BatchResult<T>(
    IReadOnlyList<T>             Successes,
    IReadOnlyList<ItemFailure>   Failures)
{
    /// <summary><c>true</c> if all items succeeded.</summary>
    public bool IsFullSuccess => Failures.Count == 0;

    /// <summary><c>true</c> if at least one item failed.</summary>
    public bool HasFailures => Failures.Count > 0;

    /// <summary><c>true</c> if every item failed.</summary>
    public bool IsFullFailure => Successes.Count == 0 && Failures.Count > 0;
}

/// <summary>
/// Describes a single item failure within a batch operation.
/// </summary>
/// <param name="ItemId">The identifier of the item that failed (e.g. project_id).</param>
/// <param name="Error">The full <see cref="DomainError"/> including code, messages, fix hint, and cause.</param>
public readonly record struct ItemFailure(
    long        ItemId,
    DomainError Error)
{
    /// <summary>Shorthand for <c>Error.InternalMessage</c> — for log formatting.</summary>
    public string InternalMessage => Error.InternalMessage;

    /// <summary>Shorthand for <c>Error.ExternalMessage</c> — for UI display.</summary>
    public string ExternalMessage => Error.ExternalMessage;
}
```

### 5.2 — Batch Processing Template

```csharp
/// <summary>
/// Enriches a batch of projects. Processes all items regardless of individual failures.
/// </summary>
/// <remarks>
/// Uses the Result contract. Returns <see cref="Result{T}.Ok"/> even when some items fail —
/// the caller inspects <see cref="BatchResult{T}.Failures"/> to determine partial failure.
/// Returns <see cref="Result{T}.Err"/> only if a systemic failure prevents processing
/// (e.g. database is unreachable).
/// </remarks>
public async Task<Result<BatchResult<ProjectDetails>>> EnrichBatchAsync(
    IReadOnlyList<Project> projects,
    CancellationToken      ct)
{
    var successes = new List<ProjectDetails>(projects.Count);
    var failures  = new List<ItemFailure>();

    foreach (var project in projects)
    {
        ct.ThrowIfCancellationRequested();

        var result = await FetchDetailAsync(project.Url, ct);

        switch (result)
        {
            case Result<ProjectDetails>.Ok ok:
                successes.Add(ok.Value);
                break;

            case Result<ProjectDetails>.Err err:
                // Log InternalMessage for developers — do NOT stop processing
                _logger.LogWarning(err.Error.Cause,
                    "Error {Code} — project {Id}: {InternalMessage}",
                    err.Error.Code, project.ProjectId, err.Error.InternalMessage);

                // ExternalMessage + FixMessage available on ItemFailure for UI display
                failures.Add(new ItemFailure(project.ProjectId, err.Error));
                break;
        }
    }

    if (failures.Count > 0)
        _logger.LogWarning(
            "Batch enrichment complete: {SuccessCount} succeeded, {FailureCount} failed. " +
            "Codes: {Codes}",
            successes.Count,
            failures.Count,
            string.Join(", ", failures.Select(f => (string)f.Error.Code)));

    return new Result<BatchResult<ProjectDetails>>.Ok(
        new BatchResult<ProjectDetails>(successes, failures));
}
```

### 5.3 — When to Use Fail-Fast vs. Aggregate

| Scenario | Strategy |
|---|---|
| Pipeline batch (enrich N projects) | **Aggregate** — continue after each failure |
| Single-item operation | **Fail-fast** — return Result.Err immediately |
| Database migration steps | **Fail-fast** — a failed migration step halts all subsequent steps |
| UI validation of N fields | **Aggregate** — collect all validation errors before showing the user |
| Poll cycle (fetch listing pages) | **Aggregate** — try all pages; report per-page failures |

---

## 6. C# Attributes for Error Annotation

### 6.1 — Attribute Definitions

These attributes live in `MostaqlK.Core/ErrorAttributes.cs` and are used throughout the codebase as documentation and metadata. They do not change runtime behavior but serve as machine-readable contracts that can be queried via reflection and used by custom Roslyn analysers in the future.

```csharp
// MostaqlK.Core/ErrorAttributes.cs
namespace MostaqlK.Core;

/// <summary>
/// Annotates an exception class with a machine-readable error code.
/// The code follows the format <c>{DOMAIN}-{NNN}</c> (e.g. <c>"HTTP-001"</c>, <c>"DB-002"</c>).
/// </summary>
/// <remarks>
/// Applied to exception classes used by Throw-contract methods.
/// Must match the code constant defined in the module's <c>Errors.cs</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ErrorCodeAttribute(string code) : Attribute
{
    /// <summary>The error code in <c>{DOMAIN}-{NNN}</c> format.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// Categorizes an exception or error class by the architectural layer that owns it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ErrorCategoryAttribute(ErrorCategory category) : Attribute
{
    /// <summary>The architectural layer responsible for this error.</summary>
    public ErrorCategory Category { get; } = category;
}

/// <summary>
/// Documents that a class or interface uses a specific module's <c>Errors.cs</c>
/// as its error source. Informational — intended for navigation and audit.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
public sealed class ErrorModuleAttribute(string moduleName) : Attribute
{
    /// <summary>The module name — matches the namespace segment (e.g. <c>"Infrastructure.Http"</c>).</summary>
    public string ModuleName { get; } = moduleName;
}

/// <summary>
/// Marks an interface method as using the <b>Neither</b> error contract.
/// The method never throws and never returns a failure value to the caller.
/// </summary>
/// <remarks>
/// This attribute is documentation only — behavior must be implemented and
/// must match the contract description in the XML <c>&lt;remarks&gt;</c>.
/// If applied to a method that actually throws or returns <see cref="Result{T}"/>,
/// that is a contract violation.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class NeitherContractAttribute : Attribute { }

/// <summary>Architectural layer classification for error categories.</summary>
public enum ErrorCategory
{
    /// <summary>Database, HTTP, file system — external resource failures.</summary>
    Infrastructure = 0,

    /// <summary>Business rule violations in the domain model.</summary>
    Domain         = 1,

    /// <summary>Service-level coordination failures (pipeline, enrichment, scoring).</summary>
    Application    = 2,

    /// <summary>ViewModel state errors surfaced to the UI.</summary>
    Ui             = 3
}
```

### 6.2 — Applying Attributes to Interfaces

```csharp
/// <summary>Scrapes project data from Mostaql.</summary>
[ErrorModule("Infrastructure.Http")]
public interface IProjectScraper
{
    /// <summary>Fetches a single listing page.</summary>
    /// <returns>
    /// <see cref="Result{T}.Ok"/> with parsed listings on success.<br/>
    /// <see cref="Result{T}.Err"/> for network, timeout, or parse failures.
    /// Never throws for these expected cases.
    /// </returns>
    Task<Result<ProjectListing>> FetchListingAsync(int page, CancellationToken cancellationToken = default);

    /// <summary>Fetches enrichment detail for a single project URL.</summary>
    Task<Result<ProjectDetails>> FetchDetailAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>Tests whether the Mostaql server is reachable.</summary>
    /// <remarks>
    /// Uses the <b>Neither</b> error contract — never throws and never reports failure.
    /// Returns <c>false</c> on any failure, including network errors.
    /// </remarks>
    [NeitherContract]
    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);
}
```

### 6.3 — Applying Attributes to Exception Classes

```csharp
[ErrorCode("DB-001")]
[ErrorCategory(ErrorCategory.Infrastructure)]
public sealed class DatabaseSchemaException : Exception { ... }

[ErrorCode("DB-002")]
[ErrorCategory(ErrorCategory.Infrastructure)]
public sealed class MigrationException : Exception { ... }
```

### 6.4 — Attributes Do Not Replace Documentation

Every `[NeitherContract]` method still requires a full `<remarks>` block (see `csharp-conventions.md §12`). Every exception class marked `[ErrorCode]` still requires a `<summary>` explaining when it is thrown. Attributes are a machine-readable supplement to human-readable documentation — not a replacement.

---

## 7. Per-Module Error Registry

Each module below lists its complete `Errors.cs` errors — all four fields filled in. These are the canonical definitions; when implementation diverges, this document is the source of truth.

---

### `MostaqlK.Core` — `Errors.cs`

Shared base utilities only. No domain-specific error codes here.

```csharp
namespace MostaqlK.Core;

internal static class CoreErrors
{
    public static readonly ErrorCode UnreachableCode = new("CORE-001");

    /// <summary>Raises <see cref="UnreachableException"/> for switch arms that should never be reached.</summary>
    internal static UnreachableException Unreachable(string context)
        => new($"[CORE-001] Unreachable code reached in: {context}. This is a programming error.");
}
```

---

### `MostaqlK.Infrastructure.Database` — `Errors.cs`

Contract: **Throw** — typed exception classes, each with `ExternalMessage` and `FixMessage` properties.

| Code | Exception class | InternalMessage pattern | ExternalMessage (AR) | FixMessage (AR) |
|---|---|---|---|---|
| `DB-001` | `DatabaseSchemaException` | `Schema mismatch: expected v{x}, found v{y}` | `تعذّر فتح قاعدة البيانات بسبب إصدار غير متوافق.` | `يرجى إلغاء تثبيت التطبيق وإعادة تثبيته.` |
| `DB-002` | `MigrationException` | `Migration to v{n} failed: {reason}` | `حدث خطأ أثناء إعداد قاعدة البيانات.` | `يرجى إلغاء تثبيت التطبيق وإعادة تثبيته.` |
| `DB-003` | `TransactionConflictException` | `Write conflict on shared connection` | `فشل حفظ البيانات بسبب تعارض في العمليات.` | `أعد المحاولة. إذا تكررت المشكلة أعد تشغيل التطبيق.` |

---

### `MostaqlK.Infrastructure.Http` — `Errors.cs`

Contract: **Result** — `DomainError` factories. See §3.1 for the full `HttpErrors` class.

| Code | Factory signature | InternalMessage pattern | ExternalMessage (AR) | FixMessage (AR) |
|---|---|---|---|---|
| `HTTP-001` | `RequestFailed(url, detail, cause?)` | `HTTP request to '{url}' failed: {detail}` | `تعذّر الاتصال بالخادم.` | `تحقق من اتصالك بالإنترنت وأعد المحاولة.` |
| `HTTP-002` | `ParseFailed(url, selector, detail, cause?)` | `Parse failed for '{url}' at '{selector}': {detail}` | `تعذّر قراءة بيانات المشروع من الموقع.` | `سيتم إصلاح هذا في تحديث قادم — لا يلزم أي إجراء منك.` |
| `HTTP-003` | `Timeout(url, elapsed, cause?)` | `Timeout after {n}s fetching '{url}'` | `استغرق الاتصال بالخادم وقتاً طويلاً.` | `تحقق من اتصالك وأعد المحاولة بعد لحظات.` |
| `HTTP-004` | `RateLimited(url, retryAfter, cause?)` | `Rate limited for '{url}'; retry after {n}s` | `جارٍ تحديث البيانات، يُرجى الانتظار.` | *(null — self-healing)* |
| `HTTP-005` | `Unexpected(url, exType, msg, cause?)` | `Unexpected {ExType} fetching '{url}': {msg}` | `حدث خطأ غير متوقع أثناء تحديث البيانات.` | `أعد تشغيل التطبيق. إذا تكررت المشكلة يرجى الإبلاغ عنها.` |

---

### `MostaqlK.Services.Pipeline` — `Errors.cs`

Contract: **Result** — Application services convert Infrastructure throws into these `DomainError` values.

```csharp
// MostaqlK.Services.Pipeline/Errors.cs
namespace MostaqlK.Services.Pipeline;

internal static class PipelineErrors
{
    // ── Poll errors ──────────────────────────────────────────────────────────────
    public static readonly ErrorCode ListingFetchFailedCode  = new("POLL-001");
    public static readonly ErrorCode ListingParseFailedCode  = new("POLL-002");

    internal static DomainError ListingFetchFailed(int page, string detail, Exception? cause = null)
        => new(
            Code:            ListingFetchFailedCode,
            InternalMessage: $"Failed to fetch listing page {page}: {detail}",
            ExternalMessage: "تعذّر تحديث قائمة المشاريع.",
            FixMessage:      "تأكد من الاتصال بالإنترنت. ستتم إعادة المحاولة تلقائياً.",
            Cause:           cause);

    internal static DomainError ListingParseFailed(int page, string detail, Exception? cause = null)
        => new(
            Code:            ListingParseFailedCode,
            InternalMessage: $"Failed to parse listing page {page}: {detail}",
            ExternalMessage: "تعذّر قراءة بيانات قائمة المشاريع.",
            FixMessage:      "ربما تغيّر تصميم الموقع. سيتم إصلاحه في تحديث قادم.",
            Cause:           cause);

    // ── Enrichment errors ────────────────────────────────────────────────────────
    public static readonly ErrorCode EnrichmentFailedCode   = new("ENRICH-001");
    public static readonly ErrorCode DetailParseFailedCode  = new("ENRICH-002");

    internal static DomainError EnrichmentFailed(long projectId, string detail, Exception? cause = null)
        => new(
            Code:            EnrichmentFailedCode,
            InternalMessage: $"Enrichment failed for project {projectId}: {detail}",
            ExternalMessage: "تعذّر تحميل تفاصيل أحد المشاريع.",
            FixMessage:      "ستتم إعادة المحاولة في الدورة القادمة — لا يلزم أي إجراء منك.",
            Cause:           cause);

    internal static DomainError DetailParseFailed(long projectId, string selector, string detail, Exception? cause = null)
        => new(
            Code:            DetailParseFailedCode,
            InternalMessage: $"Detail parse failed for project {projectId} at '{selector}': {detail}",
            ExternalMessage: "تعذّر قراءة تفاصيل أحد المشاريع.",
            FixMessage:      "ربما تغيّر تصميم الموقع. سيتم إصلاحه في تحديث قادم.",
            Cause:           cause);

    // ── Diff errors ──────────────────────────────────────────────────────────────
    public static readonly ErrorCode DiffFailedCode = new("DIFF-001");

    internal static DomainError DiffFailed(string detail, Exception? cause = null)
        => new(
            Code:            DiffFailedCode,
            InternalMessage: $"Diff computation failed: {detail}",
            ExternalMessage: "تعذّر مقارنة التحديثات الجديدة بالبيانات المحفوظة.",
            FixMessage:      "أعد تشغيل التطبيق. إذا تكررت المشكلة يرجى الإبلاغ عنها.",
            Cause:           cause);
}
```

---

### `MostaqlK.Services.Recommendation` — `Errors.cs`

Contract: **Result** — recommendation engine failures.

```csharp
// MostaqlK.Services.Recommendation/Errors.cs
namespace MostaqlK.Services.Recommendation;

internal static class RecommendationErrors
{
    public static readonly ErrorCode ProfileBuildFailedCode    = new("SCORE-001");
    public static readonly ErrorCode ScoreComputeFailedCode    = new("SCORE-002");
    public static readonly ErrorCode PersistFailedCode         = new("SCORE-003");

    internal static DomainError ProfileBuildFailed(string detail, Exception? cause = null)
        => new(
            Code:            ProfileBuildFailedCode,
            InternalMessage: $"Cannot build preference profile: {detail}",
            ExternalMessage: "لم تتوفر بيانات كافية لعرض التوصيات الشخصية بعد.",
            FixMessage:      "افتح بعض المشاريع وتفاعل معها لتفعيل التوصيات.",
            Cause:           cause);

    internal static DomainError ScoreComputeFailed(long projectId, string detail, Exception? cause = null)
        => new(
            Code:            ScoreComputeFailedCode,
            InternalMessage: $"Score computation failed for project {projectId}: {detail}",
            ExternalMessage: "حدث خطأ أثناء حساب التوصيات.",
            FixMessage:      "ستتم إعادة الحساب تلقائياً في الجلسة القادمة.",
            Cause:           cause);

    internal static DomainError PersistFailed(string detail, Exception? cause = null)
        => new(
            Code:            PersistFailedCode,
            InternalMessage: $"Failed to persist recommendation scores: {detail}",
            ExternalMessage: "تعذّر حفظ نتائج التوصيات.",
            FixMessage:      "ستظهر التوصيات في الجلسة القادمة بعد إعادة الحساب.",
            Cause:           cause);
}
```

---

### `MostaqlK` (UI) — `Errors.cs`

Contract: **UI state** — errors surface as observable properties on ViewModels. Never thrown.

```csharp
// MostaqlK/Errors.cs  (MAUI project root)
namespace MostaqlK;

internal static class ViewModelErrors
{
    public static readonly ErrorCode LoadFailedCode   = new("UI-001");
    public static readonly ErrorCode FilterInvalidCode = new("UI-002");

    /// <summary>Generic screen-load failure shown in the error banner.</summary>
    internal static DomainError LoadFailed(string screenName, string detail, Exception? cause = null)
        => new(
            Code:            LoadFailedCode,
            InternalMessage: $"Failed to load screen '{screenName}': {detail}",
            ExternalMessage: "تعذّر تحميل الصفحة.",
            FixMessage:      "أعد المحاولة. إذا استمرت المشكلة أعد تشغيل التطبيق.",
            Cause:           cause);

    /// <summary>A filter field failed validation — shown inline beside the field.</summary>
    internal static DomainError FilterInvalid(string fieldName, string detail)
        => new(
            Code:            FilterInvalidCode,
            InternalMessage: $"Filter validation failed for '{fieldName}': {detail}",
            ExternalMessage: $"قيمة غير صالحة في حقل '{fieldName}'.",
            FixMessage:      "تحقق من القيمة المدخلة وأعد المحاولة.",
            Cause:           null);
}
```

---

## 8. Anti-Patterns

### 8.1 — Constructing `DomainError` Outside `Errors.cs`

```csharp
// WRONG — ad-hoc DomainError at the call site, bypasses centralization
return Result<ProjectDetails>.Fail(new DomainError(
    new ErrorCode("HTTP-002"),
    "Could not parse budget",
    "تعذّر القراءة",
    null));

// CORRECT — always go through the module's Errors.cs factory
return Result<ProjectDetails>.Fail(
    HttpErrors.ParseFailed(url, ".budget-range", ex.Message, ex));
```

### 8.2 — Omitting `Cause` From the Factory Call

```csharp
// WRONG — exception caught but Cause is null; full chain is lost
catch (HttpRequestException ex)
{
    return Result<ProjectDetails>.Fail(HttpErrors.RequestFailed(url, ex.Message));
}

// CORRECT — always forward the exception as the last factory argument
catch (HttpRequestException ex)
{
    return Result<ProjectDetails>.Fail(HttpErrors.RequestFailed(url, ex.Message, ex));
}
```

### 8.3 — Silent Swallow (the "black hole")

```csharp
// WRONG — error disappears with zero trace
catch (Exception) { }

// WRONG — catches but doesn't log; no trace in any log sink
catch (Exception ex) when (ex is not OperationCanceledException) { return; }

// CORRECT — always log InternalMessage before discarding
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogWarning(ex,
        "Non-critical failure in {Component}: {ExType}. Error discarded.",
        nameof(InteractionTracker), ex.GetType().Name);
}
```

### 8.4 — Showing `InternalMessage` to Users

```csharp
// WRONG — surfaces raw technical detail to the user
case Result<ProjectDetails>.Err err:
    ErrorMessage = err.Error.InternalMessage;  // "HTTP request to 'https://...' failed: ..."

// CORRECT — InternalMessage → log; ExternalMessage → UI
case Result<ProjectDetails>.Err err:
    _logger.LogWarning(err.Error.Cause,
        "Error {Code}: {InternalMessage}", err.Error.Code, err.Error.InternalMessage);
    ErrorMessage  = err.Error.ExternalMessage;  // "تعذّر الاتصال بالخادم."
    FixSuggestion = err.Error.FixMessage;        // Arabic hint, may be null
```

### 8.5 — Logging Only `ex.Message` Instead of the Exception Object

```csharp
// WRONG — loses stack trace and all inner exceptions
_logger.LogError("Error {Code}: {Message}", err.Error.Code, ex.Message);

// CORRECT — pass the full exception object; the logging sink serializes the full chain
_logger.LogError(err.Error.Cause,
    "Error {Code}: {InternalMessage}", err.Error.Code, err.Error.InternalMessage);
```

### 8.6 — Catching `OperationCanceledException` as an Error

```csharp
// WRONG — treats cooperative cancellation as a business failure
catch (Exception ex)
{
    return Result<T>.Fail(SomeErrors.Failed(ex.Message, ex));
}

// CORRECT — exception filter excludes cancellation; it propagates naturally
catch (Exception ex) when (ex is not OperationCanceledException)
{
    return Result<T>.Fail(SomeErrors.Failed(ex.Message, ex));
}
```

### 8.7 — Fail-Fast in Batch Operations

```csharp
// WRONG — one failure aborts the entire batch; other items are never processed
foreach (var project in projects)
{
    var result = await EnrichAsync(project, ct);
    if (result.IsErr) return result; // ← stops here; remaining items unprocessed
}

// CORRECT — collect all DomainErrors; process all items
var failures = new List<ItemFailure>();
foreach (var project in projects)
{
    var result = await EnrichAsync(project, ct);
    if (result is Result<ProjectDetails>.Err err)
    {
        _logger.LogWarning(err.Error.Cause,
            "Error {Code} — project {Id}: {Msg}",
            err.Error.Code, project.ProjectId, err.Error.InternalMessage);
        failures.Add(new ItemFailure(project.ProjectId, err.Error));
    }
}
```

### 8.6 — Mixing Error Contracts in One Module

```csharp
// WRONG — IEnrichmentService is inconsistent: callers must both catch AND check Result
public interface IEnrichmentService
{
    Task<Result<ProjectDetails>> FetchDetailAsync(string url, CancellationToken ct = default);
    Task<ProjectDetails>         ParseDetailAsync(string html);  // throws on parse error!
}

// CORRECT — consistent: all operations return Result<T>
public interface IEnrichmentService
{
    Task<Result<ProjectDetails>>          FetchDetailAsync(string url,  CancellationToken ct = default);
    Task<Result<IReadOnlyList<string>>>   ParseSkillsAsync(string html, CancellationToken ct = default);
    Task<Result<BudgetRange?>>            ParseBudgetAsync(string html, CancellationToken ct = default);
}
```

### 8.7 — Exception Type Created Outside `Errors.cs`

```csharp
// WRONG — exception type defined in a service file, bypassing centralization
public sealed class MyException : Exception { ... } // in EnrichmentService.cs

// CORRECT — exception types live exclusively in Errors.cs
// MostaqlK.Infrastructure.Database/Errors.cs:
public sealed class DatabaseSchemaException : Exception { ... }
```

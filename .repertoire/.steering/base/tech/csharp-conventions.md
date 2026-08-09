# C# Implementation Conventions

> **Runtime:** .NET 10 (LTS — supported until November 2028)
> **Language version:** C# 14
> **Always search NuGet for the latest stable version** before adding a package; this document lists versions as of August 2026.

---

## Table of Contents

1. [Project Configuration](#1-project-configuration)
2. [Type System Reference](#2-type-system-reference)
3. [Type Declaration Hierarchy](#3-type-declaration-hierarchy)
4. [Nullability Rules](#4-nullability-rules)
5. [Collection Type Selection](#5-collection-type-selection)
6. [Async and Concurrency](#6-async-and-concurrency)
7. [C# 14 Feature Adoption](#7-c-14-feature-adoption)
8. [Error Handling](#8-error-handling)
9. [Interfaces and Dependency Injection](#9-interfaces-and-dependency-injection)
10. [LINQ](#10-linq)
11. [Approved NuGet Packages](#11-approved-nuget-packages)
12. [XML Documentation](#12-xml-documentation)
13. [Naming Conventions](#13-naming-conventions)
14. [Standard Library First](#14-standard-library-first)
15. [Design Patterns for Extensibility](#15-design-patterns-for-extensibility)
16. [Code Organization: Local Functions and Helpers](#16-code-organization-local-functions-and-helpers)

---

## 1. Project Configuration

Every project in the solution carries the following `PropertyGroup`:

```xml
<PropertyGroup>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
    <LangVersion>preview</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
</PropertyGroup>
```

| Setting | Effect |
|---|---|
| `Nullable=enable` | NRT is on. Every `T?` is intentional. Every `T` is a guarantee of non-null. |
| `TreatWarningsAsErrors=true` | Nullable warnings, unused variables, and analyser warnings are all build errors. No warning debt accumulates. |
| `AnalysisLevel=latest` | All Roslyn analysers ship enabled. New rules in patch releases are active immediately. |
| `LangVersion=preview` | Enables C# 14 features before they are locked into `latest`. Switch to `latest` after .NET 10.0 GA settles. |
| `ImplicitUsings=enable` | Standard usings (`System`, `System.Collections.Generic`, etc.) are injected globally. Project-specific globals go in `Usings.cs`. |
| `AllowUnsafeBlocks=false` | Unsafe code is prohibited. Use `Span<T>` / `Memory<T>` for performance-sensitive buffer work. |

---

## 2. Type System Reference

### 2.1 — Conceptual Type Mapping

The table below maps common Python typing concepts to their idiomatic C# 14 / .NET 10 equivalents. **Use the C# equivalent listed — not raw concrete types.**

| Concept / Python typing | C# equivalent | Notes |
|---|---|---|
| `list[T]` | `IReadOnlyList<T>` (return) / `IList<T>` (mutable param) | Never expose `List<T>` as a return type from public APIs |
| `Sequence[T]` | `IReadOnlyCollection<T>` or `IEnumerable<T>` | Use `IReadOnlyCollection<T>` when the count is semantically meaningful |
| `MutableSequence[T]` | `IList<T>` | Use in parameters that need both read and write |
| `Mapping[K, V]` (read) | `IReadOnlyDictionary<TKey, TValue>` | Return this, not `Dictionary<K,V>` |
| `Mapping[K, V]` (mutable) | `IDictionary<TKey, TValue>` | Pass this into constructors or methods that mutate |
| `Iterable[T]` | `IEnumerable<T>` | Deferred/lazy sequences only — never materialize inside the returning method |
| `Iterator[T]` | `IEnumerator<T>` / `IAsyncEnumerator<T>` | Rarely needed directly; prefer `IEnumerable<T>` / `IAsyncEnumerable<T>` |
| `AsyncIterable[T]` | `IAsyncEnumerable<T>` | Stream of T from async source (pipeline stages, DB cursors) |
| `Set[T]` | `IReadOnlySet<T>` (return) / `ISet<T>` (mutable) | For uniqueness semantics — e.g. in-flight project IDs |
| `tuple[T, U]` | `(T First, U Second)` — named `ValueTuple` | Always name the members. `(T, U)` without names is banned. |
| `TypeVar` | Generic type parameter `<T>` | Use generic type constraints explicitly |
| `Protocol` | `interface` | See §9 |
| `NewType` | `record struct` for value-semantic wrappers | e.g. `record struct ProjectId(long Value)` |
| `Never` (no return) | `[DoesNotReturn]` on method + `void` return | Use `System.Diagnostics.CodeAnalysis.DoesNotReturn` attribute |
| `Optional[T]` | `T?` | Enabled globally by `Nullable=enable`. Every `T?` must be documented. |
| `Union[A, B]` | Custom discriminated union or `OneOf<A,B>` | No native union — see §8 for error handling pattern |
| `Final[T]` (immutable binding) | `readonly` field / `const` / `init`-only property | Choose based on what you're preventing |
| `Final[T]` (no inheritance) | `sealed class` | Concrete service implementations are always `sealed` |
| `TypeGuard[T]` | Pattern matching `x is SomeType t` | Or custom `static bool IsXxx(object? x, out T result)` predicate |
| `TypedDict` | `record` with `required init` properties | See §3 |
| `Annotated` | `[Attribute]` on the member/parameter | Standard C# attribute syntax |
| `Literal[...]` | `enum` or `const` fields or pattern matching | `enum` for sets of named values; `const` for fixed scalars |

### 2.2 — Built-in Type Aliases

Always use the C# keyword alias, never the CLR type name:

| Use | Avoid |
|---|---|
| `string` | `String` |
| `int`, `long`, `double`, `bool` | `Int32`, `Int64`, `Double`, `Boolean` |
| `object` | `Object` |
| `string?` | `Nullable<string>` |

---

## 3. Type Declaration Hierarchy

Choose the right declaration form. This is the decision tree:

```
Is it a pure data carrier with value equality semantics?
├── Yes, small (≤ 4 fields, stack-allocatable) → record struct
├── Yes, larger or heap-allocated → record
└── No → continue ↓

Does it have behavior and state?
├── Yes, is it a service / component → sealed class
├── Yes, is it a base for inheritance → abstract class (rare)
└── Is it a contract / capability → interface
```

### 3.1 — `record` — Immutable DTOs

Use `record` for data transfer objects, domain value objects, and query results that cross layer boundaries. Records have structural (value) equality and are immutable by default with `init`.

```csharp
// Correct: immutable data transfer between layers
public sealed record ScoredProject(
    long     ProjectId,
    string   Title,
    string   OwnerName,
    string?  CategoryName,
    decimal? BudgetMin,
    decimal? BudgetMax,
    double   RelevanceScore,
    string   ExplanationText
);

// Correct: domain value object with required init
public sealed record ProjectFilter
{
    public required bool   UnreadOnly       { get; init; }
    public          int?   MinBudget        { get; init; }
    public          int?   MaxBudget        { get; init; }
    public          long?  CategoryId       { get; init; }
    public          string SortField        { get; init; } = "posted_at";
    public          string SortDirection    { get; init; } = "DESC";
}
```

**Rules for `record`:**
- Always `sealed` unless an explicit inheritance hierarchy is designed.
- All properties `init`-only unless mutation is explicitly justified.
- Use positional parameters for small records (≤ 5 fields). Use property syntax for larger records.
- Never put service dependencies or mutable state in a `record`.

### 3.2 — `record struct` — Lightweight Value Types

Use `record struct` for small, frequently created value types where heap allocation is wasteful. Prefer when the type has 1–4 fields and represents a measurement, identifier, or range.

```csharp
// Correct: typed identifier — prevents mixing project_id and owner_id
public readonly record struct ProjectId(long Value);
public readonly record struct OwnerId(long Value);
public readonly record struct CategoryId(long Value);

// Correct: a score range with clear semantics
public readonly record struct BudgetRange(decimal Min, decimal Max)
{
    public bool Contains(decimal value) => value >= Min && value <= Max;
}
```

**Rules for `record struct`:**
- Always `readonly` unless mutation is specifically needed.
- Implement only simple methods directly on the struct; heavy logic goes in extension methods or services.
- Never use `record struct` for types with more than ~4 fields — use `record` instead.

### 3.3 — `sealed class` — Services and Components

Concrete implementations of services, repositories, background workers, and ViewModels are always `sealed`. This prevents accidental inheritance, enables the JIT to devirtualize calls, and makes the type's responsibility explicit.

```csharp
// Correct
public sealed class ProjectRepository : IProjectRepository
{
    private readonly SqliteConnection _connection;

    public ProjectRepository(SqliteConnection connection)
        => _connection = connection;

    public async Task<IReadOnlyList<ScoredProject>> GetFeedAsync(
        ProjectFilter filter,
        int pageSize,
        int offset,
        CancellationToken ct = default)
    { /* ... */ }
}

// Wrong — never leave a concrete implementation unsealed
public class ProjectRepository : IProjectRepository { }
```

### 3.4 — `abstract class` — Base Implementations

Use `abstract class` only when sharing concrete implementation between related subtypes. This is rare. Document the inheritance contract explicitly.

### 3.5 — `interface` — Contracts

Use `interface` for all service contracts, repository contracts, and engine contracts. See §9.

### 3.6 — `enum` — Named Sets of Values

Use `enum` for closed sets of named values. Prefer `int`-backed (default). Never assign magic numbers without associating them to the enum.

```csharp
public enum EnrichmentStatus
{
    Pending  = 0,
    Enriched = 1,
    Failed   = 2
}

public enum InteractionType
{
    Opened       = 0,
    ScrolledPast = 1
}
```

**Rules for `enum`:**
- Defined next to the type that uses it if domain-specific.
- Always include a `None = 0` member for flag-like enums and for default state.
- For status/state enums, validate at the boundary (parsing from DB strings — see §8).

---

## 4. Nullability Rules

With `Nullable=enable` and `TreatWarningsAsErrors=true`, nullability is a compile-time guarantee, not a convention.

### 4.1 — The Core Contract

| Declaration | Contract |
|---|---|
| `string name` | `name` is never null. If it can be null, the compiler will warn at every call site. |
| `string? name` | `name` can be null. Every use site must handle null. |
| `T? value` where T is a value type | Equivalent to `Nullable<T>`. |

### 4.2 — Mandatory Rules

**Never use `!` (null-forgiving operator) without a comment explaining why the analyser is wrong:**

```csharp
// WRONG — silent lie to the compiler
var name = GetName()!;

// CORRECT — explain why null is impossible in this context
// GetName() returns null only when _initialized is false.
// We checked _initialized in the constructor, so this is safe.
var name = GetName()!; // safe: _initialized is guaranteed by ctor
```

**Use pattern matching for null checks — not `== null`:**

```csharp
// CORRECT
if (category is not null) { ... }
if (owner is null) throw new ArgumentNullException(nameof(owner));

// AVOID in new code
if (category != null) { ... }
if (owner == null) throw ...
```

**Use `?.` and `??` for null propagation — not ternary null checks:**

```csharp
// CORRECT
var name = project?.Owner?.DisplayName ?? "غير معروف";

// AVOID
var name = project != null && project.Owner != null
    ? project.Owner.DisplayName
    : "غير معروف";
```

**Use `ArgumentNullException.ThrowIfNull` at public boundaries:**

```csharp
public ProjectRepository(SqliteConnection connection)
{
    ArgumentNullException.ThrowIfNull(connection);
    _connection = connection;
}
```

### 4.3 — Nullable Documentation

Every `T?` in a public API must be documented. Either:
- In the `<param>` XML comment: `<param name="categoryId">The category to filter by, or <c>null</c> to include all categories.</param>`
- Or with an inline comment on the property/field.

Do not return `null` from a method that returns a collection — return an empty collection instead:

```csharp
// CORRECT — empty collection, never null
public async Task<IReadOnlyList<ProjectSkill>> GetSkillsAsync(long projectId, CancellationToken ct = default)
    => await _db.QuerySkillsAsync(projectId, ct) ?? [];

// WRONG
public Task<IReadOnlyList<ProjectSkill>?> GetSkillsAsync(...) { }
```

---

## 5. Collection Type Selection

Use the most restrictive interface appropriate for the use case.

### 5.1 — Return Types

| Scenario | Type to return |
|---|---|
| Fixed, ordered, indexed list (materialized) | `IReadOnlyList<T>` |
| Fixed, unordered set (materialized) | `IReadOnlyCollection<T>` |
| Uniqueness set | `IReadOnlySet<T>` |
| Lazy / deferred sequence | `IEnumerable<T>` |
| Async stream (DB cursor, pipeline stage) | `IAsyncEnumerable<T>` |
| Dictionary for lookups | `IReadOnlyDictionary<TKey, TValue>` |
| Zero-allocation buffer | `ReadOnlySpan<T>` (sync only, stack-bound) or `ReadOnlyMemory<T>` |

**Never return `List<T>`, `Dictionary<K,V>`, or `HashSet<T>` from a public method.** The concrete type is an implementation detail.

### 5.2 — Parameter Types

| Scenario | Type to accept |
|---|---|
| Read-only traversal | `IEnumerable<T>` |
| Random access needed | `IReadOnlyList<T>` |
| Count needed | `IReadOnlyCollection<T>` |
| Mutable append/remove needed | `IList<T>` |
| Thread-safe read/write | `ConcurrentDictionary<TKey, TValue>` — passed by interface `IDictionary<TKey, TValue>` |
| Buffer/span params | `ReadOnlySpan<T>` (using C# 14's enhanced params / lambda support) |

### 5.3 — Implementation Type Selection

| Use case | Implementation type |
|---|---|
| Thread-safe project ID registry (DiffEngine) | `ConcurrentDictionary<long, byte>` |
| Thread-safe in-flight tracker | `ConcurrentDictionary<long, byte>` |
| Pipeline work queue | `System.Threading.Channels.Channel<T>` |
| Truly immutable after construction | `ImmutableArray<T>` or `FrozenSet<T>` / `FrozenDictionary<T>` |
| Skill affinity lookup | `FrozenDictionary<long, double>` (built once per recompute) |
| Local ad-hoc collection inside a method | `List<T>` (fine here — never escapes the method) |

### 5.4 — Collection Expressions (C# 12+, still preferred in C# 14)

Use collection expression syntax for all collection literals:

```csharp
// CORRECT — collection expression
IReadOnlyList<string> empty = [];
string[] headers = ["Content-Type", "Accept"];
var ids = new HashSet<long> { 1, 2, 3 }; // still needed for non-array types

// AVOID in C# 12+ code
var empty = new List<string>();
var headers = new string[] { "Content-Type", "Accept" };
```

### 5.5 — Spans and Memory

Use `ReadOnlySpan<char>` for Arabic text normalization (diacritics stripping, Alef folding) to avoid intermediate string allocations:

```csharp
// CORRECT — processes text without heap allocation
public static string NormalizeArabic(ReadOnlySpan<char> input)
{
    Span<char> buffer = stackalloc char[input.Length];
    // ... strip tashkeel, fold alefs ...
    return new string(buffer[..written]);
}
```

Implicit span conversions (C# 14, see §7.5) allow passing `string` where `ReadOnlySpan<char>` is expected without explicit casting.

---

## 6. Async and Concurrency

### 6.1 — CancellationToken

**Every async public method must accept a `CancellationToken` with a default value:**

```csharp
// CORRECT
Task<IReadOnlyList<Project>> GetFeedAsync(ProjectFilter filter, CancellationToken ct = default);

// WRONG — no cancellation path
Task<IReadOnlyList<Project>> GetFeedAsync(ProjectFilter filter);
```

- Name it `ct` in implementations, `cancellationToken` in public interface declarations.
- Pass it to every downstream call: DB commands, HTTP requests, channel operations.
- Never swallow `OperationCanceledException` — let it propagate.

### 6.2 — `Task<T>` vs `ValueTask<T>`

| Scenario | Return type |
|---|---|
| General async method (may or may not complete synchronously) | `Task<T>` / `Task` |
| High-frequency hot path (likely completes synchronously, e.g. cache hit) | `ValueTask<T>` / `ValueTask` |
| Fire-and-forget (extremely rare — document why) | `Task` stored and awaited later |
| Streaming result | `IAsyncEnumerable<T>` |

**Rules:**
- Do not `await` a `ValueTask` more than once — cache the result first.
- Use `Task.Run` only for CPU-bound work. Never wrap I/O in `Task.Run`.
- Use `ConfigureAwait(false)` in all library/service code (not in ViewModels or UI code).

```csharp
// Library/repository layer — ConfigureAwait(false)
public async Task<ScoredProject?> GetByIdAsync(long id, CancellationToken ct = default)
{
    await using var cmd = _connection.CreateCommand();
    cmd.CommandText = "SELECT ... FROM projects WHERE project_id = @id";
    cmd.Parameters.AddWithValue("@id", id);
    await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
    return reader.Read() ? MapToScoredProject(reader) : null;
}
```

### 6.3 — Channels

Use `System.Threading.Channels.Channel<T>` for all producer-consumer pipeline handoffs (not `BlockingCollection<T>`, not `ConcurrentQueue<T>` directly):

```csharp
// Bounded channel with backpressure
var channel = Channel.CreateBounded<EnrichmentJob>(new BoundedChannelOptions(capacity: 50)
{
    FullMode     = BoundedChannelFullMode.Wait,
    SingleReader = false,
    SingleWriter = true
});

// Producer writes
await channel.Writer.WriteAsync(job, ct);

// Consumer reads
await foreach (var job in channel.Reader.ReadAllAsync(ct))
{
    await ProcessJobAsync(job, ct);
}
```

### 6.4 — `Lock` (C# 13)

Use `System.Threading.Lock` (new type from C# 13) instead of raw `object` for `lock` statements:

```csharp
// CORRECT — C# 13 Lock type (better perf, cleaner semantics)
private readonly Lock _gate = new();

private void SafeUpdate()
{
    lock (_gate) { /* ... */ }
}

// AVOID in new code
private readonly object _lock = new();
```

---

## 7. C# 14 Feature Adoption

These features shipped in November 2025 with .NET 10. Use them actively.

### 7.1 — Extension Members (Headline Feature)

C# 14 introduces a unified `extension` block for defining extension properties, extension indexers, and static extension members — not just extension methods.

**Use for:** Adding domain-specific convenience members to types you don't own (MAUI types, `SqliteDataReader`, etc.).

```csharp
// CORRECT — C# 14 unified extension block
extension(SqliteDataReader reader)
{
    // Extension property
    public long   ProjectId => reader.GetInt64(reader.GetOrdinal("project_id"));
    public string Title     => reader.GetString(reader.GetOrdinal("title"));
    public bool   IsRead    => reader.GetInt32(reader.GetOrdinal("is_read")) == 1;

    // Extension method
    public EnrichmentStatus GetEnrichmentStatus()
        => reader.GetString(reader.GetOrdinal("enrichment_status")) switch
        {
            "pending"  => EnrichmentStatus.Pending,
            "enriched" => EnrichmentStatus.Enriched,
            "failed"   => EnrichmentStatus.Failed,
            var s      => throw new InvalidDataException($"Unknown enrichment_status: {s}")
        };
}

// Usage — reads like native members
var project = new Project(
    ProjectId : reader.ProjectId,
    Title     : reader.Title,
    IsRead    : reader.IsRead,
    Status    : reader.GetEnrichmentStatus()
);
```

**Convention:** Extension blocks live in a file named `{TypeName}Extensions.cs` in the same namespace as the types they extend.

### 7.2 — `field` Keyword

Simplifies properties that need custom accessor logic while keeping the compiler-generated backing store:

```csharp
// CORRECT — C# 14 field keyword (no explicit backing field needed)
public string Title
{
    get;
    set
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Title));
        field = value.Trim();
        OnPropertyChanged();
    }
}

// Old way — still valid but verbose
private string _title = "";
public string Title
{
    get => _title;
    set { ArgumentException.ThrowIfNullOrWhiteSpace(value); _title = value.Trim(); OnPropertyChanged(); }
}
```

**Use in ViewModels** for properties where `[ObservableProperty]` source generation is insufficient (validation, side effects on set).

### 7.3 — Null-Conditional Assignment

Allows safe assignment through a null-conditional access chain — the assignment is skipped when the target is null:

```csharp
// CORRECT — C# 14: no-op if _owner is null
_owner?.LastSeenAt = DateTimeOffset.UtcNow;

// Old way required an explicit null guard
if (_owner is not null)
    _owner.LastSeenAt = DateTimeOffset.UtcNow;
```

**Use for:** Optional lazy-init targets, optional UI element updates, optional cache updates.

### 7.4 — `nameof` with Unbound Generic Types

```csharp
// CORRECT — C# 14
_logger.LogError("Failed to deserialize {Type}", nameof(List<>));
throw new InvalidOperationException($"Cannot serialize {nameof(IReadOnlyDictionary<,>)}");

// Old way — required typeof().Name at runtime or a string literal
```

**Use in:** Exception messages, log statements, and diagnostic strings involving generic types.

### 7.5 — Implicit Span Conversions

`string` now implicitly converts to `ReadOnlySpan<char>`, and arrays implicitly convert to `ReadOnlySpan<T>` / `Span<T>`:

```csharp
// CORRECT — C# 14: string passed directly, no .AsSpan() needed
var normalized = NormalizeArabic(rawTitle);   // NormalizeArabic(ReadOnlySpan<char>)

// Old way
var normalized = NormalizeArabic(rawTitle.AsSpan());
```

**Use in:** Arabic text normalization utilities, all buffer-processing helpers.

### 7.6 — Partial Constructors and Partial Events

Source generators (CommunityToolkit.Mvvm 8.4.2) use partial members. C# 14 extends this to constructors and events:

```csharp
// Generator fills the constructor body; we declare intent only
public partial class ProjectsViewModel : ObservableObject
{
    public partial ProjectsViewModel();
}
```

Primarily used through CommunityToolkit source generation — not written manually.

### 7.7 — Simple Lambda Parameter Modifiers

Lambda parameters can use `ref`, `out`, `in`, `scoped` without explicit type declarations:

```csharp
// CORRECT — C# 14: modifier without explicit type
Span<int> data = [1, 2, 3];
data.ForEach((ref item) => item *= 2);
```

**Use when:** Working with value-type spans in performance-sensitive paths.

### 7.8 — Previously Introduced (C# 12–13), Still Actively Use

| Feature | Where we use it |
|---|---|
| Primary constructors on classes | Simple services with 1–2 injected dependencies |
| Collection expressions `[1, 2, 3]` | All collection literals (see §5.4) |
| `params ReadOnlySpan<T>` (C# 13) | Text normalization and small buffer helpers |
| `System.Threading.Lock` (C# 13) | All `lock` usages (see §6.4) |
| `required` init properties | Records and DTOs (see §3.1) |
| Pattern matching (`switch` expressions, `is` patterns) | Enum mapping, null handling, status parsing |
| `is not null` / `is null` | All null checks (see §4.2) |

---

## 8. Error Handling

### 8.1 — The Three Contracts: Throw, Result, Neither

Every function in this codebase has exactly **one** error-handling contract. Callers must know — without reading the implementation — which contract a function follows. Mixing contracts in a single function is always wrong.

| Contract | Return type signals it | When to use |
|---|---|---|
| **Throw** | `T` (not wrapped) | Programming bugs, invariant violations, impossible states. The caller either catches or lets it propagate. |
| **Result** | `Result<T>` / `Task<Result<T>>` | Expected business failures: network errors, parse errors, validation failures, "not found" on write operations. The function **never** throws for these cases. |
| **Neither** | `void` / `Task` (and the doc says so) | Non-critical fire-and-forget. Logging, analytics, interaction tracking. Failures are swallowed internally. |

The contract is part of the function's API. Callers must never guess which mechanism to handle.

### 8.2 — Expressing Each Contract in C#

**Throw contract — return type is `T`, doc has `<exception>` tags:**

```csharp
/// <summary>Parses application configuration from JSON.</summary>
/// <exception cref="ArgumentException">Thrown when <paramref name="json"/> is null or empty.</exception>
/// <exception cref="JsonException">Thrown when the JSON is malformed or cannot be deserialized.</exception>
public static AppConfig ParseConfig(string json)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(json, nameof(json));
    return JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig)
        ?? throw new InvalidDataException("Config deserialized to null");
}
```

**Result contract — return type is `Result<T>`, doc has `<returns>` describing Err cases. Errors are created exclusively via the module's `Errors.cs` factory:**

```csharp
/// <summary>Fetches and parses a project detail page.</summary>
/// <returns>
/// <see cref="Result{T}.Ok"/> with the parsed details on success.
/// <see cref="Result{T}.Err"/> if the HTTP request fails or the HTML cannot be parsed.
/// Never throws for these expected failure cases. Error details accessible via
/// <see cref="DomainError.InternalMessage"/> (log) and <see cref="DomainError.ExternalMessage"/> (UI).
/// </returns>
public async Task<Result<ProjectDetails>> FetchDetailAsync(string url, CancellationToken ct)
{
    try
    {
        var html    = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        var details = _parser.ParseDetail(html);
        return new Result<ProjectDetails>.Ok(details);
    }
    catch (HttpRequestException ex)
    {
        // Factory from HttpErrors.cs — carries Code, InternalMessage, ExternalMessage, FixMessage
        return Result<ProjectDetails>.Fail(
            HttpErrors.RequestFailed(url, ex.Message, ex));
    }
    catch (ParseException ex)
    {
        return Result<ProjectDetails>.Fail(
            HttpErrors.ParseFailed(url, ".project-detail", ex.Message, ex));
    }
    // OperationCanceledException is intentionally NOT caught — it always propagates
}
```

**Neither contract — return type is `Task`, doc explicitly states no failure reporting:**

```csharp
/// <summary>Records that the user scrolled past a project without opening it.</summary>
/// <remarks>
/// This method uses the Neither error contract — it never throws and never reports failure.
/// If the interaction cannot be persisted (e.g. database error), the failure is logged
/// and silently discarded. Interaction tracking is non-critical.
/// </remarks>
public async Task RecordScrolledPastAsync(long projectId, CancellationToken ct = default)
{
    try
    {
        await _db.InsertInteractionAsync(projectId, InteractionType.ScrolledPast, ct)
                 .ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _logger.LogWarning(ex, "Failed to record scrolled_past for project {Id}", projectId);
    }
}
```

### 8.3 — Three-Level Contract Consistency

**Level 1 — Every Function (required)**

Each function has one contract. The return type makes it visible. A function that returns `Result<T>` must NEVER also throw for expected failures.

**Level 2 — Every Module (required)**

All functions in a module follow similar contracts. A module where some methods throw and others return Result forces callers to remember each method individually.

```csharp
// CORRECT — consistent module: all business operations return Result<T>
public interface IEnrichmentService
{
    Task<Result<ProjectDetails>>    FetchDetailAsync(string url,  CancellationToken ct = default);
    Task<Result<IReadOnlyList<string>>> ParseSkillsAsync(string html, CancellationToken ct = default);
    Task<Result<BudgetRange?>>      ParseBudgetAsync(string html, CancellationToken ct = default);
}
// Callers know: this module never throws for expected failures.

// WRONG — inconsistent: caller must both catch AND check Result
public interface IEnrichmentService
{
    Task<Result<ProjectDetails>> FetchDetailAsync(string url, CancellationToken ct = default);
    Task<ProjectDetails>         ParseDetailAsync(string html); // throws on parse error!
}
```

**Level 3 — System-Wide Layer Conventions (required)**

The layer architecture defines the default contract for each layer:

| Layer | Default contract | Reasoning |
|---|---|---|
| **Infrastructure** — Repositories (DB) | Throw (`SqliteException`) for unexpected failures; `T?` / `bool` for "not found" | DB being offline is genuinely exceptional |
| **Infrastructure** — HTTP scrapers | Throw (`HttpRequestException`) | HTTP failures are exceptional at the raw client level |
| **Application** — Services, Engines | **Result** for all business operations that can predictably fail | Services are the translation boundary: they catch Infrastructure throws and return `Result.Err` |
| **Application** — Interaction Trackers | **Neither** | Fire-and-forget; failure is genuinely non-critical |
| **Application** — Loggers | **Neither** | Logging never propagates failure |
| **Domain** — Validators, Parsers in Core | Throw for programming-contract violations; Result for expected parse failures | Depends on what "failure" means |
| **ViewModel** | Catches `Result.Err`; surfaces to UI observable state; never lets exceptions escape to View | |
| **View / UI** | Never throws; exceptions reaching here are programming bugs | |

**The boundary rule:** Application services are the bridge between Infrastructure (which may throw) and the rest of the system (which expects Results). An Application service always wraps Infrastructure calls and converts exceptions into `DomainError` values via the module's `Errors.cs` factory:

```csharp
// Application service: converts Infrastructure throws → Result<T> via PipelineErrors.cs factory
public async Task<Result<ProjectDetails>> EnrichAsync(long projectId, CancellationToken ct)
{
    try
    {
        // Infrastructure (repository) may throw SqliteException
        var project = await _repo.GetAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
            return Result<ProjectDetails>.Fail(
                PipelineErrors.EnrichmentFailed(projectId, "project row not found"));

        // Infrastructure (HTTP client) may throw HttpRequestException
        var html    = await _http.GetStringAsync(project.Url, ct).ConfigureAwait(false);
        var details = _parser.Parse(html);
        return new Result<ProjectDetails>.Ok(details);
    }
    catch (SqliteException ex)
    {
        return Result<ProjectDetails>.Fail(
            PipelineErrors.EnrichmentFailed(projectId, ex.Message, ex));
    }
    catch (HttpRequestException ex)
    {
        return Result<ProjectDetails>.Fail(
            PipelineErrors.EnrichmentFailed(projectId, $"network error: {ex.Message}", ex));
    }
    // OperationCanceledException is NOT caught — it always propagates upward
}
```

### 8.4 — Anti-Pattern: Mixing Contracts

```csharp
// WRONG — caller must both check Result AND catch exceptions
public async Task<Result<User>> LoginAsync(string email, string password)
{
    if (wrongPassword)
        return Result<User>.Fail(SomeErrors.InvalidCredentials());  // Result

    if (!_dbConnected)
        throw new InvalidOperationException("DB down");   // Throw — MIXED!

    return new Result<User>.Ok(user);
}

// CORRECT — all failures go through the module's Errors.cs factory, no mixing
public async Task<Result<User>> LoginAsync(string email, string password, CancellationToken ct)
{
    try
    {
        if (!await _auth.VerifyAsync(email, password, ct).ConfigureAwait(false))
            return Result<User>.Fail(AuthErrors.InvalidCredentials());

        var user = await _repo.FindByEmailAsync(email, ct).ConfigureAwait(false);
        return user is not null
            ? new Result<User>.Ok(user)
            : Result<User>.Fail(AuthErrors.UserNotFoundAfterAuth(email));
    }
    catch (SqliteException ex)
    {
        return Result<User>.Fail(AuthErrors.DatabaseUnavailable(ex.Message, ex));
    }
    // OperationCanceledException propagates
}
```

### 8.5 — `Result<T>` and `DomainError` Implementation

> **Canonical Reference:** See [`errors-handling.md §3.0`](./errors-handling.md) for the full `DomainError` and `Result<T>` implementation, as well as the rules for `Errors.cs` module factories.

`Result<T>` embeds a `DomainError` carrying all four fields (Code, InternalMessage, ExternalMessage, FixMessage, and Cause). Defined once in `MostaqlK.Core`. **Do not use an external Result library.**

```csharp
// Consuming — always switch on the full DomainError; log Internal, surface External
var result = await _enrichmentService.FetchDetailAsync(url, ct);

switch (result)
{
    case Result<ProjectDetails>.Ok ok:
        await _repo.SaveDetailsAsync(ok.Value, ct);
        break;

    case Result<ProjectDetails>.Err err:
        // InternalMessage → developer log (always; includes code, context, cause)
        _logger.LogWarning(err.Error.Cause,
            "Error {Code}: {InternalMessage}",
            err.Error.Code, err.Error.InternalMessage);

        // ExternalMessage + FixMessage → UI (see §8.8)
        await _repo.MarkFailedAsync(projectId, ct);
        break;
}
```

### 8.6 — Per-Module Contract Reference (This Codebase)

| Interface / Method | Contract | Why |
|---|---|---|
| `IProjectRepository.GetFeedAsync` | Throw (`SqliteException`) | Unexpected — DB must be working for the app to function |
| `IProjectRepository.TryInsertAsync` | Throw + `bool` | `INSERT OR IGNORE` returns bool; unexpected DB error throws |
| `IProjectRepository.GetDetailAsync` | Throw + `T?` null | `T?` null = not found (Neither-like for the not-found case) |
| `IEnrichmentService.FetchDetailAsync` | **Result** | Network/parse failure is expected and recoverable |
| `IPollService.RunPollAsync` | **Result** | Listing fetch failure is expected |
| `IInteractionTracker.*` | **Neither** | Tracking failure is non-critical |
| `IRecommendationEngine.RecomputeAsync` | **Result** | Score computation may fail if interaction data is inconsistent |
| `ILogger<T>.*` | **Neither** | Logging never propagates failure |

### 8.7 — Enum Boundary Parsing

When parsing an enum from a DB string or HTTP value, always use a `switch` expression with an explicit default arm. This is the Throw contract for boundary parsing — invalid values are programming errors:

```csharp
public static EnrichmentStatus ParseStatus(string raw) => raw switch
{
    "pending"  => EnrichmentStatus.Pending,
    "enriched" => EnrichmentStatus.Enriched,
    "failed"   => EnrichmentStatus.Failed,
    _          => throw new ArgumentOutOfRangeException(nameof(raw), raw,
                      "Unknown enrichment_status value in database")
};
```

**Never use `Enum.Parse` / `Enum.TryParse`** for values coming from the database or HTTP responses — they don't enforce the exact string contract and silently accept values outside the known set.

---

### 8.8 — UI Binding of `ExternalMessage` and `FixMessage`

Every `Result<T>.Err` carries two user-facing strings. The ViewModel reads them from `DomainError` and exposes them as observable properties. The View binds to those properties.

**ViewModel — consume the Result, expose the error fields:**

```csharp
// MostaqlK/ViewModels/ProjectsViewModel.cs
public partial class ProjectsViewModel : ObservableObject
{
    // ── Error state ──────────────────────────────────────────────────────────────

    /// <summary>Arabic user-facing error message. Null when no error.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Optional Arabic fix hint. Null when no error or error is self-healing.</summary>
    [ObservableProperty]
    private string? _fixSuggestion;

    /// <summary>True when an error banner should be visible.</summary>
    [ObservableProperty]
    private bool _hasError;

    // ── Commands ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadFeedAsync(CancellationToken ct)
    {
        HasError      = false;
        ErrorMessage  = null;
        FixSuggestion = null;

        var result = await _projectService.GetFeedAsync(_filter, ct);

        switch (result)
        {
            case Result<IReadOnlyList<ProjectSummary>>.Ok ok:
                Projects = new ObservableCollection<ProjectSummary>(ok.Value);
                break;

            case Result<IReadOnlyList<ProjectSummary>>.Err err:
                // InternalMessage → developer log ONLY
                _logger.LogWarning(err.Error.Cause,
                    "Error {Code}: {InternalMessage}",
                    err.Error.Code, err.Error.InternalMessage);

                // ExternalMessage + FixMessage → observable state → View
                ErrorMessage  = err.Error.ExternalMessage;   // never null
                FixSuggestion = err.Error.FixMessage;         // may be null
                HasError      = true;
                break;
        }
    }
}
```

**View (XAML) — bind directly to the observable properties:**

```xml
<!-- MostaqlK/Views/ProjectsPage.xaml -->
<!-- Error banner: visible only when HasError is true -->
<Border
    IsVisible="{Binding HasError}"
    BackgroundColor="{StaticResource ErrorSurface}"
    Padding="16,12"
    Margin="0,0,0,8">

    <VerticalStackLayout Spacing="4">

        <!-- ExternalMessage: always present in the DomainError -->
        <Label
            Text="{Binding ErrorMessage}"
            Style="{StaticResource ErrorLabel}"
            HorizontalTextAlignment="End" />

        <!-- FixMessage: optional — hide the label when null/empty -->
        <Label
            Text="{Binding FixSuggestion}"
            Style="{StaticResource FixHintLabel}"
            HorizontalTextAlignment="End"
            IsVisible="{Binding FixSuggestion,
                        Converter={StaticResource NotNullOrEmptyConverter}}" />

    </VerticalStackLayout>
</Border>
```

**Rules for UI error binding:**

| Rule | Enforcement |
|---|---|
| Only `ExternalMessage` is shown to users | Never bind `InternalMessage` to any UI element |
| `FixMessage` binding must handle `null` | Use `NotNullOrEmptyConverter` or `IsVisible` guard |
| `HasError` resets to `false` before every command | Clear stale errors before new operation starts |
| Log `InternalMessage` + `Code` before touching UI state | Ensures the developer record is written even if the UI update throws |

---

## 9. Interfaces and Dependency Injection

### 9.1 — Interface Contract Rules

- Every service, repository, engine, and tracker has a corresponding `interface`.
- Interface name: `I` prefix + noun/noun-phrase (`IProjectRepository`, `IInteractionTracker`).
- Interfaces live in `MostaqlK.Core` or `MostaqlK.Abstractions` — never in the implementation project.
- Concrete implementations live in `MostaqlK.Infrastructure` or `MostaqlK.Services`.
- Concrete implementations are always `sealed`.

```csharp
// In MostaqlK.Core (no implementation details)
public interface IProjectRepository
{
    /// <summary>Returns a paginated feed of projects matching the filter.</summary>
    Task<IReadOnlyList<ProjectSummary>> GetFeedAsync(
        ProjectFilter     filter,
        int               pageSize,
        int               offset,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the full project with details, or <c>null</c> if not found.</summary>
    Task<ProjectDetail?> GetDetailAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new project. No-op if the project already exists (INSERT OR IGNORE).</summary>
    /// <returns><c>true</c> if newly inserted; <c>false</c> if already existed.</returns>
    Task<bool> TryInsertAsync(NewProject project, CancellationToken cancellationToken = default);
}
```

### 9.2 — DI Registration

All registrations go in extension methods named `Add{Feature}Services`:

```csharp
public static class DatabaseServiceExtensions
{
    public static IServiceCollection AddDatabaseServices(
        this IServiceCollection services,
        string                  databasePath)
    {
        services.AddSingleton<SqliteConnection>(_ =>
        {
            var conn = new SqliteConnection($"Data Source={databasePath}");
            conn.Open();
            return conn;
        });

        services.AddSingleton<IProjectRepository,    ProjectRepository>();
        services.AddSingleton<IOwnerRepository,      OwnerRepository>();
        services.AddSingleton<IInteractionTracker,   InteractionTracker>();
        services.AddSingleton<IRecommendationEngine, RecommendationEngine>();

        return services;
    }
}
```

### 9.3 — Lifetime Rules

| Type | Lifetime | Reason |
|---|---|---|
| `SqliteConnection` | `Singleton` | WAL allows concurrent reads; writes serialized with `Lock` |
| Repositories | `Singleton` | Stateless over the shared connection |
| `IRecommendationEngine` | `Singleton` | Holds in-memory `PreferenceProfile` cache |
| `IInteractionTracker` | `Singleton` | Shares the connection |
| ViewModels | `Transient` | One per page navigation, fresh each time |
| `IHttpClientFactory` / `HttpClient` | Via `AddHttpClient<T>` | Never `new HttpClient()` directly |

---

## 10. LINQ

### 10.1 — Use LINQ for

- Transforming in-memory collections (`.Select`, `.Where`, `.OrderBy`, `.GroupBy`)
- Building parameter lists before handing off to SQL
- Simple aggregations (`.Sum`, `.Max`, `.Count`, `.Any`, `.All`)

```csharp
// CORRECT — LINQ for in-memory transformation
var skillIds = rawSkills
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Select(s => s.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Select(s => _skillLookup[s])
    .ToList();
```

### 10.2 — Do Not Use LINQ for

- **Querying the database** — use raw parameterized SQL (see `sql-conventions.md`)
- **Side-effectful loops** — use `foreach` (`.ForEach` is banned)
- **Chains longer than 5 operators** — break into named intermediate variables

```csharp
// AVOID — ForEach with side effects
projects.Where(p => p.IsUnread).ToList().ForEach(p => Track(p.Id));  // NO

// CORRECT
var unread = projects.Where(p => p.IsUnread).ToList();
foreach (var p in unread)
    await _tracker.RecordAsync(p.Id, ct);
```

### 10.3 — Deferred vs. Materialized

Know when LINQ is deferred vs. materialized:

```csharp
// CORRECT — materialize immediately when passing to multiple consumers
var ids = newProjects.Select(p => p.ProjectId).ToHashSet();

// AVOID — deferred sequence may enumerate the source twice
var ids = newProjects.Select(p => p.ProjectId);  // still deferred!
```

---

## 11. Approved NuGet Packages

The table below lists all approved packages and their versions (August 2026). **Always verify the latest stable version on nuget.org before adding.** Never use beta or preview packages in production paths.

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Data.Sqlite` | **10.0.10** | SQLite ADO.NET provider — bundles native SQLite binary |
| `CommunityToolkit.Mvvm` | **8.4.2** | MVVM source generation (`[ObservableProperty]`, `[RelayCommand]`, etc.) |
| `HtmlAgilityPack` | **1.12.4** | HTML parsing for project detail pages |
| `Microsoft.Extensions.Resilience` | latest stable | Resilience pipelines (retry, timeout, circuit breaker) |
| `Microsoft.Extensions.Http.Resilience` | latest stable | `IHttpClientFactory` + resilience — replaces deprecated `Microsoft.Extensions.Http.Polly` |
| `FuzzySharp` | latest stable | Levenshtein-distance fuzzy re-ranking of FTS5 candidates |
| `Microsoft.Extensions.Hosting` | **10.0.x** | Generic Host (`IHost`, `IHostedService`, `BackgroundService`) |
| `Microsoft.Extensions.DependencyInjection` | **10.0.x** | DI container |
| `Microsoft.Extensions.Logging` | **10.0.x** | `ILogger<T>` abstraction |

> **Deprecated — do not use:**
> - `Microsoft.Extensions.Http.Polly` — replaced by `Microsoft.Extensions.Http.Resilience`
> - `System.Data.SQLite` — legacy; use `Microsoft.Data.Sqlite`
> - `Newtonsoft.Json` — use `System.Text.Json` (in-box since .NET Core 3)
> - `AutoMapper` — use explicit `record` constructors or manual mapping methods

### 11.1 — HTTP Resilience Configuration

```csharp
// CORRECT — Microsoft.Extensions.Http.Resilience
services.AddHttpClient<IMostaqlClient, MostaqlHttpClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        // Rate limiter is layered separately via the WorkerPool / TokenBucketRateLimiter
    });

// AVOID — raw Polly direct (deprecated path)
services.AddHttpClient<...>()
    .AddPolicyHandler(Policy.Handle<HttpRequestException>().RetryAsync(3));
```

### 11.2 — JSON (System.Text.Json, source-generated)

Use `System.Text.Json` with source generation for AOT compatibility:

```csharp
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(IReadOnlyList<NotificationRecord>))]
internal sealed partial class AppJsonContext : JsonSerializerContext { }

// Usage
var settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings);
```

---

## 12. XML Documentation

### 12.1 — Required on

- All `interface` members (every method, property, event)
- All `public` and `internal` type declarations
- All `public` constructors that accept parameters
- All `T?` parameters — explaining what null means in context
- All `enum` values

### 12.2 — Documentation Tied to the Error Contract

The XML documentation of every function must reflect its error contract from §8:

**Throw contract — must have `<exception>` tags:**

```csharp
/// <summary>Parses the enrichment status from a database string value.</summary>
/// <param name="raw">The raw string from the <c>enrichment_status</c> column.</param>
/// <returns>The corresponding <see cref="EnrichmentStatus"/> enum value.</returns>
/// <exception cref="ArgumentOutOfRangeException">
/// Thrown when <paramref name="raw"/> is not a known status string.
/// This indicates a database corruption or schema mismatch — a programming error.
/// </exception>
public static EnrichmentStatus ParseStatus(string raw);
```

**Result contract — `<returns>` must describe both Ok and Err cases, and explicitly state it never throws:**

```csharp
/// <summary>Fetches and parses a project detail page from Mostaql.</summary>
/// <param name="url">The canonical project URL to fetch.</param>
/// <param name="cancellationToken">Cancellation token propagated from the caller.</param>
/// <returns>
/// <see cref="Result{T}.Ok"/> containing the parsed <see cref="ProjectDetails"/> on success.<br/>
/// <see cref="Result{T}.Err"/> if the HTTP request fails (network error, non-2xx status),
/// or if the HTML cannot be parsed. This method never throws for these expected failures.
/// </returns>
Task<Result<ProjectDetails>> FetchDetailAsync(string url, CancellationToken cancellationToken = default);
```

**Neither contract — `<remarks>` must explicitly state the non-critical, swallowed-failure behavior:**

```csharp
/// <summary>Records that the user scrolled past a project without opening it.</summary>
/// <param name="projectId">The ID of the project that was scrolled past.</param>
/// <param name="cancellationToken">Cancellation token propagated from the caller.</param>
/// <remarks>
/// Uses the <b>Neither</b> error contract. This method never throws and never reports failure.
/// If the interaction cannot be persisted, the failure is logged at Warning level and discarded.
/// Interaction tracking is non-critical and must not disrupt the user experience.
/// </remarks>
Task RecordScrolledPastAsync(long projectId, CancellationToken cancellationToken = default);
```

### 12.3 — Full Documentation Format

```csharp
/// <summary>
/// Inserts a newly discovered project into the database.
/// Uses <c>INSERT OR IGNORE</c> — silent no-op if the project already exists.
/// </summary>
/// <param name="project">The project parsed from the listing card. Must not be null.</param>
/// <param name="cancellationToken">Cancellation token propagated from the caller.</param>
/// <returns>
/// <c>true</c> if the row was newly inserted;
/// <c>false</c> if the project already existed (idempotent).
/// </returns>
/// <exception cref="SqliteException">
/// Thrown if the INSERT fails for any reason other than a primary-key uniqueness conflict.
/// This indicates an unexpected infrastructure failure.
/// </exception>
/// <remarks>
/// This is the only INSERT operation on the <c>projects</c> table.
/// The no-update policy means rows are never modified once committed.
/// See <c>docs/sql-conventions.md §7</c>.
/// </remarks>
Task<bool> TryInsertAsync(NewProject project, CancellationToken cancellationToken = default);
```

### 12.4 — `<example>` for Complex APIs

For non-obvious APIs (especially Result-returning ones), add an `<example>` block showing the consumption pattern:

```csharp
/// <example>
/// <code>
/// var result = await enrichmentService.FetchDetailAsync(project.Url, ct);
/// switch (result)
/// {
///     case Result&lt;ProjectDetails&gt;.Ok ok:
///         // use ok.Value
///         break;
///     case Result&lt;ProjectDetails&gt;.Err err:
///         logger.LogWarning("Failed: {Reason}", err.Reason);
///         break;
/// }
/// </code>
/// </example>
```

### 12.5 — `<remarks>` for Context

Use `<remarks>` for cross-references, thread safety notes, and policy references:

```csharp
/// <remarks>
/// This method is thread-safe. The underlying <see cref="SqliteConnection"/> is
/// protected by a <see cref="System.Threading.Lock"/> for write operations.
/// Concurrent reads proceed without locking (WAL mode).
/// </remarks>
```

---

## 13. Naming Conventions

Project-specific rules that supplement the standard C# naming guidelines.

| Element | Convention | Example |
|---|---|---|
| Async method | `{Verb}Async` suffix | `GetFeedAsync`, `InsertAsync`, `RecomputeAsync` |
| `CancellationToken` parameter | `ct` (implementations), `cancellationToken` (interface declarations) | |
| Private fields | `_camelCase` with underscore prefix | `_connection`, `_logger`, `_gate` |
| `interface` | `I` prefix + noun | `IProjectRepository`, `IRecommendationEngine` |
| `record` DTO | PascalCase noun/noun-phrase | `ScoredProject`, `ProjectFilter`, `NewProject` |
| `record struct` typed ID | PascalCase + `Id` suffix | `ProjectId`, `OwnerId`, `CategoryId` |
| `enum` | PascalCase; members PascalCase | `EnrichmentStatus.Enriched` |
| Constants | PascalCase (both `public` and `private const`) | `MaxRetryAttempts`, `DefaultPageSize` |
| Generic type parameters | `T`, or `T` + noun for clarity | `TKey`, `TResult`, `TEntity` |
| Unnamed `ValueTuple` members | **Banned** — always name them | `(long ProjectId, string Title)` not `(long, string)` |
| Extension blocks (C# 14) | File: `{TypeName}Extensions.cs` | `SqliteDataReaderExtensions.cs` |
| Repository classes | `{Entity}Repository` | `ProjectRepository`, `OwnerRepository` |
| Service classes | `{Domain}Service` | `EnrichmentService`, `PollService` |
| Engine classes | `{Domain}Engine` | `RecommendationEngine`, `DiffEngine` |
| ViewModel classes | `{Screen}ViewModel` | `ProjectsViewModel`, `SettingsViewModel` |
| Background workers | `{Domain}Worker` | `EnrichmentWorker`, `PollWorker` |
| Extension block files | `{TypeName}Extensions.cs` | `SqliteDataReaderExtensions.cs`, `DateTimeOffsetExtensions.cs` |

---

## 14. Standard Library First

Before adding any NuGet package, exhaustively check what .NET 10's standard library already provides. A package has a cost: version conflicts, supply-chain risk, update overhead. If the STD library covers the need — even partially — prefer it.

### 14.1 — Decision Flowchart

```
Does System.* or Microsoft.* (in-box with .NET 10) provide this?
└── Yes → Use it. Stop here.

Can it be implemented in < 50 lines without cleverness?
└── Yes → Implement it in Core/Shared. Stop here.

Is there an approved package in §11?
└── Yes → Use that package. Stop here.

New package needed → discuss, document the choice, update §11.
```

### 14.2 — What STD Covers (use these, not packages)

| Need | Standard library solution |
|---|---|
| Async producer-consumer pipeline | `System.Threading.Channels.Channel<T>` |
| Rate limiting (token bucket, fixed window) | `System.Threading.RateLimiting.TokenBucketRateLimiter` |
| Thread-safe dictionary | `System.Collections.Concurrent.ConcurrentDictionary<K,V>` |
| Thread-safe uniqueness set | `ConcurrentDictionary<T, byte>` with dummy byte values |
| Frozen (read-after-build) lookups | `System.Collections.Frozen.FrozenDictionary<K,V>` / `FrozenSet<T>` |
| JSON serialize / deserialize | `System.Text.Json.JsonSerializer` with source generation |
| Regex (compiled, AOT-friendly) | `System.Text.RegularExpressions.Regex` — use `[GeneratedRegex]` |
| HTTP client + retry / timeout | `System.Net.Http.HttpClient` via `IHttpClientFactory` |
| Logging abstraction | `Microsoft.Extensions.Logging.ILogger<T>` (no extra package needed) |
| Periodic background work | `System.Threading.PeriodicTimer` (not `System.Timers.Timer`) |
| Immutable array | `System.Collections.Immutable.ImmutableArray<T>` |
| Span / buffer operations | `System.Span<T>`, `System.ReadOnlySpan<T>`, `System.Memory<T>` |
| Weak event references | `System.WeakReference<T>` |
| Unicode / Arabic normalization | `System.Text.NormalizationForm` + `System.Globalization.StringInfo` |

### 14.3 — When STD Is Not Enough (approved packages)

| Need | Why STD is insufficient | Approved package |
|---|---|---|
| HTML DOM parsing | No built-in HTML parser | `HtmlAgilityPack 1.12.4` |
| SQLite access | No bundled SQLite native binary in-box | `Microsoft.Data.Sqlite 10.0.10` |
| Edit-distance fuzzy matching | No in-box Levenshtein implementation | `FuzzySharp` (latest stable) |
| MVVM source generation | No in-box `[ObservableProperty]` / `[RelayCommand]` | `CommunityToolkit.Mvvm 8.4.2` |
| HTTP resilience pipelines | `IHttpClientFactory` alone has no built-in retry / circuit breaker | `Microsoft.Extensions.Http.Resilience` |

---

## 15. Design Patterns for Extensibility

The patterns in this section are tools to reach for when the codebase genuinely benefits from the structure. They are not mandates applied everywhere. Over-engineering is as harmful as under-engineering.

### 15.1 — The Core Principle

**Program to interfaces, not implementations.** This is already enforced in §9 — every service, engine, repository, and tracker has an interface and is registered through DI. This single practice is the most powerful extensibility tool available. The patterns below address specific scenarios on top of that foundation.

### 15.2 — Multiple Implementations of One Interface

When a feature will have different implementations (now or planned), name the implementations descriptively:

```csharp
// The interface defines the contract — it never changes
public interface IRecommendationEngine
{
    bool IsWarmedUp { get; }
    Task RecomputeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ScoredProject>> GetTopAsync(int count, CancellationToken ct = default);
}

// v1 — weighted feature scoring (pure .NET math)
public sealed class WeightedScoringEngine : IRecommendationEngine { ... }

// v2 — ML model (future, via ML.NET)
public sealed class MlRecommendationEngine : IRecommendationEngine { ... }
```

Swapping the implementation is a one-line DI registration change. No callers change.

**Naming convention:** When multiple implementations exist, use a descriptive prefix that names the technique (`WeightedScoring`, `Ml`, `Cached`, `Stub`, `Fake`) — not generic names like `Default` or `New`.

### 15.3 — Factory Pattern: Implementation Chosen at Runtime

Use a Factory when the concrete implementation depends on a runtime configuration value:

```csharp
public static class RecommendationEngineFactory
{
    /// <summary>Creates the appropriate engine based on runtime configuration.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for unknown engine modes.</exception>
    public static IRecommendationEngine Create(
        RecommendationConfig config,
        IServiceProvider     services)
    => config.Mode switch
    {
        RecommendationMode.WeightedScoring => services.GetRequiredService<WeightedScoringEngine>(),
        RecommendationMode.MlModel         => services.GetRequiredService<MlRecommendationEngine>(),
        _ => throw new ArgumentOutOfRangeException(nameof(config.Mode), config.Mode,
                 "Unknown recommendation mode")
    };
}
```

**When to use:** The implementation choice is a runtime/config decision, not compile-time.
**When NOT to use:** There's only one implementation and no documented plan for multiple. A Factory here is premature abstraction.

### 15.4 — Decorator Pattern: Cross-Cutting Concerns

Use Decorator to add logging, caching, or metrics to an existing implementation without modifying it:

```csharp
// Adds structured logging to any IProjectRepository without touching the real implementation
public sealed class LoggingProjectRepository : IProjectRepository
{
    private readonly IProjectRepository                _inner;
    private readonly ILogger<LoggingProjectRepository> _logger;

    public LoggingProjectRepository(
        IProjectRepository                inner,
        ILogger<LoggingProjectRepository> logger)
    {
        _inner  = inner;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProjectSummary>> GetFeedAsync(
        ProjectFilter filter, int pageSize, int offset, CancellationToken ct = default)
    {
        _logger.LogDebug("GetFeedAsync pageSize={PageSize} offset={Offset}", pageSize, offset);
        var result = await _inner.GetFeedAsync(filter, pageSize, offset, ct).ConfigureAwait(false);
        _logger.LogDebug("GetFeedAsync returned {Count} items", result.Count);
        return result;
    }
    // ... forward remaining interface members to _inner
}
```

**When to use:** Cross-cutting behavior (logging, caching) that shouldn't pollute the core implementation, and applies to multiple implementations or scenarios.

### 15.5 — Don't Over-Engineer

| Apply a pattern when | Skip the pattern when |
|---|---|
| Future variations are documented or planned | "Maybe someday" with no real plan |
| Cross-cutting concern applies to 3+ components | Adding behavior to just one implementation |
| Testing requires substituting a dependency | The logic is a pure stateless transformation |
| DI wiring makes the pattern natural | A simple static utility is simpler and sufficient |

---

## 16. Code Organization: Local Functions and Private Helpers

### 16.1 — The Decision Hierarchy

When a chunk of logic needs a name for readability, choose the right scope:

```
Is this logic only used in one specific method?
└── Yes → Local function (defined inside that method)

Is this logic shared by 2+ methods in the same class?
└── Yes → Private static method in the same class

Is this logic shared by 2+ classes in the same layer?
└── Yes → Internal static utility class or extension method

Is this logic shared across the whole codebase?
└── Yes → Public extension method or helper in MostaqlK.Core
```

### 16.2 — Local Functions

C# supports local functions — functions defined inside a method body. Use them to break a long-but-single-purpose method into readable named steps without exposing the sub-steps to the rest of the class.

```csharp
public IReadOnlyList<ScoredProject> ScoreAll(
    IReadOnlyList<CandidateProject> candidates,
    PreferenceProfile               profile)
{
    return candidates
        .Select(c => ToScoredProject(c, profile))
        .OrderByDescending(p => p.RelevanceScore)
        .ToList();

    // Local helpers — only visible inside ScoreAll
    static double ScoreSkills(IReadOnlyList<long> skillIds, PreferenceProfile p)
    {
        if (skillIds.Count == 0) return 0.0;
        var matched = skillIds.Where(p.SkillAffinities.ContainsKey);
        return matched.Any() ? matched.Average(id => p.SkillAffinities[id]) : 0.0;
    }

    static double ScoreBudget(decimal? budgetMax, PreferenceProfile p)
    {
        if (budgetMax is null || p.PreferredBudgetSpread == 0) return 0.5;
        var z = (double)(budgetMax.Value - (decimal)p.PreferredBudgetCenter) / p.PreferredBudgetSpread;
        return Math.Exp(-0.5 * z * z);
    }

    ScoredProject ToScoredProject(CandidateProject c, PreferenceProfile p)
    {
        var skill    = ScoreSkills(c.SkillIds, p);
        var budget   = ScoreBudget(c.BudgetMax, p);
        var combined = 0.40 * skill + 0.20 * budget; // + other dimensions
        return new ScoredProject(c.ProjectId, c.Title, combined, skill, budget, "");
    }
}
```

**Rules for local functions:**
- Mark `static` whenever possible — `static` local functions cannot accidentally close over outer variables, preventing subtle bugs.
- Keep local functions at the **bottom** of the enclosing method (main logic flow at top, helpers below the main body).
- Keep them short (< 15 lines). If longer, promote to a private method.

### 16.3 — Private Static Methods

When the same helper logic is needed by 2+ methods in a class:

```csharp
public sealed class RecommendationEngine : IRecommendationEngine
{
    // Public API first
    public async Task RecomputeAsync(CancellationToken ct) { ... }
    public async Task<IReadOnlyList<ScoredProject>> GetTopAsync(int count, CancellationToken ct) { ... }

    // Private instance methods
    private async Task<PreferenceProfile> BuildProfileAsync(CancellationToken ct) { ... }
    private async Task PersistScoresAsync(IReadOnlyList<ProjectScore> scores, CancellationToken ct) { ... }

    // Private static — pure math, no instance state dependency
    private static double GaussianScore(double value, double center, double spread)
    {
        if (spread == 0) return 0.5;
        var z = (value - center) / spread;
        return Math.Exp(-0.5 * z * z);
    }

    private static double DecayWeight(double daysAgo, double lambda = 0.05)
        => Math.Exp(-lambda * daysAgo);
}
```

**Rules:** Mark `static` if the method accesses no instance state. Group in order: public → internal → private → private static.

### 16.4 — `file` Scoped Types (C# 11+)

Use the `file` access modifier for helper types that are implementation details of a single file and must never leak outside it:

```csharp
// ArabicTextNormalizer.cs
public static class ArabicTextNormalizer
{
    public static string Normalize(ReadOnlySpan<char> input) { ... }
}

// Only visible within ArabicTextNormalizer.cs — completely private to this file
file static class TashkeelTable
{
    internal static readonly FrozenSet<char> Chars = new HashSet<char>
    {
        '\u064B', '\u064C', '\u064D', '\u064E', '\u064F',
        '\u0650', '\u0651', '\u0652', '\u0653', '\u0654',
    }.ToFrozenSet();
}
```

**Use `file` for:** Lookup tables, internal state machines, parsing helpers that are specific to one file's implementation.

### 16.5 — `partial class` for Large Types

For large classes with clearly separable concerns, use `partial class` to split by concern without splitting into multiple types:

```csharp
// ProjectsViewModel.Feed.cs
public partial class ProjectsViewModel : ObservableObject
{
    [RelayCommand]
    private async Task LoadFeedAsync(CancellationToken ct) { ... }

    [RelayCommand]
    private async Task LoadNextPageAsync(CancellationToken ct) { ... }
}

// ProjectsViewModel.Filters.cs
public partial class ProjectsViewModel
{
    [ObservableProperty]
    private bool _unreadOnly;

    [RelayCommand]
    private void ApplyFilter(FilterChip chip) { ... }
}
```

**When to use:** A class exceeds ~200 lines and has clearly separable concerns. Never use `partial` to hide complexity — it should reveal structure, not bury it.

### 16.6 — Namespace and Folder Layout

Namespace mirrors the folder structure exactly:

```
MostaqlK/
├── Core/                          → MostaqlK.Core
│   ├── Abstractions/              → MostaqlK.Core.Abstractions   (interfaces)
│   ├── Domain/                    → MostaqlK.Core.Domain          (records, enums, value objects)
│   └── Result.cs                  → MostaqlK.Core
├── Infrastructure/                → MostaqlK.Infrastructure
│   ├── Database/                  → MostaqlK.Infrastructure.Database  (repos, migrations)
│   ├── Http/                      → MostaqlK.Infrastructure.Http      (scrapers, parsers)
│   └── Text/                      → MostaqlK.Infrastructure.Text      (Arabic normalizer)
├── Services/                      → MostaqlK.Services
│   ├── Pipeline/                  → MostaqlK.Services.Pipeline         (poll, enrich, diff)
│   └── Recommendation/            → MostaqlK.Services.Recommendation  (engine, tracker)
└── MostaqlK/ (MAUI project)       → MostaqlK
    ├── ViewModels/                → MostaqlK.ViewModels
    └── Views/                     → MostaqlK.Views
```

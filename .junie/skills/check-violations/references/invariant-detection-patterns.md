# Context-Based Invariant Detection Patterns

This reference details the modular invariant verification partitions available when auditing codebases, systems, and artifacts. 

**Context-First Rule:** Software systems take many forms (CLIs, headless microservices, static libraries, database daemons, or GUI applications). Never assume every project has UI screens, HTML parsing, or relational databases. Activate only the partitions that apply to the target system's nature.

---

## 1. Partition A: Lexical Pattern & Text Inspection (Core / Always Applicable)

Static rule inspection checks source code against architectural invariants, forbidden constructs, and coding contracts.

### Pragmatic Philosophy: Regex / Lightweight Rules First
- **Fast, Quick, and Efficient:** The primary goal of a checker is fast feedback. Building and maintaining full AST visitor suites (Roslyn, Tree-sitter, etc.) introduces significant project complexity, build overhead, and maintenance friction.
- **Rely on Regex and Line Scans:** The vast majority of architectural rules can be caught with targeted regular expressions, line scanners, or string matching (e.g. searching for banned factory instantiations `new ErrorResponse\(`, raw `catch \(\w+Exception\) \{\s*\}`, or banned namespace imports).
- **Reserve AST for Strict Necessity:** Only reach for AST parsers when lexical regex is genuinely incapable of resolving the invariant—such as complex multi-line nested scope disambiguation or semantic type resolution.

### Scriptability & Language Selection
- Pick the language that is most scriptable and natural for the target task (examples below, as many scriptable languages exist):
  - **Python (`.py`)**: Outstanding for quick, standalone scripts scanning directory trees with `re` and `pathlib`.
  - **Node.js / TypeScript (`.js` / `.ts`)**: Ideal for lightweight regex checks in JavaScript/web repositories.
  - **Single-File C# via PowerShell (`.ps1`)**: Ideal for .NET projects. C# can be executed as a lightweight single-file script directly inside PowerShell (using `Add-Type` or `dotnet-script`) without creating a full `.csproj` solution.
  - *(Note: Checkers are not restricted to these; any scriptable language such as Bash, Ruby, or Go scripts can be selected depending on ecosystem fit).*

### Common Invariant Targets
- **Bypassed Construction Factories:** Creating domain models, error instances, or network requests using raw constructors rather than designated factory methods.
- **Swallowed Failures:** Empty `catch` blocks or bare `except:` statements without logging or rethrowing.
- **Missing Cancellation Propagation:** Asynchronous functions omitting `CancellationToken` (C#), `context.Context` (Go), or `AbortSignal` (TypeScript).
- **Leaky Layer Boundaries:** Core/Domain modules importing Infrastructure, UI, or database libraries.

### Universal Fast Pattern Workflow
1. Enumerate target source files matching extension patterns (`*.cs`, `*.py`, `*.ts`, `*.go`).
2. Read files line-by-line or with multi-line regex matching.
3. Ignore comment lines or test fixtures if specified.
4. Flag violations with exact file and line coordinates.
5. If and only if false positives persist due to syntax nesting, escalate to a targeted AST parser.

---

## 2. Partition B: Structural & Contract Equivalence Testing (Context: Parsers, Scrapers, Serializers, Ingest Pipelines)

*Activate when the system processes structured/unstructured external inputs, parses markup/JSON, or performs data transformations.*

Structural equivalence testing checks whether a subsystem maintains its input/output contracts when exposed to adversarial, drifted, or alternative representations.

### Common Invariant Targets
- **Markup & Schema Drift Resilience:** DOM or payload parsers surviving CSS class renaming, tag substitutions, attribute reordering, or nested wrapper additions.
- **API Version Compatibility:** Schemas tolerating missing non-mandatory fields, nulls, or unexpected extra fields without panicking.
- **Bijective Serialization:** Round-trip transformations (`A -> B -> A'`) where `A == A'`.

### Universal Equivalence Harness Design
1. **Golden Master Fixtures:** Maintain a corpus of valid, canonical input fixtures alongside known expected extractions.
2. **Adversarial Mutated Fixtures:** Introduce mutated fixtures that simulate real-world changes:
   - *Renamed Markup / Payload Keys:* Semantic keys/class names replaced with obfuscated hashes or renamed aliases.
   - *Structural Wrapping:* Essential elements wrapped in extra containers or parent tags.
   - *Attribute / Field Stripping:* Non-essential IDs or metadata attributes removed.
3. **Headless Execution:** Run parsing harnesses headlessly with zero external network dependencies.
4. **Assertion Matrix:** Verify that extracted entities preserve mandatory invariant fields (e.g., ID, Title, Normalized Date, Currency Amount) despite payload mutations.

---

## 3. Partition C: Visual & Asset Parity Diffing (Context: Desktop, Mobile, & Web Frontends Only)

*Activate ONLY when the system has a graphical user interface (GUI) or renders visual assets. Skip completely for backend, CLI, or library projects.*

Visual parity auditing verifies that rendered interfaces match design specifications and that design system tokens/assets remain uncorrupted.

### Common Invariant Targets
- **Design Spec Fidelity:** Comparing rendered application screenshots against authorized design mockups (e.g., HTML mockups or Figma exports).
- **Cross-Platform Layout Consistency:** Ensuring responsive margins, typography scaling, and component heights adhere to platform constraints.
- **Asset Integrity:** Verifying that generated application icons, tray badges, and fonts conform to size, format, and color palette restrictions.

### Universal Visual Parity Methodology
1. **Target Capture:** Render UI components headlessly or take cropped region snapshots of running views.
2. **Pre-Processing & Normalization:** Resize to identical canvas dimensions and align color spaces (RGB/RGBA).
3. **Multi-Algorithm Metric Fusion:**
   - **Pixel-Level Diff:** Identifies exact coordinate discrepancies.
   - **Perceptual Hashing (pHash / dHash):** Tolerates sub-pixel rendering shifts, anti-aliasing variations, and font smoothing differences.
   - **Structural Similarity Index (SSIM):** Measures perceptual degradation and structural distortion rather than raw pixel variance.
4. **Tolerances & Thresholds:** Define clear acceptance boundaries (e.g., SSIM &ge; 0.95, Hamming distance &le; 2) to eliminate false failures from OS-level font smoothing.

---

## 4. Partition D: Data Model & State Invariant Auditing (Context: Relational/Document Databases & State Stores)

*Activate when the system persists state to a database (SQLite, PostgreSQL, etc.), persistent file, or distributed cache. Skip for purely stateless applications.*

State and database auditing validates that persisted entities adhere to relational and domain business rules that cannot be expressed in basic static types alone.

### Common Invariant Targets
- **Orphan Prevention:** Child entities referencing deleted or non-existent parent identifiers.
- **State Machine Legality:** Entities in states with incompatible flags (e.g., an item marked `Completed` without a `CompletedAt` timestamp).
- **Concurrency & Version Monotonicity:** Monotonically increasing revision numbers, valid optimistic concurrency tokens.
- **Data Hygiene:** Absence of un-sanitized entities, stripped whitespace, normalized ISO-8601 UTC timestamps.

### Universal State Audit Query Patterns
- Execute direct read-only SQL queries or ORM criteria queries against test or staging SQLite/Postgres databases:
  - `SELECT COUNT(*) FROM children LEFT JOIN parents ON children.parent_id = parents.id WHERE parents.id IS NULL;`
  - `SELECT id, status, completed_at FROM items WHERE status = 'done' AND completed_at IS NULL;`
- Assert that violation query count is strictly `0`.

---

## 5. Partition E: Runtime Concurrency, Protocol & Boundary Probing (Context: Daemons, Async Systems, CLI, Network)

*Activate when auditing async concurrency, background worker pools, CLI process contracts, or network boundaries.*

Validates resilience against timing hazards, rate-limit exhausts, uncooperative cancellations, exit code contracts, and boundary leaks.

### Common Invariant Targets
- **Cancellation Responsiveness:** Tasks immediately releasing resources when a cancellation signal triggers.
- **Backpressure & Token Bucket Conformance:** Requests stalling or rejecting predictably when concurrency limits are breached.
- **Process Exit Contracts (CLI/Daemons):** CLI tools returning proper exit codes (0 for success, non-zero for errors) and printing clean diagnostics to stderr.
- **Silent Degradation:** Graceful fallback behavior triggering without corrupting primary data stores.

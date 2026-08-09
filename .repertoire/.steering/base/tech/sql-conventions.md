# SQL Conventions and Standards

> **Scope:** All SQLite queries, DDL, and schema decisions in MostaqlK.
> **Principle:** Traditional, portable SQL only. Features that are SQLite-specific, NoSQL-adjacent, or that move business logic into the database are explicitly disallowed unless granted an exception in this document with a recorded rationale.

---

## Table of Contents

1. [Philosophy](#1-philosophy)
2. [Column Types — What We Use](#2-column-types--what-we-use)
3. [Explicit Avoidances](#3-explicit-avoidances)
4. [What We Do Use](#4-what-we-do-use)
5. [Accepted SQLite-Specific Exceptions](#5-accepted-sqlite-specific-exceptions)
6. [Parameterized Queries — Non-Negotiable](#6-parameterized-queries--non-negotiable)
7. [Naming Conventions](#7-naming-conventions)
8. [Boolean Handling](#8-boolean-handling)
9. [Date and Time Handling](#9-date-and-time-handling)
10. [NULL Semantics](#10-null-semantics)
11. [Transaction Discipline](#11-transaction-discipline)
12. [Index Rules](#12-index-rules)
13. [Query Formatting Style](#13-query-formatting-style)

---

## 1. Philosophy

The database is a **dumb, reliable store**. It holds rows and answers queries. It does not contain business logic, it does not transform data, and it does not make decisions.

All intelligence — parsing, validation, normalization, enrichment status transitions, search ranking, conflict resolution logic — lives in the **C# application layer**, not in the database.

This has three practical benefits:

1. **Portability:** If we ever swap SQLite for libSQL or another compatible engine, no SQL needs to change. Features that are engine-specific create lock-in.
2. **Testability:** C# code is unit-testable. SQL triggers, views with embedded logic, and generated columns are not.
3. **Debuggability:** When something goes wrong, you read C# code, not a mix of app code and hidden DB-layer behavior. Every operation the app performs is visible in one place.

The rule is simple: **if it can be done in C#, do it in C#. Use SQL only for storage and retrieval.**

---

## 2. Column Types — What We Use

SQLite has five storage classes and a type affinity system. We constrain ourselves to four of them with explicit, consistent usage:

| Storage class | Our usage | Examples |
|---|---|---|
| `INTEGER` | Numeric IDs, counts, booleans (0/1), ordinal values | `project_id`, `proposal_count`, `is_read` |
| `REAL` | Decimal numbers that may have fractional values | `budget_min`, `budget_max`, `hire_rate` |
| `TEXT` | All strings, dates, enumerations, status values | `title`, `description`, `posted_at`, `enrichment_status` |
| `BLOB` | Binary data **only when there is no alternative** — currently unused | — |

### Prohibited type usages

| What | Why prohibited |
|---|---|
| `NUMERIC` affinity | Ambiguous — behaves like INTEGER or REAL depending on the stored value. Use one or the other explicitly. |
| Storing dates as `INTEGER` (Unix timestamp) | Loses human-readability in DB tools, requires conversion functions for every comparison. Use ISO8601 TEXT (see §9). |
| Storing booleans as `TEXT` (`'true'`/`'false'`) | Inconsistent with the rest of the codebase. Use `INTEGER` `0`/`1` (see §8). |
| Storing enumerations as `INTEGER` codes | Opaque. `1` means nothing; `'enriched'` is self-documenting. Use `TEXT` with a `CHECK` constraint. |

---

## 3. Explicit Avoidances

These features are **banned** from this codebase. Each has a rationale. If a future situation seems to require one of these, open a discussion and write an ADR before using it.

### 3.1 — JSON / JSONB Columns

**Banned.**

Storing structured data as a JSON string (or SQLite's `JSON1` extension functions like `json_extract()`, `json_each()`) is prohibited.

If a piece of data seems like it needs JSON storage, the correct answer is a new normalized table. JSON columns break 1NF, cannot be indexed on their internal fields, cannot be joined against, and cannot be type-checked by the DB. They are a shortcut that defers a schema design problem rather than solving it.

```sql
-- WRONG: storing skills as JSON
ALTER TABLE projects ADD COLUMN skills_json TEXT;  -- NO

-- RIGHT: normalized join table
CREATE TABLE project_skills (
    project_id INTEGER NOT NULL REFERENCES projects(project_id),
    skill_id   INTEGER NOT NULL REFERENCES skills(skill_id),
    PRIMARY KEY (project_id, skill_id)
);
```

**The only exception:** the `settings` table uses a key-value TEXT store for app configuration. This is a deliberate design choice for settings specifically, documented in the schema design. It is not a license to use key-value patterns for domain data.

---

### 3.2 — Triggers

**Banned.**

Triggers embed business logic inside the database, invisible to the application layer. They fire on DML statements and can produce side effects that are not apparent from reading the application code.

Specifically prohibited:
- `AFTER INSERT` triggers to maintain derived data
- `BEFORE INSERT` triggers for validation
- Triggers to maintain the FTS index (handled explicitly in the enrichment transaction)
- Triggers to update `owners.last_seen_at` (handled explicitly in `OwnerRepository.UpsertAsync`)

If you find yourself wanting a trigger, the correct solution is to do that work explicitly in the C# repository method that performs the DML.

---

### 3.3 — Views

**Banned** for application queries.

Views are a presentation abstraction that hide the actual JOIN structure of a query. They make it harder to reason about query performance (indexes, table access patterns) and create an additional layer of indirection with no benefit in a single-application codebase.

Every query the application runs is written out explicitly with its full JOIN chain. This makes query intent clear, makes index usage analyzable, and makes every SELECT grep-searchable in the codebase.

```sql
-- WRONG: a view hiding the JOIN
CREATE VIEW enriched_projects AS
    SELECT p.*, pd.description, pd.budget_min
    FROM projects p JOIN project_details pd ...;

-- RIGHT: write the JOIN every time, in the repository method
SELECT p.project_id, p.title, pd.description, pd.budget_min
FROM   projects p
JOIN   project_details pd ON p.project_id = pd.project_id
WHERE  ...;
```

---

### 3.4 — Generated / Computed Columns

**Banned.**

SQLite supports `GENERATED ALWAYS AS (expr)` columns (since 3.31.0). These are prohibited because:
- Computed values belong in the application model, not the schema
- They are not portable across SQLite versions
- They make the schema harder to read for developers unfamiliar with this SQLite feature

If a value needs to be computed from other columns, compute it in C# and — if it needs to be stored — store it as a normal column updated explicitly.

---

### 3.5 — Stored Procedures

SQLite does not support stored procedures. This is not an accidental limitation — it is a design choice that aligns with our philosophy. If we ever migrate to a database that does support them, they remain banned by this convention.

---

### 3.6 — Window Functions

**Banned for now.**

Window functions (`ROW_NUMBER()`, `RANK()`, `LAG()`, `LEAD()`, etc.) are SQL standard (SQL:2003) and SQLite supports them since version 3.25.0. However, all ranking and pagination logic we need can be expressed with simpler `ORDER BY` + `LIMIT`/`OFFSET` queries. Window functions are permitted only if a specific query genuinely cannot be expressed without them and an ADR documents the exception.

---

### 3.7 — `RIGHT JOIN` and `FULL OUTER JOIN`

**Banned** — and SQLite does not support them anyway. All joins are written as `INNER JOIN` or `LEFT JOIN` (with the known/"complete" table on the left). This is both a convention and a practical constraint.

---

### 3.8 — `UPSERT` (ON CONFLICT DO UPDATE)

**Banned for domain tables.**

The `INSERT ... ON CONFLICT DO UPDATE SET ...` (upsert) syntax is not used. It would blur the line between our two distinct insert policies:

- `INSERT OR IGNORE` for `projects` — the no-update / store-and-forget policy
- `INSERT OR REPLACE` for `owners` — the one explicit update exception

Using upsert syntax would make both look the same and make the different intent invisible. Explicit conflict resolution clauses are used instead (see §5).

---

### 3.9 — Schema Changes Without a Migration

**Banned.**

No `ALTER TABLE`, `CREATE TABLE`, or `CREATE INDEX` statement may be executed outside a numbered migration in `schema_migrations`. Every change to the schema — even adding a single column — goes through the migration runner (`DatabaseInitializer`). This ensures every installation of the app has a predictable schema state.

---

## 4. What We Do Use

### Standard DML

All four standard DML statements, used conventionally:

```sql
SELECT  ...
FROM    ...
[JOIN   ...]
[WHERE  ...]
[GROUP BY ...]
[HAVING ...]
[ORDER BY ...]
[LIMIT  @n OFFSET @offset];

INSERT INTO table (col1, col2) VALUES (@v1, @v2);

UPDATE table SET col = @val WHERE pk = @pk;

DELETE FROM table WHERE pk = @pk;
```

### Joins

Only `INNER JOIN` (written as `JOIN`) and `LEFT JOIN`:

```sql
-- INNER JOIN: both sides must have a matching row
JOIN   owners o ON p.owner_id = o.owner_id

-- LEFT JOIN: left side is guaranteed; right side may be absent (nullable result)
LEFT JOIN project_details pd ON p.project_id = pd.project_id
LEFT JOIN categories       c  ON p.category_id = c.category_id
```

`LEFT JOIN` is used whenever the right-side table might not have a row for every left-side row — specifically: `project_details` (not yet enriched), `categories` (category might be unknown at listing time), and `assets` (only if `include_assets` is on).

### Subqueries

Permitted in `WHERE` clauses for set membership and existence checks:

```sql
-- Acceptable: existence check
WHERE EXISTS (
    SELECT 1 FROM project_skills ps
    JOIN skills s ON ps.skill_id = s.skill_id
    WHERE ps.project_id = p.project_id
      AND s.name = @skillName COLLATE NOCASE
);

-- Acceptable: IN with a subquery
WHERE p.project_id IN (
    SELECT project_id FROM project_skills WHERE skill_id = @skillId
);
```

Subqueries in `FROM` (derived tables) are permitted for simple cases but should be favored only when an explicit multi-step approach in C# would be more complex. When in doubt, bring the data up to C# and filter there.

### CTEs (Common Table Expressions)

Permitted for readability when a query has multiple logical steps that would otherwise be hard to follow as nested subqueries:

```sql
WITH unread_projects AS (
    SELECT project_id FROM projects WHERE is_read = 0
)
SELECT p.project_id, p.title
FROM   projects p
JOIN   unread_projects u ON p.project_id = u.project_id
ORDER  BY p.posted_at DESC;
```

CTEs are SQL:1999 standard and improve readability significantly over deeply nested subqueries. They are not execution-time materialized barriers — the SQLite query planner treats them as inline views.

### Aggregate Functions

Standard set only: `COUNT()`, `SUM()`, `MIN()`, `MAX()`, `AVG()`. No engine-specific aggregate extensions.

### Standard DDL

```sql
CREATE TABLE IF NOT EXISTS ...
CREATE INDEX IF NOT EXISTS ...
DROP TABLE IF EXISTS ...     -- only inside a DOWN migration, never in production code
ALTER TABLE ... ADD COLUMN ...  -- only inside a migration script
```

---

## 5. Accepted SQLite-Specific Exceptions

These features are SQLite-specific and would not port to another SQL engine without change. Each is explicitly accepted with a documented rationale. No other SQLite-specific feature is permitted without a new entry here and an ADR.

### Exception 1 — `INSERT OR IGNORE`

**Used on:** `projects` table exclusively.
**Rationale:** Implements the store-and-forget / no-update policy. Standard SQL equivalent would be `INSERT ... ON CONFLICT DO NOTHING` (SQL:2003), which SQLite also supports but which is longer and less idiomatic in SQLite codebases. Either syntax is acceptable; `INSERT OR IGNORE` is chosen for conciseness. This is a deliberate, documented design choice — not lazy usage.

```sql
INSERT OR IGNORE INTO projects (project_id, title, ...) VALUES (@id, @title, ...);
```

### Exception 2 — `INSERT OR REPLACE`

**Used on:** `owners` table exclusively.
**Rationale:** Owners are the one exception to the no-update policy. Their stats (hire rate, open projects count, etc.) are refreshed when the same owner appears in a new scrape. `INSERT OR REPLACE` deletes the old row and inserts a new one atomically. Documented exception in ADR-0001.

```sql
INSERT OR REPLACE INTO owners (owner_id, display_name, ...) VALUES (@id, @name, ...);
```

### Exception 3 — FTS5 Virtual Table

**Used on:** `projects_fts` table only.
**Rationale:** Accepted in ADR-0001 as the full-text search mechanism. FTS5 is part of the SQLite core binary shipped by `Microsoft.Data.Sqlite` — it requires no loadable extension DLL. No other virtual table mechanism is used.

```sql
CREATE VIRTUAL TABLE IF NOT EXISTS projects_fts USING fts5 ( ... );
INSERT INTO projects_fts (...) VALUES (...);
SELECT project_id, rank FROM projects_fts WHERE projects_fts MATCH @query;
```

### Exception 4 — `COLLATE NOCASE`

**Used on:** `categories.name`, `skills.name`.
**Rationale:** Prevents duplicate entries that differ only in case ("PHP" vs "php"). Standard SQL has `COLLATE` but the specific collation names are engine-specific. `NOCASE` is the SQLite built-in case-insensitive ASCII collation. Acceptable because Arabic text is already case-insensitive by script; this only affects Latin skill names and category names.

### Exception 5 — `PRAGMA` statements

**The following PRAGMAs are set on every connection open:**

| PRAGMA | Value | Reason |
|---|---|---|
| `foreign_keys` | `ON` | Enforces FK constraints — SQLite disables them by default |
| `journal_mode` | `WAL` | Concurrent readers during pipeline writes |
| `synchronous` | `NORMAL` | Safe with WAL; faster than `FULL` |
| `busy_timeout` | `5000` | 5-second wait before returning `SQLITE_BUSY` on writer contention |

No other PRAGMAs are used in application code. `PRAGMA user_version` is used only by the migration runner, not in domain queries.

---

## 6. Parameterized Queries — Non-Negotiable

Every value that originates from outside the application binary — user input, scraped data, configuration — **must** go through a query parameter. Never ever build a SQL string by concatenation or interpolation.

```csharp
// CORRECT — parameterized
var cmd = connection.CreateCommand();
cmd.CommandText = "SELECT * FROM projects WHERE category_id = @catId AND is_read = @isRead";
cmd.Parameters.AddWithValue("@catId",  categoryId);
cmd.Parameters.AddWithValue("@isRead", 0);

// WRONG — string interpolation (SQL injection, broken with special characters)
cmd.CommandText = $"SELECT * FROM projects WHERE category_id = {categoryId}";  // NO
```

### Dynamic query builder (QueryCompiler)

The v2 filter system compiles user-defined filter chips into a parameterized WHERE clause. The field name and operator come from a **whitelist**, never from raw user input. Only the value goes through a parameter.

```csharp
// CORRECT — field and operator from whitelist, value parameterized
var allowedFields = new HashSet<string> { "budget_max", "category_id", "is_read", ... };
if (!allowedFields.Contains(chip.Field)) throw new InvalidOperationException("Unknown field");

sql.Append($" AND {chip.Field} {GetOperatorSql(chip.Operator)} @p{paramIndex}");
cmd.Parameters.AddWithValue($"@p{paramIndex}", chip.Value);
```

The `QueryCompiler` class is the **only** place in the codebase where SQL fragments are assembled dynamically. Every other repository method uses a fixed query string.

---

## 7. Naming Conventions

| Element | Convention | Examples |
|---|---|---|
| Table names | `snake_case`, plural | `projects`, `poll_runs`, `project_skills` |
| Column names | `snake_case`, singular | `project_id`, `display_name`, `enriched_at` |
| Primary key | `{table_singular}_id` | `project_id`, `owner_id`, `skill_id` |
| Foreign key | Same name as the PK it references | `owner_id` on `projects` references `owner_id` on `owners` |
| Boolean columns | `is_` prefix | `is_read` |
| Timestamp columns | `_at` suffix | `posted_at`, `scraped_at`, `enriched_at`, `fired_at` |
| Date-only columns | `_on` suffix (if we ever need date-only) | `joined_on` |
| Status/enum columns | `_status` suffix | `enrichment_status`, `poll status` |
| Count columns | `_count` suffix | `proposal_count`, `projects_found` |
| Index names | `idx_{table}_{column(s)}` | `idx_projects_posted_at`, `idx_project_skills_skill` |
| FTS table | `{table}_fts` | `projects_fts` |
| Migration files | `Migration_{NNNN}_{description}.sql` | `Migration_0001_initial_schema.sql` |

### SQL keyword casing in queries

SQL keywords are written in **UPPERCASE**. Table and column names are written in **lowercase**:

```sql
SELECT p.project_id, p.title, o.display_name
FROM   projects p
JOIN   owners o ON p.owner_id = o.owner_id
WHERE  p.is_read = 0
ORDER  BY p.posted_at DESC
LIMIT  @pageSize OFFSET @offset;
```

---

## 8. Boolean Handling

SQLite has no native boolean type. Booleans are stored as `INTEGER` with value `0` (false) or `1` (true).

**Rules:**
- Column type declared as `INTEGER NOT NULL DEFAULT 0`
- Always accompanied by a `CHECK` constraint: `CHECK (col_name IN (0, 1))`
- Column name always uses the `is_` prefix
- In C# model classes, mapped to `bool` — the repository layer converts `0`/`1` ↔ `false`/`true`
- In SQL queries, compared with `= 0` or `= 1`, never with `= true` or `IS true`

```sql
-- Schema declaration
is_read INTEGER NOT NULL DEFAULT 0 CHECK (is_read IN (0, 1))

-- Query usage
WHERE is_read = 0     -- unread projects
WHERE is_read = 1     -- read projects

-- NOT like this:
WHERE is_read = true  -- NOT valid in SQLite
WHERE is_read IS true -- NOT valid in SQLite
```

---

## 9. Date and Time Handling

SQLite has no native date or timestamp type. All dates and datetimes are stored as **ISO8601 TEXT** in UTC.

**Rules:**

| Rule | Detail |
|---|---|
| Always UTC | All timestamps stored in UTC. Conversion to local time happens in the UI layer (C# `DateTimeOffset` → local display). |
| Full datetime format | `"2026-08-09T14:30:00Z"` — always include the time component, even for dates that originated as date-only. |
| Never relative strings | `"منذ 4 ساعات"` is resolved to an absolute `DateTimeOffset` at scrape time in `ListingParser`. The relative string is never stored. |
| No SQLite date functions in app queries | `datetime()`, `julianday()`, `strftime()` are not used in application queries. Date arithmetic and formatting happen in C#. |
| DEFAULT expressions | `DEFAULT (datetime('now'))` is acceptable in `CREATE TABLE` as a column default — this is the one place SQLite date functions appear. All explicit INSERT values use the ISO8601 string from C#. |

```csharp
// C# — always use UTC when building INSERT values
var scrapedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
cmd.Parameters.AddWithValue("@scrapedAt", scrapedAt);
```

```sql
-- Schema default (acceptable SQLite function usage)
scraped_at TEXT NOT NULL DEFAULT (datetime('now'))

-- Application query date comparison (string comparison works on ISO8601)
WHERE posted_at >= @sevenDaysAgo  -- ISO8601 strings compare lexicographically correctly
ORDER BY posted_at DESC
```

---

## 10. NULL Semantics

NULL means **"this value is genuinely unknown or not applicable"** — not a sentinel, not a default, not an error state.

**Rules:**

| Rule | Detail |
|---|---|
| Use NOT NULL wherever possible | Only columns that are genuinely optional at the DB level may be nullable. |
| Never use NULL as a sentinel for a specific state | Use a TEXT enum value instead (e.g. `enrichment_status = 'failed'`, not `description = NULL` meaning "failed"). |
| Document every nullable column | Each nullable column in the schema has a comment explaining what NULL means in that context. |
| Always use IS NULL / IS NOT NULL | Never use `= NULL` (always evaluates to UNKNOWN in SQL). |
| Nullable columns in JOINs | When joining to a table that may not have a row (e.g. `project_details`), use `LEFT JOIN` and handle NULL results in C#. |

### Documented nullable columns

| Column | Table | Null means |
|---|---|---|
| `hire_rate` | `owners` | "لم يحسب بعد" — rate not yet calculated by Mostaql |
| `completed_at` | `poll_runs` | Poll is still running, or failed before it could record completion |
| `error_message` | `poll_runs` | Poll succeeded — no error |
| `category_id` | `projects` | Category was not parseable from the listing card |
| `budget_min` / `budget_max` | `project_details` | No budget specified by the client |
| `delivery_days` | `project_details` | No delivery timeframe specified |
| `local_path` | `assets` | File has not been downloaded yet |
| `downloaded_at` | `assets` | File has not been downloaded yet |
| `project_id` | `notifications` | Grouped toast (references multiple projects, not one) |
| `dismissed_at` / `clicked_at` | `notifications` | Notification has not been dismissed or clicked yet |

---

## 11. Transaction Discipline

Every write that touches more than one table **must** be wrapped in a single transaction. A partial write (some tables updated, others not) is a corrupt state.

**The enrichment transaction** (the most important one):

```csharp
using var tx = connection.BeginTransaction();
try
{
    // 1. Upsert owner
    await ownerRepo.UpsertAsync(owner, tx);

    // 2. Resolve or insert category
    var categoryId = await categoryRepo.GetOrCreateAsync(categoryName, tx);

    // 3. Resolve or insert each skill
    var skillIds = await skillRepo.GetOrCreateManyAsync(skillNames, tx);

    // 4. Insert project (INSERT OR IGNORE — no-op if already committed)
    var inserted = await projectRepo.InsertAsync(project, categoryId, tx);

    if (inserted)
    {
        // 5. Insert project_details (same transaction)
        await detailRepo.InsertAsync(projectId, details, tx);

        // 6. Insert project_skills (same transaction)
        await skillRepo.LinkToProjectAsync(projectId, skillIds, tx);

        // 7. Insert into FTS (same transaction)
        await ftsRepo.InsertAsync(projectId, title, description, joinedSkills, tx);
    }

    tx.Commit();
    // Only after Commit: mark the project complete in InFlightTracker
}
catch
{
    tx.Rollback();
    throw;
}
```

**Rules:**
- `InFlightTracker.MarkComplete(id)` is called **only after** `tx.Commit()` — never inside the transaction, never before commit.
- Single-row reads (SELECT) do not need explicit transactions.
- Batch reads (multiple SELECTs that must be consistent) use a read transaction if consistency is required.
- Never use `SAVEPOINT` or nested transactions — flatten the logic instead.

---

## 12. Index Rules

**Indexes are schema — they live in migration scripts, not in application code.**

| Rule | Detail |
|---|---|
| Every FK column gets an index | FK columns used in JOINs are always indexed: `owner_id`, `category_id`, `poll_run_id`, `project_id` (on child tables), `skill_id`. |
| Every common filter column gets an index | Columns frequently in WHERE: `is_read`, `enrichment_status`, `posted_at`, `budget_min`/`budget_max`. |
| Every common sort column gets an index | `posted_at DESC` is the primary sort — it gets a descending index. |
| Composite indexes only when both columns appear together in queries | Avoid composite indexes speculatively — add them when a specific slow query justifies them. |
| No redundant indexes | `PRIMARY KEY` is already indexed. Don't add a separate index on the PK column. |
| `IF NOT EXISTS` on all `CREATE INDEX` | Makes migration scripts idempotent. |

---

## 13. Query Formatting Style

Queries written in C# string literals follow this formatting — consistent, readable, diff-friendly:

```sql
SELECT p.project_id,
       p.title,
       p.posted_at,
       p.proposal_count,
       p.is_read,
       p.enrichment_status,
       o.display_name   AS owner_name,
       c.name           AS category_name,
       pd.budget_min,
       pd.budget_max,
       pd.delivery_days
FROM   projects p
JOIN   owners         o  ON p.owner_id    = o.owner_id
LEFT JOIN categories  c  ON p.category_id = c.category_id
LEFT JOIN project_details pd ON p.project_id = pd.project_id
WHERE  p.is_read = @isRead
  AND  p.enrichment_status = @status
ORDER  BY p.posted_at DESC
LIMIT  @pageSize
OFFSET @offset;
```

**Style rules:**
- Keywords (`SELECT`, `FROM`, `JOIN`, `WHERE`, `AND`, `ORDER BY`, `LIMIT`, `OFFSET`) in UPPERCASE.
- Table aliases are single lowercase letters or short abbreviations (`p` for `projects`, `o` for `owners`, `pd` for `project_details`).
- `AND` conditions align under `WHERE` with two-space indent.
- `AS` aliases aligned with spaces for readability in multi-column SELECT lists.
- One clause per line.
- Closing `;` on the last line.

---

## Amendment Process

If a use case arises that genuinely requires a feature listed as banned:
1. Raise it with the team.
2. Write an ADR explaining the specific use case, why the traditional SQL approach is insufficient, and what the trade-offs are.
3. Add the exception to §5 of this document with a reference to the ADR.
4. Do not use the feature until the ADR is accepted.

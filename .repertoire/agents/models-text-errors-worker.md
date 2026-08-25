# Worker Report: models-text-errors-worker

**Wave:** B1 — pure leaf layers (`errors`, `models`, `text`)
**Date:** 2026-08-25
**Status:** COMPLETE — all gates green

---

## Goal

Implement the pure leaf layers of the Python migration exactly per frozen plan §8
contracts (`.repertoire/python/docs/refactor-python-plan.md`): `src/mostaql/errors.py`,
`src/mostaql/models/**`, `src/mostaql/text/**`, plus thorough parity tests, mirroring
C# `Core/DomainError.cs`, `Core/Utilities/StringNormalization.cs`,
`Core/Formatting/{ArabicRelativeTime,ArabicProposalParser}.cs`, `Models/*.cs`, and the
module error factories (PollErrors/DiffErrors/EnrichErrors/DatabaseErrors/ParseErrors).

## Files created / modified

| File | Action |
|---|---|
| `src/mostaql/errors.py` | Implemented: `DomainError` dataclass, `BackboneError` + 7 subclasses, 10 factories |
| `src/mostaql/models/__init__.py` | Barrel re-exports |
| `src/mostaql/models/enrichment_status.py` | New: `EnrichmentStatus(StrEnum)` |
| `src/mostaql/models/owner.py` | New: `Owner` frozen slots dataclass |
| `src/mostaql/models/project_skill.py` | New: `ProjectSkill` |
| `src/mostaql/models/project_summary.py` | New: `ProjectSummary` |
| `src/mostaql/models/project_details.py` | New: `ProjectDetails(ProjectSummary)` |
| `src/mostaql/models/field_resolution.py` | New: `FieldResolution`, `FieldMismatch` |
| `src/mostaql/text/normalization.py` | Implemented: normalize / to_ascii_digits / strip_diacritics / normalize_label / clean_html + LABEL_TRIM_CHARS |
| `src/mostaql/text/relative_time.py` | Implemented: parse_relative_number |
| `src/mostaql/text/proposals.py` | Implemented: parse_proposals |
| `src/mostaql/text/__init__.py` | Barrel re-exports |
| `tests/test_normalization.py` | New |
| `tests/test_relative_time.py` | New |
| `tests/test_proposals.py` | New |
| `tests/test_models.py` | New |

Existing `test_config.py` / `test_interaction_log.py` untouched; pyproject/other modules untouched.

## Decisions

1. **ProjectDetails = INHERITANCE (not composition).**
   C# duplicates summary fields into ProjectDetails and omits ClientName/SkillsText/
   IsUnread. Python subclassing `ProjectDetails(ProjectSummary)` was chosen because:
   both are `frozen=True, slots=True` so inheritance stays sane; flat constructor mirrors
   DetailParser's object-initializer usage; details then flow through any
   summary-shaped consumer. Documented deviation: Python details carry client_name=""
   /skills_text=""/is_unread=True with safe defaults — harmless for parity since C#
   UpsertDetailsAsync binds `@client_name` from `details.Owner.Name`, never from a
   ClientName property (verified in ProjectRepository.cs:146).
2. **Required kw-only `discovered_at`.** Dataclass field-order rules forbid a required
   field after defaulted ones, so it is declared `field(kw_only=True)` in C# property
   order; `enriched_at` is kw-only with default None. Plain tz-aware datetimes kept.
3. **Int32 TryParse guard preserved.** Both digit parsers mirror C# `int.TryParse`
   fall-through: digit runs > 2^31-1 are ignored and word heuristics decide
   ("99999999999 يوم" → 1; "99999999999 عرض" → 0).
4. **PARSE codes invented as PARSE-001/002/003.** C# ParseErrors throws bare
   exceptions (no DomainError); ErrorCodeRegistry documents only the "PARSE" domain.
   Numbered in C# declaration order: EmptyHtml→001, MissingTitle→002,
   NoProjectRows→003. NoProjectRows message verbatim incl. misleading
   "div.project-card" (plan §4.1 trap 1).
5. **schema_mismatch(current, expected) wording synthesis.** Combines DB-003's
   SchemaInvalid frame ("Database schema is invalid or out of date: ...") with the
   SchemaVersionMismatch sentence verbatim as the details payload; Arabic external
   message from SchemaInvalid.
6. **DomainError.cause typed `BaseException | None`** per goal text (plan §8 said
   `Exception`; goal is stricter and binding).
7. **RUF001/2/3 ambiguous-unicode suppression via inline noqa** on intentional Arabic
   literals + ASCII hyphens in docstrings (pyproject untouchable per boundaries).
8. **Xenon C→B refactor:** parser bodies split into `_first_digit_run` /
   `_word_count` / `_contains_any` helpers preserving exact C# evaluation order.

## Parity traps verified by tests (§4.1)

- Trap 1: NoProjectRows verbatim message ✓ (test_no_project_rows_verbatim_including_div_project_card_trap)
- Trap 5: "2024/05/01"→2024 digit-run precedence ✓
- Trap 6: to_ascii_digits maps BOTH U+0660–69 and U+06F0–F9 ✓ (Persian ۴ test)
- Trap 7: entity-decode-before-strip ("&lt;b&gt;" removed as tag) ✓;
  newline-spanning tag "<div\nclass='x'>" survives ✓; single-line tags around \n still removed ✓
- Trap 8: zero-width/bidi U+200B/U+200E/U+200F survive normalize + normalize_label ✓
- Trap 21: "عرض واحد" requires bigram; bare "عرض" equality→1; negatives impossible ✓
- Trap 22: model defaults snapshot (is_unread True, PENDING, empty-string-not-null) ✓

## Verification evidence (trimmed)

```
> uv sync            → Resolved 56 packages ... Installed 1 package (mostaql 0.1.0)
> uv run ruff format .   → 52 files left unchanged
> uv run ruff check .    → All checks passed!
> uv run mypy src        → Success: no issues found in 43 source files
> uv run xenon src -b B  → (no output = no blocks worse than B)
> uv run lint-imports    → Contracts: 3 kept, 0 broken (pipeline-free-of-storage-and-http KEPT,
                           pure-leaves KEPT, httpx-only-in-http-layer KEPT)
> uv run pytest -q       → 174 passed in 1.23s
Coverage: errors.py 100%, models/* 100%, text/* 100%; TOTAL 93% (threshold 60)
```

## Concerns / notes for master

- PARSE-001/002/003 numbering is my invention (C# has none) — downstream scraping wave
  should import `empty_html`/`missing_title`/`no_project_rows` from `mostaql.errors`
  rather than redefining codes in `scraping/parsers/errors.py`.
- `html.unescape` vs HAP `DeEntitize`: identical for all HTML entities in practice;
  exotic edge entities could theoretically differ (flagged for regression-fixture wave).
- The Int32-guard tests document behavior for absurd inputs only; real pages unaffected.

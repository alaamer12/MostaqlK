# inference-engine-porter

## Goal
Port `inference.py`'s structure-independent scoring engine to C# as `InferenceEngine`, and rewrite
`DetailParser.Parse()` to be the full structural/inference combinator matching `pipeline.py`'s
`parse_project()` algorithm, replacing the simpler placeholder merge from a prior step.

## Files created
- `Infrastructure/Http/Parsers/InferenceEngine.cs` - full port of `inference.py`.

## Files modified
- `Infrastructure/Http/Parsers/DetailParser.cs` - rewritten to mirror `pipeline.py`'s
  `parse_project()` combinator exactly.
- `Models/ProjectDetails.cs` - added `FieldProvenance` (`Dictionary<string, FieldResolution>`) and
  `Mismatches` (`List<FieldMismatch>`) properties, plus the two new `FieldResolution`/`FieldMismatch`
  records, so every one of the 12 meta-row fields (not just the ones with a dedicated property like
  `Budget`/`DeliveryDays`/`Owner.HiringRatePercent`) gets its resolved value + source + confidence
  recorded, matching `pipeline.py`'s `fields` dict output. Verified via grep that `ProjectDetails` is
  only constructed in `DetailParser.cs`, so no other code needed updating.

## Files NOT touched (per instructions)
`ListingParser.cs`, `Services/`, `UI/`, `Features/`, `Infrastructure/Database`, `Infrastructure/Notifications`.

## Ported weights / field profiles (spot-check against `inference.py`)
- `WEIGHTS`: `STEM_WEIGHT=3.0`, `UNIT_WEIGHT=2.0`, `TYPE_WEIGHT=1.0`, `POSITION_WEIGHT=0.5`,
  `MISSING_UNIT_PENALTY=-1.5`, `BOILERPLATE_DAMPING_THRESHOLD=6`, `BOILERPLATE_DAMPING_FACTOR=0.35`.
- `LOCAL_WINDOW_TOKENS=12`, `LOCAL_CONFIDENCE_MARGIN=0.20`.
- Arabic affix lists: prefixes `["ال","و","ف","ب","ل","لل"]`, suffixes
  `["ها","هم","ه","ة","ات","ين","ون","ي","ا"]` - ported verbatim into `StripAffixes`.
- `FIELD_PROFILES`: all 12 field keys ported 1:1 with the same `core_stems`/`expected_types`/
  `expected_types_weak`/`unit_hints`/`requires_unit` as the Python source (`project_status`,
  `published_date`, `budget`, `duration`, `registration_date`, `hire_rate`, `open_projects_count`,
  `in_progress_count`, `ongoing_conversations`, `started_since`, `deal_date`, `delivery_date`).
- Value-type regexes (`PERCENT_RE`, `RANGE_RE`, `FLOAT_RE`, `INT_RE`, `ABSOLUTE_DATE_RE`) translated
  1:1 to .NET `Regex` syntax (all patterns are regex-syntax-compatible as-is).
- Candidate merge regexes (`_MERGE_CONNECTOR_RE`, `_MERGE_DIGIT_RE`, `_VALUE_SEED_RE`) ported verbatim.
- `dom_distance`/`_dom_path`/`flatten`/`extract_candidates`/`_find_adjacent_unit`/`score_candidate`/
  `apply_position_prior`/`softmax`/`resolve_fields`/`infer_fields` all ported with matching control
  flow; each C# method has an inline comment citing the Python function it mirrors.

## `DetailParser.Parse()` rewrite
- `LabelToField` (Arabic label -> field key), `CompletedOnlyFields = {started_since, deal_date,
  delivery_date}`, `CompletedStatusText = "مكتمل"` ported verbatim from `LABEL_TO_FIELD`/
  `COMPLETED_ONLY_FIELDS`/`COMPLETED_STATUS_TEXT`.
- Per field: structural value from `StructuralExtractor.ExtractMetaFields` (falling back to
  `LabelDrivenExtract` when the structural selector itself has nothing for that label - this was
  already the case in the prior placeholder merge and is preserved), sanity-checked via `SanityOk`
  (mirrors `_sanity_ok`: non-empty, placeholder markers are valid-but-nullable, numeric fields
  require a digit - ASCII or Arabic-Indic via `ArabicDigitRegex`). On sanity failure, falls back to
  `InferenceEngine.InferFields` computed lazily once per page (`inferenceResults ??= ...`).
- Cross-validation (`ValuesAgree` mirrors `_values_agree`) runs whenever inference has already been
  computed and structural produced *some* value, recording a `FieldMismatch` and preferring the
  inference value when structural failed sanity and they disagree.
- `IsPlaceholder` (mirrors `_is_placeholder`) resolves placeholder markers to `null` after resolution.
- Nullable-by-design enforcement: (1) for `CompletedOnlyFields` resolved via inference, forced to
  `null` unless that field's Arabic label text is literally present in the page's full text
  (`root.InnerText`, de-entitized); (2) regardless, forced to `null` for `CompletedOnlyFields`
  unless `project_status == "مكتمل"`.
- title/skills/description/attachments stay structural-only:
  - description now scopes `text-wrapper-div` lookup inside `#projectDetailsTab` first (matching
    `pipeline.py`'s exact fallback order/comment about review-comment vs. proposal reuse of the
    same class), only falling back to a page-wide search if the tab is absent. This is a behavior
    change vs. the prior step's `SelectByClassOrId` (which searched by id OR class, not "id then
    scoped class-inside-id, else page-wide class").
  - skills/attachments/owner-name extraction unchanged from the prior step (already correct).
- Mapped resolved fields into `ProjectDetails`: `Budget` <- `fields["budget"]`, `DeliveryDays` <-
  `ParseLeadingInt(fields["duration"])`, `Owner.HiringRatePercent` <-
  `ParsePercent(fields["hire_rate"])`, `Owner.CompletedProjectsCount` <-
  `ParseLeadingInt(fields["in_progress_count"])` (this mirrors the prior step's existing - albeit
  oddly-named - `OwnerCompletedProjectsLabel = "مشاريع قيد التنفيذ"` mapping, left unchanged since
  it predates this task). All 12 fields (including the 8 with no dedicated model property) are
  recorded into the new `ProjectDetails.FieldProvenance` dictionary with their source/confidence,
  and cross-validation mismatches into `ProjectDetails.Mismatches`.

## Intentional deviations from the Python original
1. `pipeline.py`'s `structural.get(label)` only reads `analyzer.structural_meta_extract` (no
   label-driven fallback layered underneath); the prior C# step had already layered
   `LabelDrivenExtract` under `ExtractMetaFields` for robustness (selectors are less stable than a
   pure identifier-blind fallback) - this was preserved here since it's strictly more robust than
   the Python original and doesn't change behavior when the structural selector does match.
2. `ProjectDetails` doesn't have dedicated properties for 8 of the 12 meta-row fields
   (`project_status`, `published_date`, `registration_date`, `open_projects_count`,
   `ongoing_conversations`, `started_since`, `deal_date`, `delivery_date`) - rather than expanding
   the model with 8 new properties (out of scope / risk of breaking other consumers), they are
   exposed via the new `FieldProvenance` dictionary instead, which also carries source/confidence
   for every field (something `pipeline.py`'s plain dict-of-dicts already did implicitly).
3. `FieldInferenceResult` in this port only carries `Value`/`Confidence`/`Strategy` (no
   `evidence`/`competing_candidates` runner-up detail) since nothing downstream in this codebase
   consumes that transparency data yet; can be added later if needed.

## Verification
- `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` succeeded: 0 errors, 35 warnings (all
  pre-existing/unrelated - SQLitePCLRaw NU1903 vulnerability, MAUI `Frame` obsolescence in generated
  XAML code, `CS8625` nullability in the untouched attachment-null-arg calls, `MVVMTK0045` AOT
  warnings in unrelated viewmodels).
- Ran a throwaway console project under `scratch/sanitycheck/` (deleted after use, `scratch/` is
  empty again) that fed synthetic HTML mimicking `project-meta-panel`/`profile_card`/`skills`/
  `projectDetailsTab`/attachment shapes through `DetailParser.Parse`. Confirmed: no exceptions;
  structural fields (`project_status`, `budget`, `duration`, `hire_rate`, `in_progress_count`)
  resolved correctly with `source=structural, confidence=1`; fields absent from the synthetic page
  (`published_date`, `registration_date`, `open_projects_count`, `ongoing_conversations`) fell back
  to low-confidence `inference` guesses (as expected - no label for them exists in the synthetic
  HTML, so inference only sees unrelated number candidates); `started_since`/`deal_date`/
  `delivery_date` were correctly nulled to `null, source=none` even though `project_status ==
  "مكتمل"`, because their Arabic labels are not literally present in the page text (rule 1 of the
  nullable-by-design enforcement fired correctly); attachments/skills/description/title all resolved
  correctly.

## Not done / follow-ups
- No real captured HTML fixtures exist in the repo, so no live A/B comparison against the actual
  Python output was possible (confirmed by a prior step) - only synthetic-HTML sanity checking was
  performed, per the task's acceptance criteria.

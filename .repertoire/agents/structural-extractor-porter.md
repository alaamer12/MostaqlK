# structural-extractor-porter — report

## Goal
Implement Step 1 of the V1 implementation plan: add HTML parsing support and port the Python
`analyzer.py` structural + label-driven extraction logic to C#, replacing the stub
`ListingParser`/`DetailParser`.

## Actions taken

1. Added `HtmlAgilityPack` (1.12.4) as a `PackageReference` to `MostaqlK.csproj` via
   `dotnet add package HtmlAgilityPack`.
2. Created `Infrastructure/Http/Parsers/StructuralExtractor.cs` (namespace
   `MostaqlK.Infrastructure.Http.Parsers`), porting from `analyzer.py`:
   - `KnownLabels` — same Arabic label list as `KNOWN_LABELS`.
   - `Normalize(string?)` — whitespace-collapsing helper mirroring Python's `normalize()`.
   - `ExtractMetaFields(HtmlNode)` — mirrors `structural_meta_extract`: reads
     `div#project-meta-panel`/`div.meta-container` → `div.meta-row` → `div.meta-label`/`div.meta-value`,
     then `div.profile_card` → `table.table` rows with exactly 2 `<td>`s.
   - `FindLabelElements`/`WalkToValue`/`LabelDrivenExtract` — mirror `find_label_elements`,
     `walk_to_value`, `label_driven_extract` exactly (own-text match, leaf full-text match,
     next-sibling/next-td/parent-next-sibling/parent-text-minus-label walk).
   - `ExtractAttachments`/`AttachmentFromLink` — mirror `extract_attachments`/
     `_attachment_from_link`: resolves filename (title attr or text), `data-file-type`, an
     `ext-file`-class `<bdi>` badge scoped to the nearest ancestor `<li>` whose class contains
     "attachment" (falling back to the immediate `<li>`/parent), and filename-suffix extension
     via regex; skips links with no recognized extension AND no explicit file-type/badge signal;
     detects `requires_auth` via `/register`/`/login` in href and nulls the resolved URL while
     keeping `raw_url`; dedupes by raw URL or filename.
   - New `AttachmentCandidate` record (Filename, Extension, Url, RawUrl, RequiresAuth, SizeText)
     defined in the same file, used as an internal DTO before mapping into `Models.Asset`.
   - **Simplification vs Python original**: instead of Python's full `mimetypes` DB, uses a
     small hand-picked `KnownFileExtensions` set (documented in a code comment) covering the
     extensions realistically seen on Mostaql pages (docx, doc, pdf, zip, rar, 7z, xlsx, xls,
     pptx, ppt, psd, ai, png, jpg, jpeg, gif, svg, txt, csv, json, sql, sketch, fig, mp4, mp3, rtf).
3. Implemented `ListingParser.Parse(string html)`:
   - Loads HTML via `HtmlDocument`, looks for `tr.project-row`, falling back to `div.project-item`
     per analyzer.py's `projects_list` branch.
   - For each row: title/url from `h2 > a` (falls back to any `<a>`), and client
     name/posted-relative/proposal-count parsed defensively from `ul.project__meta > li` items
     (order-based heuristic, proposal count detected via Arabic "عرض/عروض/تسليم" keywords).
   - **Documented assumption**: no explicit `data-project-id` attribute was observed in the
     sample HTML available to this task, so `ProjectId` is parsed from the trailing numeric
     segment of the project URL (`.../...-<id>` or `.../<id>`), defaulting to 0 if absent. This
     is flagged in a code comment as an assumption to revisit once real listing HTML is available.
   - Throws `ParseException` only when the HTML is empty/whitespace, or when no `tr.project-row`
     AND no `div.project-item` elements exist at all (structure changed drastically); otherwise
     returns whatever rows it could parse (possibly an empty list), matching the "nullable per
     project" philosophy from the Python original.
4. Implemented `DetailParser.Parse(long projectId, string html)`:
   - Loads HTML, extracts `title` (`h1` — throws `ParseException` only if entirely missing),
     `description` (`div.text-wrapper-div` or `div#projectDetailsTab`), and `skills`
     (`ul.skills > li` text + optional link), directly via HtmlAgilityPack, mirroring
     `analyze_file`'s project_ branch.
   - Calls `StructuralExtractor.ExtractMetaFields` + `LabelDrivenExtract` +
     `ExtractAttachments`, then does the **simple prefer-structural-else-label-driven merge**
     (structural wins if present, else falls back to label-driven) for `Budget` and
     `DeliveryDays` (parsed as leading int from the merged text) and Owner's
     `HiringRatePercent`/`CompletedProjectsCount`. Owner name comes from
     `div.profile_card h5.profile__name` first, else the merged `صاحب المشروع` label.
   - Attachments are mapped from `AttachmentCandidate` into `Models.Asset` (new fields below).
   - Left an explicit `// TODO(Step 2): replace with InferenceEngine cross-validation
     combinator per pipeline.py` comment at the merge point, as required — the full
     robustness/cross-check verdict system (`cross_check_robustness` in Python) was
     intentionally NOT ported; that belongs to the separate future Step 2 task.
5. `ParseException` usage kept consistent with the "nullable per field" philosophy: only thrown
   for genuinely unparseable HTML (empty input, or a completely missing title/h1, or a listing
   page with neither known row shape present) — individually-missing optional fields
   (budget, delivery days, owner stats, attachments, skills, meta) are simply left null/empty.

## Files created/modified

- **Created**: `Infrastructure/Http/Parsers/StructuralExtractor.cs`
- **Modified**: `Infrastructure/Http/Parsers/ListingParser.cs` (full rewrite, real parsing logic)
- **Modified**: `Infrastructure/Http/Parsers/DetailParser.cs` (full rewrite, real parsing logic)
- **Modified**: `MostaqlK.csproj` (added `PackageReference` for `HtmlAgilityPack` 1.12.4)
- **Modified**: `Models/Asset.cs` — added four new nullable properties needed to carry
  attachment-parsing metadata through to the model, since the stub DTO only had
  `AssetId`/`ProjectId`/`FileName`/`Url`/`LocalPath`/`SizeBytes`:
  - `string? Extension` — resolved file extension (docx/pdf/etc.)
  - `string? RawUrl` — original unresolved href, kept even when `Url` is nulled by `RequiresAuth`
  - `bool RequiresAuth` — true when the link only resolves behind `/register`/`/login`
  - `string? SizeText` — human-readable size text as displayed on the page (e.g. "(15.99KB)")

`ParseException.cs` was read but not modified — its existing shape (message + optional inner
exception) was already sufficient.

## Key decisions / simplifications vs the Python original

- File-extension recognition uses a small hand-picked list instead of Python's full `mimetypes`
  database (documented in-code); this could occasionally miss an exotic extension but covers all
  extensions realistically expected on Mostaql project pages.
- The full `cross_check_robustness` (STRUCTURAL vs LABEL-DRIVEN agreement/verdict system) was
  intentionally NOT ported — per the task's explicit scope, only the simpler
  prefer-structural-else-label-driven merge was implemented in `DetailParser`, with the required
  TODO comment marking where the Step 2 `InferenceEngine` combinator will replace it.
- `ListingParser`'s exact list-item HTML shape (project id source, meta item ordering) was not
  available as a concrete sample file in this repo at the time of implementation, so the id
  extraction (via URL trailing digits) and meta-item interpretation (positional heuristic +
  Arabic-keyword detection for proposal count) are clearly-commented assumptions based on
  `analyzer.py`'s `h2 > a` / `ul.project__meta` structure. These should be revisited/tightened
  once real `projects_list.html` samples are available for testing.
- No automated tests were added in this session (no HTML fixture files were available in the
  repo to build reproduction tests against); verification was via a successful `dotnet build`
  only. A follow-up task should add unit tests using the actual sample HTML files referenced in
  `analyzer.py`'s docstring (`projects_list.html`, `project_*.html`) if/when those become
  available in this C# repo.

## Verification

- `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` — **succeeded, 0 errors**
  (only pre-existing, unrelated `MVVMTK0045` warnings about `[ObservableProperty]` AOT
  compatibility in various ViewModels, not touched by this task).
- No `Services/`, `UI/`, `Features/`, or `Infrastructure/Database|Notifications` files were
  touched, per the acceptance criteria.

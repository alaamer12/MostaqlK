# ParserTests

Headless, offline test harness for the Mostaql HTML parsers
(`Infrastructure/Http/Parsers/*`). It is the C# counterpart of the Python prototype's
`.repertoire/progress/python/parser/scratch/test_analyzer.py`, and intentionally stricter:
the Python tests asserted "the field was found", these assert the exact parsed value.

## Run

```powershell
.\scripts\test-parser.ps1                     # all fixture checks (offline, ~2s)
dotnet run --project tools\ParserTests        # same thing, directly

# Parse a real page end-to-end and dump every field + its provenance:
dotnet run --project tools\ParserTests -- --live "https://mostaql.com/project/1268113-..."

# ...as a logged-in user (3rd arg = cookie file), which also downloads every attachment:
dotnet run --project tools\ParserTests -- --live "https://mostaql.com/project/1268152-..." cookies.txt
```

Exit code is non-zero if any check fails, so it is safe to wire into CI. It is also run
automatically at the start of `scripts\test.ps1`.

## Live mode and cookies

Attachments are the one thing that **cannot** be verified anonymously: Mostaql renders a
logged-out visitor a `/register?...` stub instead of the real `/file/{id}/...` URL, so an
anonymous parse can only ever produce a manual-download placeholder. Passing a cookie file
makes live mode send it on the *page* fetch, print each attachment's resolved URL, and save
the bytes under `bin/.../attachments` (reporting `AUTHFAIL` if the server returns an HTML
login page instead of a file).

The cookie file may be either a Netscape/curl export or plain `name=value` lines copied from
DevTools — both are handled by `Infrastructure/Http/CookieJar.cs`, which is shared with the
app's `MostaqlScraper` and `AssetDownloadService`. When no path is given, it falls back to
`MOSTAQL_COOKIE`, `MOSTAQL_COOKIE_FILE`, then a `cookies.txt` found by walking up from the
working directory. `cookies.txt` is git-ignored — never commit a real session.

## Fixtures

| Fixture | What it proves |
|---|---|
| `project_current_markup.html` | The structural (class/id) fast path still works against today's real Mostaql markup, including description paragraph preservation and `/register` attachment links. |
| `project_renamed_markup.html` | **The robustness claim.** Same project data, every class/id renamed, no `h1`, no `ul.skills`, no `.profile_card`, no `.text-wrapper-div`, labels carrying colons/spelling variants, values in Arabic-Indic numerals. Every field must still come out identical to the fixture above. |
| `project_adversarial_redesign.html` | The Python prototype's own adversarial page (synonym labels, words split across spans, deeply nested wrappers, reordered DOM, decoy numbers) — must reach the inference engine and must not invent completed-only fields. |
| `projects_list.html` | `ListingParser` row parsing and project-id extraction, including a slug that itself ends in a number. |

## Adding a case

When a real page breaks the parser, add the smallest reproducing markup to the fixture that
matches the failure mode (or add a new fixture), assert the expected value, watch it fail,
then fix the parser. Do not weaken an existing assertion to make a run go green.

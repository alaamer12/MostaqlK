# nafezly-capture-implementer — A2 report

## Goal
Task A2 (plan `fresh-narrows-gull.md`, requirements §8.3): capture nafezly.com DOM truth as raw
fixtures + expected-field manifest under `tools/ParserTests/Fixtures/nafezly/`, feeding the A3
`NafezlyParseProfile`. Write targets strictly limited to that fixture dir + this report + scratch.

## Method
- Read the `capture` skill (`SKILL.md` + `references/dom.md`) in full. Its raw-HTML modes were
  judged unfit or forbidden for this contract: `dom` = headless Playwright (explicitly forbidden by
  §8.3), `website` = asset mirror, `design` = curl but single-site analysis dumps. Used the
  task-sanctioned fallback: **plain HTTPS GET via curl 8.10.1** with the app's exact
  `MostaqlSource.UserAgent` (Chrome/126), `Accept`/`Accept-Language` headers, `--compressed`,
  forced UTF-8. No headless browser was ever needed (probe returned 200 / `charset=UTF-8`).
- Two scratch Python drivers (`scratch/a2_capture_nafezly.py`, `scratch/a2_recapture.py`) fetched
  the 6-URL capture set + analyzed fixtures; **all deleted after the run**.

## Key findings / incidents
- **Cloudflare 1015 (HTTP 429)** hit on the two last detail URLs during an unpaced burst of 8
  requests; re-fetched after 90 s cooldown + 40 s pacing → both 200. Recorded in manifest + metas.
- **Owner's 56515 slug was a mistranscription** (`تعمل`/`بالاي` vs real `لعمل`/`بالai`). Recovered the
  real URL from listing **page 3** (kept `listing-page3-probe.html` + meta as auxiliary evidence).
- **State shift truth**: `detail-open.html` (56540) is actually `تحت التنفيذ`, `detail-in-progress.html`
  (56515) is `مكتمل`, `detail-extra.html` (56573) is `مفتوح`. Filenames kept per task spec; manifest
  records true states (the 3 pages incidentally cover the 3 live states).
- State vocabulary: `مفتوح`/`تحت التنفيذ`/`مكتمل` with badge colors `#28a745`/`#007bff`/`#2a2a2a`;
  `مغلق` never observed.
- Detail exposes JSON-LD `JobPosting` (title/skills/description/datePosted with embedded whitespace,
  `workHours` actually days), meta panel `بطاقة المشروع` label→value rows (`حالة المشروع`, `تاريخ النشر`,
  `المدة المتاحة`, `الميزانية`, `عدد المتقدمين`), budget `10 - 25 $` (LTR-in-RTL), skills block
  ` مهارات مطلوبة` with `a[href^=/projects/skill/]`, offers fully server-rendered (`offer-order` divs
  count == عدد المتقدمين), owner/executor via app-gated anchors (`js-profile-app-gate`).
- **Absent in anonymous HTML** (encoded as nullable, not guessed): executor rating number, successful-
  projects count, supervised/verified flag (JS handler exists but control unrendered), attachments
  section (only offer-modal uploader), profile URLs. `سعر الخدمة` belongs to service cards, not projects.
- Listing grammar: 20 × `div.project-box` rows, `^https://nafezly\.com/project/(\d+)-` id extraction,
  meta icons (`fa-usd-circle`, `fa-business-time`, `fa-ballot` "N عروض", `fa-clock`, `fa-check-circle`
  state), prev/next-only pagination echoing full encoded query, no result count, newest-first.
- Filter effect: no chips/counts; only `checked` specialize inputs, `selected` skills options, echoed
  params, and budget-shifted rows. All seven `specialize` slugs confirmed against the page's own
  Arabic labels (quoted in manifest §3).
- Encoding: `lang="ar" dir="rtl"`, UTF-8 clean, zero mojibake markers across all 7 files.

## Files touched (only allowed paths)
- `tools/ParserTests/Fixtures/nafezly/` — 7 `.html` + 7 `.meta.json` (6 capture-set + page3 probe,
  all 200, 315 KB–489 KB) + `capture-manifest.md` (the deliverable: verbatim-markup analysis +
  A3 handoff vocabulary).
- `.repertoire/agents/nafezly-capture-implementer.md` — this report.

## Gates verification
- All 6 required fixtures exist, each ≥ 315 KB (none under the 5 KB error-page threshold); every
  status 200; `.meta.json` carries url/effective, method, UTC timestamp, status, byte size, sha256;
  UTF-8 validity + mojibake count recorded per file.
- Manifest complete with quoted markup excerpts (states, budget, proposals, publish time, skills,
  poster/executor, supervised absence evidence, attachments absence evidence, listing grammar,
  pagination diff, filter diff, encoding, handoff vocabulary list).
- No writes to `Infrastructure/ Sources/ Services/ Core/ UI/ Features/`; scratch scripts deleted;
  `git status` shows only the fixture dir + this report as new files.

## Skills used
`capture` (consulted and its sanctioned fallback followed).

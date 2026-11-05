using MostaqlK.Core.Sources;

namespace MostaqlK.Models;

/// <summary>
/// One append-only record of a single cookie-crawl attempt against one source's profile
/// (v4 R2.8). <see cref="Ok"/> distinguishes a usable read from a failed attempt; a failed
/// row is EVIDENCE about the attempt, never a profile write — the projection only ever reads
/// <c>ok = 1</c> rows, so a dead session (which answers HTTP 200, §3) cannot blank the profile.
/// </summary>
/// <param name="Id">The snapshot number the UI quotes («لقطة #14»); 0 before insert.</param>
/// <param name="FieldsJson">The parsed field set in the same derived shape manual entry produces
/// (R1.6). Public scraped facts ONLY — never a cookie or token. Null when <c>Ok</c> is false.</param>
/// <param name="ErrorKind">One of session_expired | waf_challenge | not_found | network | parse_empty;
/// null when <c>Ok</c> is true. <c>waf_challenge</c> is first-class so Mostaql's /u/ block is a known
/// outcome, not a mis-reported empty profile (D33/D53).</param>
public sealed record ProfileSnapshot(
    long Id,
    SourceId Source,
    bool Ok,
    string? FieldsJson,
    string? ErrorKind,
    int? HttpStatus,
    long? BodyBytes,
    int? PagesRead,
    DateTimeOffset CrawledAt);

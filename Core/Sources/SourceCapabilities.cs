namespace MostaqlK.Core.Sources;

/// <summary>
/// What a source's pages can yield. UI and persistence consult these flags instead of
/// branching on a source name — a missing capability means the section/field is simply
/// never rendered, never rendered empty (backend-ui-contract / v4 R6.9).
/// <para>
/// The four winnability flags are additive and default to <c>false</c>, so existing sources
/// keep compiling untouched and a source only claims a capability once the parser that actually
/// reaches it has landed. Claiming a capability before its extraction exists would render an
/// empty section — the exact failure this record is built to prevent.
/// </para>
/// </summary>
/// <param name="HasExecutorInfo">The detail page names who will run the project.</param>
/// <param name="HasSupervisedFlag">The site exposes a supervised/escrowed marker (false on both current sources — see v4 §4.5).</param>
/// <param name="NeedsSessionForFullEnrichment">A detail fetch is materially richer with a session cookie.</param>
/// <param name="HasBidderRows">Per-project bidder/offer rows are extractable (v4 R3.4); drives the bidders surface.</param>
/// <param name="HasOwnProfileRead">The viewer's own profile is scrapable on this site (v4 R1); drives the profile surface.</param>
/// <param name="HasOutcomeHistory">Past win/loss outcomes are available to feed the band win-rate factor (v4 C1).</param>
/// <param name="HasWafTokenPath">Reaching this site's data needs a WAF token bootstrap (v4 R1.10) — a source fact, so the token decision stays module-side and never becomes a name check in shared HTTP.</param>
public sealed record SourceCapabilities(
    bool HasExecutorInfo,
    bool HasSupervisedFlag,
    bool NeedsSessionForFullEnrichment,
    bool HasBidderRows = false,
    bool HasOwnProfileRead = false,
    bool HasOutcomeHistory = false,
    bool HasWafTokenPath = false);

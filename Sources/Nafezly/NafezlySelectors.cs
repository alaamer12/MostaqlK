using MostaqlK.Core.Parsing;

namespace MostaqlK.Sources.Nafezly;

/// <summary>
/// The Nafezly module's DOM selector vocabulary: every class/id/attribute/path/regex fragment the
/// shared <c>Core/Parsing</c> engines and this module's thin parsers match against, as immutable
/// data derived from the A2 capture manifest
/// (<c>tools/ParserTests/Fixtures/nafezly/capture-manifest.md</c>). A nafezly redesign that only
/// renames selectors is a one-line edit here.
/// </summary>
/// <remarks>
/// Sentinels: several <see cref="SelectorMap"/> members exist to serve Mostaql's owner-card /
/// attachment / details-tab shapes, which nafezly does not use (its owner block is app-gated and
/// its anonymous pages carry no public attachment list — capture-manifest §1.9-§1.11). Those
/// members are given a value that cannot match anywhere on a nafezly page, so the shared engine's
/// owner-card / attachment lookups resolve to nothing and the thin nafezly parser extracts the
/// equivalent facts directly instead. No engine behavior is touched.
/// </remarks>
public static class NafezlySelectors
{
    public static SelectorMap Selectors { get; } = new(
        // The detail meta panel ("بطاقة المشروع") has no stable id — a guaranteed-absent sentinel
        // makes ExtractMetaFields skip the id lookup and use the scoped container-class fallback.
        MetaPanelId: "nafezly-meta-panel-sentinel",
        // Scoped within the بطاقة المشروع card (see NafezlyDetailParser): the rows container is a
        // div.col-12.row and every label/value row is a div.col-12.row.
        MetaContainerClassSubstring: "row",
        MetaRowClassSubstring: "row",
        // Label cell carries "text-right"; the value cell is the only one also carrying "font-1".
        MetaLabelClassSubstring: "text-right",
        MetaValueClassSubstring: "font-1",
        // Nafezly owner stats are app-gated (no server-rendered stats table), so the owner-card
        // scan is neutralised with unmatched sentinels and the thin parser reads the name directly.
        OwnerStatsTableClassSubstring: "nafezly-owner-stats-unused",
        OwnerStatRowClassSubstring: "nafezly-owner-row-unused",
        OwnerCardClassSubstrings: [],
        OwnerCardWrapperClassSubstrings: [],
        OwnerNameClassSubstring: "nafezly-owner-name-unused",
        OwnerUserIdAttribute: "data-owner-id",
        // Details-tab / description class: nafezly's brief lives in the JSON-LD description + the
        // body prose, so the shared tab-scoped lookup is neutralised and the thin parser extracts
        // the description itself.
        DetailsTabId: "nafezly-details-tab-unused",
        ProjectDetailsClassSubstring: "nafezly-project-details-unused",
        DescriptionClassSubstring: "nafezly-desc-unused",
        // Skills: identifier-blind recognition is by the skill-taxonomy href (class-driven <ul>
        // fallback is unused because nafezly renders skill pills as loose anchors, not a list).
        SkillsListClassSubstring: "nafezly-skills-list-unused",
        SkillHrefSubstrings: ["/projects/skill/"],
        // Nafezly has no /u/{username} profile URLs (every person anchor is javascript:void(0) +
        // js-profile-app-gate) — a sentinel that never appears keeps the shared profile-link sniff
        // from grabbing an unrelated href.
        ProfileUrlPathSegment: "nafezly-profile-path-unused",
        ProfileUrlSegmentToken: "nafezly",
        // Proposal rows carry a data-score attribute (capture-manifest §1.6); the deterministic
        // offer count is what the thin parser uses. Unused by the shared engines.
        BidItemAttribute: "data-score",
        // Anonymous nafezly pages have no public attachment list (capture-manifest §1.11), so the
        // shared attachment-link scanner is neutralised; NafezlyDetailParser returns none.
        AttachmentItemClassSubstring: "nafezly-attachment-unused",
        AttachmentFileTypeAttribute: "data-file-type-unused",
        AttachmentBadgeClassSubstring: "nafezly-ext-badge-unused",
        // Listing rows are the 20 div.project-box cards per page (exact class in capture-manifest §2).
        ListingRowClass: "project-box",
        ListingItemClass: "nafezly-listing-item-unused",
        ListingMetaClass: "nafezly-listing-meta-unused",
        ListingBriefClass: "naskh",
        // Proposals meta item is signalled by the fa-ballot icon (the span-class signals are the
        // Mostaql redesign shapes and never appear on nafezly).
        ProposalIconSpanClass: "nafezly-proposal-span-unused",
        ProposalIconElementClass: "fa-ballot",
        // Project link href shape + id regex (slug→id: the first digits after /project/).
        ProjectHrefPathSegment: "/project/",
        ProjectIdUrlPattern: @"/project/(\d+)");
}

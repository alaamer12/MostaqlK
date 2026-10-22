using MostaqlK.Core.Parsing;

namespace MostaqlK.Sources.Mostaql;

/// <summary>
/// The Mostaql module's DOM selector vocabulary: every class/id/attribute/path/regex fragment
/// the shared <c>Core/Parsing</c> engines and this module's thin parsers match against, as
/// immutable data. Every value here is byte-copied from the constants/inline literals the
/// parsers carried before generalization (A1) — a Mostaql redesign that only renames selectors
/// is a one-line edit in this file.
/// </summary>
public static class MostaqlSelectors
{
    public static SelectorMap Selectors { get; } = new(
        // Was the inline XPaths/attr names in StructuralExtractor.ExtractMetaFields.
        MetaPanelId: "project-meta-panel",
        MetaContainerClassSubstring: "meta-container",
        MetaRowClassSubstring: "meta-row",
        MetaLabelClassSubstring: "meta-label",
        MetaValueClassSubstring: "meta-value",
        // Was the inline owner-card lookups in ExtractMetaFields / FindOwnerCard / DetailParser.
        OwnerStatsTableClassSubstring: "table",
        OwnerStatRowClassSubstring: "justify-between",
        OwnerCardClassSubstrings: ["profile_card", "profile-card"],
        OwnerCardWrapperClassSubstrings: ["card", "box"],
        OwnerNameClassSubstring: "name",
        OwnerUserIdAttribute: "data-user-id",
        // Was the inline details-area lookups repeated across the three parsers.
        DetailsTabId: "projectDetailsTab",
        ProjectDetailsClassSubstring: "project-details",
        DescriptionClassSubstring: "text-wrapper-div",
        // Was the inline skills lookups in DetailParser.ExtractSkills / LooksLikeSkillHref.
        SkillsListClassSubstring: "skills",
        SkillHrefSubstrings: ["/skills/", "skill=", "/tag/"],
        // Was the inline user-profile link sniff ("/u/" href, "u" path token).
        ProfileUrlPathSegment: "/u/",
        ProfileUrlSegmentToken: "u",
        // Was the inline DetailParser.CountBidItems attribute probe.
        BidItemAttribute: "data-bid-item",
        // Was the inline attachment sniff in StructuralExtractor.AttachmentFromLink.
        AttachmentItemClassSubstring: "attachment",
        AttachmentFileTypeAttribute: "data-file-type",
        AttachmentBadgeClassSubstring: "ext-file",
        // Was the inline ListingParser row/card/meta/brief XPaths.
        ListingRowClass: "project-row",
        ListingItemClass: "project-item",
        ListingMetaClass: "project__meta",
        ListingBriefClass: "project__brief",
        ProposalIconSpanClass: "hsoub-file-signature-icon",
        ProposalIconElementClass: "fa-users",
        // Was the inline ListingParser project-link fallback + ProjectIdRegex pattern.
        ProjectHrefPathSegment: "/project/",
        ProjectIdUrlPattern: @"/project/(\d+)");
}

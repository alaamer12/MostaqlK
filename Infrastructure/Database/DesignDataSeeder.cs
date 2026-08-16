using MostaqlK.Core;
using MostaqlK.Models;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Replaces the local project store with the exact dataset the MVP mockups are drawn against
/// (<c>.repertoire/design/mvp/projects.html</c> and <c>project-details.html</c>), so a
/// design-parity capture compares real SQLite-backed rendering against the design instead of
/// whatever the live scraper happened to fetch.
/// <para>
/// Invoked from <c>App</c> when the process is started with <c>--seed-design-data</c>. It writes
/// through the normal repository layer (no bespoke SQL), and is idempotent: every run clears the
/// project tables first, so re-running produces byte-identical rows.
/// </para>
/// </summary>
public sealed class DesignDataSeeder
{
    /// <summary>Startup argument that triggers a reseed. See <c>App</c>.</summary>
    public const string StartupArgument = "--seed-design-data";

    /// <summary>
    /// Preference key remembering that the store currently holds design-parity data. While it is
    /// set the polling pipeline stays offline, so a later capture run (which passes only
    /// <c>--default-page</c>/<c>--theme</c>) cannot have the seeded rows buried by freshly
    /// scraped projects. Pass <c>--seed-design-data=off</c> to clear it and restore live polling.
    /// </summary>
    public const string PreferenceKey = "design_parity_mode";

    /// <summary>Status-bar total in projects.html: "147 مشروع متتبَّع".</summary>
    private const int TrackedCount = 147;

    /// <summary>Sidebar stat card in projects.html: "مشاريع مضافة اليوم" = 12.</summary>
    private const int AddedTodayCount = 12;

    /// <summary>
    /// Header rate readout in projects.html: "12 طلب / دقيقة". This is a stored setting, not a
    /// live measurement — <c>ProjectFeedViewModel</c> reads it from the same preference key the
    /// settings page writes, falling back to the rate limiter's own capacity (10) when unset,
    /// which is why an unseeded run showed "10". Seeding the preference lines the two up.
    /// </summary>
    private const int RequestsPerMinute = 12;

    /// <summary>Mirrors <c>ProjectFeedViewModel.KeyMaxRequestsPerMinute</c> / the settings page.</summary>
    private const string MaxRequestsPerMinutePreferenceKey = "settings_max_requests_per_minute";

    /// <summary>The two projects.html feed cards plus the project-details.html project.</summary>
    private const int DesignCardCount = 3;

    private const int ArchivedCount = TrackedCount - DesignCardCount;

    private const long ArchivedIdBase = 1200000;

    private readonly IProjectRepository _projectRepository;
    private readonly IOwnerRepository _ownerRepository;

    public DesignDataSeeder(IProjectRepository projectRepository, IOwnerRepository ownerRepository)
    {
        _projectRepository = projectRepository;
        _ownerRepository = ownerRepository;
    }

    /// <summary>
    /// Reads <c>--seed-design-data</c> / <c>--seed-design-data=off</c> out of the process
    /// arguments. Returns <c>null</c> when the flag is absent (leave the store untouched).
    /// </summary>
    public static bool? ParseArguments(string[] args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith(StartupArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = arg.Length > StartupArgument.Length ? arg[(StartupArgument.Length + 1)..] : string.Empty;
            return !value.Equals("off", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    public async Task<Result<int>> SeedAsync(CancellationToken cancellationToken = default)
    {
        Microsoft.Maui.Storage.Preferences.Set(MaxRequestsPerMinutePreferenceKey, RequestsPerMinute);

        var cleared = await _projectRepository.ClearAllAsync(cancellationToken);
        if (!cleared.IsOk)
        {
            return Result<int>.Err(cleared.Error);
        }

        // Cards render newest-first (ORDER BY discovered_at DESC), so the anchor timestamps are
        // laid out to reproduce the mockup's order: card 1 is the most recent.
        var now = DateTimeOffset.UtcNow;
        var seeded = 0;

        foreach (var project in BuildProjects(now))
        {
            var ownerResult = await _ownerRepository.UpsertAsync(project.Owner, cancellationToken);
            if (!ownerResult.IsOk)
            {
                return Result<int>.Err(ownerResult.Error);
            }

            var summaryResult = await _projectRepository.InsertSummaryAsync(project.Summary, cancellationToken);
            if (!summaryResult.IsOk)
            {
                return Result<int>.Err(summaryResult.Error);
            }

            var detailsResult = await _projectRepository.UpsertDetailsAsync(project.Details, cancellationToken);
            if (!detailsResult.IsOk)
            {
                return Result<int>.Err(detailsResult.Error);
            }

            seeded++;
        }

        foreach (var archived in BuildArchivedHistory(now))
        {
            var archivedResult = await _projectRepository.InsertSummaryAsync(archived, cancellationToken);
            if (!archivedResult.IsOk)
            {
                return Result<int>.Err(archivedResult.Error);
            }

            seeded++;
        }

        return Result<int>.Ok(seeded);
    }

    /// <summary>
    /// Deletes only the rows this seeder itself would have written — the archived
    /// <c>"مشروع سابق"</c> range (<see cref="ArchivedIdBase"/>..<c>ArchivedIdBase + ArchivedCount - 1</c>),
    /// the three design-card projects (<c>1300000</c>-<c>1300002</c>) and their matching seed
    /// owners (<c>9300000</c>-<c>9300002</c>) — through the real repository layer, leaving any
    /// other (real, scraped) rows untouched. Invoked from <c>App</c> when the process is started
    /// with <c>--seed-design-data=off</c>, so turning the flag off on a mixed store actually
    /// cleans it up instead of merely disabling the preference.
    /// </summary>
    [TraceInteraction("DesignDataSeeder.PurgeSeededRows")]
    public async Task<Result<int>> PurgeSeededRowsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = TraceScope.Begin("DesignDataSeeder.PurgeSeededRows");

        var archivedMinId = ArchivedIdBase;
        var archivedMaxId = ArchivedIdBase + ArchivedCount - 1;
        const long DesignCardMinId = 1300000;
        const long DesignCardMaxId = 1300000 + DesignCardCount - 1;
        const long SeedOwnerMinId = 9300000;
        const long SeedOwnerMaxId = 9300000 + DesignCardCount - 1;

        var archivedResult = await _projectRepository.DeleteByProjectIdRangeAsync(archivedMinId, archivedMaxId, cancellationToken);
        if (!archivedResult.IsOk)
        {
            scope.MarkFaulted(new InvalidOperationException(archivedResult.Error.ToString()));
            return Result<int>.Err(archivedResult.Error);
        }

        var designCardsResult = await _projectRepository.DeleteByProjectIdRangeAsync(DesignCardMinId, DesignCardMaxId, cancellationToken);
        if (!designCardsResult.IsOk)
        {
            scope.MarkFaulted(new InvalidOperationException(designCardsResult.Error.ToString()));
            return Result<int>.Err(designCardsResult.Error);
        }

        var ownersResult = await _ownerRepository.DeleteByIdRangeAsync(SeedOwnerMinId, SeedOwnerMaxId, cancellationToken);
        if (!ownersResult.IsOk)
        {
            scope.MarkFaulted(new InvalidOperationException(ownersResult.Error.ToString()));
            return Result<int>.Err(ownersResult.Error);
        }

        var purgedProjectRows = archivedResult.Value + designCardsResult.Value;
        InteractionLogger.Mark(
            "DesignDataSeeder.PurgeSeededRows.Completed",
            "A",
            new { purgedProjectRows, purgedOwnerRows = ownersResult.Value });

        return Result<int>.Ok(purgedProjectRows);
    }

    /// <summary>
    /// Older, already-read rows that make the status-bar totals match projects.html
    /// ("147 مشروع متتبَّع" / "1 غير مقروء") and the sidebar's "مشاريع مضافة اليوم" = 12.
    /// They sort below the two design cards, so they only exist as scroll history and never
    /// occupy the visible viewport.
    /// </summary>
    private static IEnumerable<ProjectSummary> BuildArchivedHistory(DateTimeOffset now)
    {
        for (var index = 0; index < ArchivedCount; index++)
        {
            // The first ten stay inside today's date so "added today" totals 12 with the two cards.
            var discoveredAt = index < AddedTodayCount - 2
                ? now.AddMinutes(-20 - index)
                : now.AddDays(-1 - ((index - (AddedTodayCount - 2)) / 20));

            yield return new ProjectSummary
            {
                ProjectId = ArchivedIdBase + index,
                Title = "مشروع سابق",
                Url = $"https://mostaql.com/project/{ArchivedIdBase + index}",
                ClientName = "عميل",
                PublishTimeNumber = 0,
                PublishTimeText = string.Empty,
                ProposalCount = 0,
                IsUnread = false,
                EnrichmentStatus = EnrichmentStatus.Enriched,
                DiscoveredAt = discoveredAt,
            };
        }
    }

    private static IEnumerable<SeedProject> BuildProjects(DateTimeOffset now)
    {
        // ---- Card 1 in projects.html: unread, enriched. ----
        yield return Build(
            projectId: 1300001,
            title: "تصميم موقع تعليمي تفاعلي",
            description: "أحتاج إلى تصميم وتطوير موقع تعليمي تفاعلي للدورات اونلاين مع لوحة تحكم للمدرب والطلاب، يدعم المحتوى المرئي والاختبارات والشهادات.",
            skills: ["تصميم واجهات", "تطوير ويب", "PHP", "MySQL"],
            ownerId: 9300001,
            ownerName: "أحمد العتيبي",
            postedRelative: "منذ 3 دقائق",
            budget: "2500 - 5500",
            deliveryDays: 20,
            proposalCount: 69,
            isUnread: true,
            status: EnrichmentStatus.Enriched,
            discoveredAt: now.AddMinutes(-3));

        // ---- Card 2 in projects.html: read, still pending enrichment (amber badge). ----
        yield return Build(
            projectId: 1300002,
            title: "كتابة محتوى تسويقي لمتجر إلكتروني",
            description: "مطلوب كتابة أوصاف منتجات احترافية وجذابة لمتجر إلكتروني متخصص في العناية بالبشرة مع تحسين المحتوى لمحركات البحث.",
            skills: ["كتابة محتوى", "سيو", "تسويق إلكتروني"],
            ownerId: 9300002,
            ownerName: "سارة المطيري",
            postedRelative: "منذ 8 دقائق",
            budget: "500 - 1000",
            deliveryDays: 7,
            proposalCount: 69,
            isUnread: false,
            status: EnrichmentStatus.Pending,
            discoveredAt: now.AddMinutes(-8));

        // ---- The project project-details.html is drawn against. It is deliberately older than
        // the two feed cards (so it sits below the fold on projects.html) but carries the most
        // recent `enriched_at`, which is what the details route opens when no id is supplied. ----
        yield return Build(
            projectId: 1300000,
            title: "تصميم وتطوير نظام SaaS لوكالات السياحة",
            description: DetailsDescription,
            skills:
            [
                "تطوير قاعدة بيانات", "تطوير البرمجيات", "تطوير الويب", "SaaS",
                "خدمات الويب", "تطوير الويب الكامل", "تصميم مواقع", "برمجة مواقع",
            ],
            ownerId: 9300000,
            ownerName: "مشعل ا.",
            postedRelative: "منذ 8 ساعات",
            budget: "$1000.00 - $2500.00",
            deliveryDays: 60,
            proposalCount: 16,
            isUnread: false,
            status: EnrichmentStatus.Enriched,
            // Yesterday, so it neither reaches the visible feed nor inflates "added today"; the
            // "منذ 8 ساعات" wording comes from the stored posted_relative string, as it does for
            // every scraped row.
            discoveredAt: now.AddDays(-1).AddHours(-8),
            enrichedAt: now,
            hiringRatePercent: 23,
            attachments:
            [
                new Asset { FileName = "mockup-vendor-dashboard.png", Extension = "png" },
                new Asset { FileName = "نطاق-العمل.pdf", Extension = "pdf" },
            ]);
    }

    /// <summary>Verbatim description body from project-details.html § تفاصيل المشروع.</summary>
    private const string DetailsDescription = """
        السلام عليكم

        لدي فكرة نظام SaaS لانشاء مواقع للمشتركين معنا لعرض رحلات السفر الخاص بهم وامكانية حجزها عن طريق الموقع الالكتروني مع تواجد صفحات فرعية يمكن تعديلها لكل مشترك، كل مشترك يستطيع انشاء صب دومين على منصتنا او اختيار رابط خاص لحجزه. طبعا احتاج ادارة لاسعار الباقات والمزايا، وسوف يتم مشاركة واجهات المستخدم مع المبرمج بعد رؤية اعمال مشابهه للتسعير النهائي عبر المنصة.

        1. لوحة تحكم وكالات السياحة (Vendor Dashboard):
        • إدارة العروض: إمكانية إضافة وتعديل البكجات السياحية (مثل: رحلة فرنسا) مع تفاصيل الجدول الزمني والصور والأسعار.
        • إدارة الخدمات التكميلية: قسم خاص لإضافة خدمات إصدار التأشيرات، وتأجير السيارات، والتأمين السفر.
        • نظام التحليلات (Analytics): لوحة بيانات توضح إحصائيات العروض (عدد المشاهدات، عدد الإضافات للسلة، عدد الحجوزات المؤكدة).
        • إدارة الطلبات: نظام لاستقبال طلبات العملاء وتحديث حالتها (قيد المعالجة، مؤكد، ملغي).
        • إدارة الصفحات: اضافة صفحات مخصصة وتعديل محتويات الصفحات.

        3. لوحة تحكم الإدارة العامة (Admin Panel):
        • إدارة واعتماد الوكالات المسجلة.
        • التحكم في العمولات والتحويلات المالية.
        • إدارة محتوى المنصة العام.
        """;

    private static SeedProject Build(
        long projectId,
        string title,
        string description,
        string[] skills,
        long ownerId,
        string ownerName,
        string postedRelative,
        string budget,
        int deliveryDays,
        int proposalCount,
        bool isUnread,
        EnrichmentStatus status,
        DateTimeOffset discoveredAt,
        DateTimeOffset? enrichedAt = null,
        int? hiringRatePercent = null,
        Asset[]? attachments = null)
    {
        var url = $"https://mostaql.com/project/{projectId}";
        var owner = new Owner
        {
            OwnerId = ownerId,
            Name = ownerName,
            ProfileUrl = $"https://mostaql.com/u/{ownerId}",
            HiringRatePercent = hiringRatePercent,
        };

        return new SeedProject(
            owner,
            new ProjectSummary
            {
                ProjectId = projectId,
                Title = title,
                Url = url,
                ClientName = ownerName,
                PublishTimeNumber = 0, // Placeholder, will be updated by PublishedTimeUpdateService
                PublishTimeText = postedRelative,
                ProposalCount = proposalCount,
                Budget = budget,
                DeliveryDays = deliveryDays,
                SkillsText = string.Join(", ", skills),
                Description = description,
                IsUnread = isUnread,
                EnrichmentStatus = status,
                DiscoveredAt = discoveredAt,
            },
            new ProjectDetails
            {
                ProjectId = projectId,
                Title = title,
                Url = url,
                Description = description,
                Budget = budget,
                DeliveryDays = deliveryDays,
                ProjectStatus = "مفتوح",
                PublishTimeText = postedRelative,
                PublishTimeNumber = 0,
                Skills = [.. skills.Select(s => new ProjectSkill { Name = s })],
                Owner = owner,
                Attachments = [.. attachments ?? []],
                EnrichmentStatus = status,
                EnrichedAt = status == EnrichmentStatus.Enriched ? enrichedAt ?? discoveredAt : null,
            });
    }

    private sealed record SeedProject(Owner Owner, ProjectSummary Summary, ProjectDetails Details);
}

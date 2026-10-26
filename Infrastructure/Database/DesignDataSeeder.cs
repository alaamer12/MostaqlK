using MostaqlK.Core;
using MostaqlK.Core.Sources;
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
    /// Which mockup era's fixture set a <c>--seed-design-data</c> launch writes. The MVP
    /// single-source set (<see cref="SeedMode.Mvp"/>) is the historical behavior;
    /// <see cref="SeedMode.V3"/> writes the multi-source dataset drawn against
    /// <c>.repertoire/design/postmvp/v3-multisource/*.html</c> (both mostaql and nafezly rows).
    /// </summary>
    public enum SeedMode
    {
        Mvp,
        V3,
    }

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

    /// <summary>The two projects.html feed cards plus the project-details.html project.</summary>
    private const int DesignCardCount = 3;

    private const int ArchivedCount = TrackedCount - DesignCardCount;

    private const long ArchivedIdBase = 1200000;

    // ---- v3 (postmvp multi-source) fixture literals — see the wave W0 report for the
    // mockup-literal → seeded-entity mapping. Site-local ids; the nafezly band is applied by
    // the repositories themselves (SourceRegistry.ToInternalId). ----

    /// <summary>projects.html v3 shows 7 Mostaql cards; the rest of "98 مشروعًا من مستقل" is fill.</summary>
    private const int V3MostaqlVisibleCount = 7;

    private const int V3MostaqlTrackedCount = 98;

    /// <summary>projects.html "أُضيفت اليوم" = 12 for Mostaql (all 7 visible cards are from today).</summary>
    private const int V3MostaqlAddedToday = 12;

    private const long V3MostaqlCardIdBase = 1300101;

    private const long V3MostaqlOwnerIdBase = 9300101;

    private const long V3MostaqlFillIdBase = 1200100;

    /// <summary>projects.html SOURCE_META nafezly: "49 مشروعًا من نفذلي" / today = 5.</summary>
    private const int V3NafezlyVisibleCount = 2;

    private const int V3NafezlyTrackedCount = 49;

    private const int V3NafezlyAddedToday = 5;

    /// <summary>
    /// Synthetic Nafezly site-local ids (live nafezly ids are ~56k today; a 99xxxxxxx id cannot
    /// collide with a scraped row for years, so purge ranges stay honest about "seed-shaped only").
    /// </summary>
    private const long V3NafezlyCardIdBase = 990000001;

    private const long V3NafezlyOwnerIdBase = 991000001;

    private const long V3NafezlyFillIdBase = 990000003;

    /// <summary>Purge ranges covering EVERY seed-shaped row this class can write (MVP + v3).</summary>
    private const long MostaqlPurgeMinId = 1200000;

    private const long MostaqlPurgeMaxId = 1399999;

    private const long MostaqlOwnerPurgeMinId = 9300000;

    private const long MostaqlOwnerPurgeMaxId = 9300999;

    private const long NafezlyPurgeMinId = 990000000;

    private const long NafezlyPurgeMaxId = 990999999;

    private const long NafezlyOwnerPurgeMinId = 991000000;

    private const long NafezlyOwnerPurgeMaxId = 991999999;

    private readonly IProjectRepository _projectRepository;
    private readonly IOwnerRepository _ownerRepository;
    private readonly ISettingsRepository _settings;
    private readonly SourceRegistry _registry;

    public DesignDataSeeder(IProjectRepository projectRepository, IOwnerRepository ownerRepository, ISettingsRepository settings, SourceRegistry registry)
    {
        _projectRepository = projectRepository;
        _ownerRepository = ownerRepository;
        _settings = settings;
        _registry = registry;
    }

    /// <summary>
    /// Reads <c>--seed-design-data</c> / <c>--seed-design-data=off</c> out of the process
    /// arguments. Returns <c>null</c> when the flag is absent (leave the store untouched).
    /// Any explicit value other than <c>off</c>/<c>false</c> (including <c>=v3</c>) requests seeding.
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

    /// <summary>
    /// Which fixture set the <c>--seed-design-data</c> argument asks for: <c>=v3</c> selects
    /// <see cref="SeedMode.V3"/>, anything else that counts as a seed request selects
    /// <see cref="SeedMode.Mvp"/>. Returns <c>null</c> when no seeding is requested at all.
    /// </summary>
    public static SeedMode? ParseSeedMode(string[] args)
    {
        var requested = ParseArguments(args);
        if (requested != true)
        {
            return null;
        }

        foreach (var arg in args)
        {
            if (!arg.StartsWith(StartupArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Same bounds guard as ParseArguments: the bare flag has no '=' suffix at all.
            var value = arg.Length > StartupArgument.Length ? arg[(StartupArgument.Length + 1)..] : string.Empty;
            if (value.Equals("v3", StringComparison.OrdinalIgnoreCase))
            {
                return SeedMode.V3;
            }

            return SeedMode.Mvp;
        }

        return SeedMode.Mvp;
    }

    /// <summary>The v3 Nafezly identity (fixture data only — this seeder is parity harness
    /// infrastructure, its rows are explicitly the mockups' two platforms).</summary>
    private static SourceId V3Nafezly => SourceId.From("nafezly");

    public Task<Result<int>> SeedAsync(CancellationToken cancellationToken = default)
        => SeedAsync(SeedMode.Mvp, cancellationToken);

    public async Task<Result<int>> SeedAsync(SeedMode mode, CancellationToken cancellationToken = default)
    {
        return mode switch
        {
            SeedMode.V3 => await SeedV3Async(cancellationToken),
            _ => await SeedMvpAsync(cancellationToken),
        };
    }

    private async Task<Result<int>> SeedMvpAsync(CancellationToken cancellationToken)
    {
        _settings.Set(SourceId.Default, SettingsKeys.MaxRequestsPerMinute, RequestsPerMinute);

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
    /// Deletes only the rows this seeder itself would have written — every seed-shaped id range
    /// for both sources (the Mostaql range covers the MVP archived rows + design cards AND the
    /// v3 cards/fill; the Nafezly range covers the v3 synthetic fixture ids) — through the real
    /// repository layer, leaving any other (real, scraped) rows untouched. Invoked from
    /// <c>App</c> when the process is started with <c>--seed-design-data=off</c>, so turning the
    /// flag off on a mixed store actually cleans it up instead of merely disabling the preference.
    /// </summary>
    [TraceInteraction("DesignDataSeeder.PurgeSeededRows")]
    public async Task<Result<int>> PurgeSeededRowsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = TraceScope.Begin("DesignDataSeeder.PurgeSeededRows");

        var mostaqlResult = await _projectRepository.DeleteByProjectIdRangeAsync(SourceId.Mostaql, MostaqlPurgeMinId, MostaqlPurgeMaxId, cancellationToken);
        if (!mostaqlResult.IsOk)
        {
            scope.MarkFaulted(new InvalidOperationException(mostaqlResult.Error.ToString()));
            return Result<int>.Err(mostaqlResult.Error);
        }

        var nafezlySource = V3Nafezly;
        var nafezlyResult = await _projectRepository.DeleteByProjectIdRangeAsync(nafezlySource, NafezlyPurgeMinId, NafezlyPurgeMaxId, cancellationToken);
        if (!nafezlyResult.IsOk)
        {
            scope.MarkFaulted(new InvalidOperationException(nafezlyResult.Error.ToString()));
            return Result<int>.Err(nafezlyResult.Error);
        }

        var ownersResult = await _ownerRepository.DeleteByIdRangeAsync(SourceId.Mostaql, MostaqlOwnerPurgeMinId, MostaqlOwnerPurgeMaxId, cancellationToken);
        if (!ownersResult.IsOk)
        {
            scope.MarkFaulted(new InvalidOperationException(ownersResult.Error.ToString()));
            return Result<int>.Err(ownersResult.Error);
        }

        var nafezlyOwnersResult = await _ownerRepository.DeleteByIdRangeAsync(nafezlySource, NafezlyOwnerPurgeMinId, NafezlyOwnerPurgeMaxId, cancellationToken);
        if (!nafezlyOwnersResult.IsOk)
        {
            scope.MarkFaulted(new InvalidOperationException(nafezlyOwnersResult.Error.ToString()));
            return Result<int>.Err(nafezlyOwnersResult.Error);
        }

        var purgedProjectRows = mostaqlResult.Value + nafezlyResult.Value;
        InteractionLogger.Mark(
            "DesignDataSeeder.PurgeSeededRows.Completed",
            "A",
            new { purgedProjectRows, purgedOwnerRows = ownersResult.Value + nafezlyOwnersResult.Value });

        return Result<int>.Ok(purgedProjectRows);
    }

    /// <summary>
    /// Writes the v3 multi-source dataset the <c>postmvp/v3-multisource</c> mockups are drawn
    /// against: 7 Mostaql feed cards + fill to "98 مشروعًا من مستقل" (12 added today), 2 Nafezly
    /// cards + fill to "49 مشروعًا من نفذلي" (5 added today), the per-source cadence/rate settings
    /// the header caption and settings rows show, and full details for the two stable
    /// details-capture targets (<see cref="V3DetailsMostaqlProjectId"/> /
    /// <see cref="V3DetailsNafezlyProjectId"/>). Idempotent: clears the store first, exactly like
    /// the MVP seed; everything goes through the real repositories.
    /// </summary>
    private async Task<Result<int>> SeedV3Async(CancellationToken cancellationToken)
    {
        var cleared = await _projectRepository.ClearAllAsync(cancellationToken);
        if (!cleared.IsOk)
        {
            return Result<int>.Err(cleared.Error);
        }

        // Header caption "المسح كل 30 ثانية · 12 طلب/دقيقة" (mostaql) / "… 60 … · 8 …" (nafezly)
        // and the settings rows فاصل الفحص / حد الطلبات read these stored settings.
        _settings.Set(SourceId.Mostaql, SettingsKeys.PollIntervalSeconds, 30);
        _settings.Set(SourceId.Mostaql, SettingsKeys.MaxRequestsPerMinute, 12);
        _settings.Set(V3Nafezly, SettingsKeys.PollIntervalSeconds, 60);
        _settings.Set(V3Nafezly, SettingsKeys.MaxRequestsPerMinute, 8);
        // Settings mockup: تجميع التنبيهات ON for both platforms, تشغيل مع النظام OFF.
        _settings.Set(SourceId.Mostaql, SettingsKeys.NotificationGroupingEnabled, true);
        _settings.Set(V3Nafezly, SettingsKeys.NotificationGroupingEnabled, true);
        _settings.SetGlobal(SettingsKeys.StartupEnabled, false);

        var now = DateTimeOffset.UtcNow;
        var seeded = 0;

        foreach (var group in new (SourceId Source, IEnumerable<SeedProject> Projects, IEnumerable<ProjectSummary> Fill)[]
        {
            (SourceId.Mostaql, BuildV3MostaqlProjects(now),
                BuildV3Fill(SourceId.Mostaql, V3MostaqlFillIdBase, V3MostaqlTrackedCount - V3MostaqlVisibleCount, V3MostaqlAddedToday - V3MostaqlVisibleCount, now)),
            (V3Nafezly, BuildV3NafezlyProjects(now),
                BuildV3Fill(V3Nafezly, V3NafezlyFillIdBase, V3NafezlyTrackedCount - V3NafezlyVisibleCount, V3NafezlyAddedToday - V3NafezlyVisibleCount, now)),
        })
        {
            foreach (var project in group.Projects)
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

            foreach (var filler in group.Fill)
            {
                var fillerResult = await _projectRepository.InsertSummaryAsync(filler, cancellationToken);
                if (!fillerResult.IsOk)
                {
                    return Result<int>.Err(fillerResult.Error);
                }

                seeded++;
            }
        }

        return Result<int>.Ok(seeded);
    }

    /// <summary>
    /// The two stable details-capture targets wired to <c>--project-id</c>/<c>--source-id</c>:
    /// the Mostaql feed's first card, and the Nafezly CV project project-details.html is drawn
    /// against (budget "10 - 25 $", 4 عروض, مفتوح, 5 أيام, owner دوحة with the 7 / 61٪ / 4.6
    /// stat cells, executor محمد. أ).
    /// </summary>
    public const long V3DetailsMostaqlProjectId = 1300101;

    public const long V3DetailsNafezlyProjectId = 990000001;

    /// <summary>
    /// Read/older rows that make the per-source footer totals ("98 مشروعًا من مستقل" /
    /// "49 مشروعًا من نفذلي") and today counters (12 / 5) true. They are Enriched with old
    /// <c>enriched_at</c>, so they sort below the visible cards and only exist as scroll history.
    /// </summary>
    private IEnumerable<ProjectSummary> BuildV3Fill(SourceId source, long idBase, int count, int addedToday, DateTimeOffset now)
    {
        var descriptor = _registry.Get(source);
        for (var index = 0; index < count; index++)
        {
            var discoveredAt = index < addedToday
                ? now.AddHours(-4 - index)
                : now.AddDays(-1 - ((index - addedToday) / 20));

            yield return new ProjectSummary
            {
                Source = source,
                ProjectId = idBase + index,
                Title = "مشروع سابق",
                Url = descriptor.BuildProjectUrl(idBase + index),
                ClientName = "عميل",
                ProposalCount = 0,
                IsUnread = false,
                EnrichmentStatus = EnrichmentStatus.Enriched,
                DiscoveredAt = discoveredAt,
                EnrichedAt = discoveredAt,
            };
        }
    }

    /// <summary>
    /// The discovery instant <c>project-details.html</c> prints in its header caption
    /// ("· اكتُشف 21 أكتوبر 2026 14:32"). The details page renders the stored
    /// <c>ProjectDetails.discovered_at</c> verbatim, so the two stable details-capture targets
    /// carry this fixed literal (their <c>ProjectSummary.discovered_at</c> stays relative, so feed
    /// ordering is untouched). D2 seed-literal alignment.
    /// </summary>
    private static DateTimeOffset V3DetailsDiscoveredAt =>
        new(new DateTime(2026, 10, 21, 14, 32, 0, DateTimeKind.Unspecified));

    /// <summary>
    /// The seven Mostaql cards of <c>postmvp/v3-multisource/projects.html</c>, verbatim: titles,
    /// descriptions, budgets, owners, elapsed-time texts, proposal counts, skills, unread flags
    /// (cards 1/3/6) and enrichment badges (Enriched / Pending / Failed).
    /// </summary>
    private IEnumerable<SeedProject> BuildV3MostaqlProjects(DateTimeOffset now)
    {
        var mostaql = SourceId.Mostaql;

        yield return BuildV3(
            source: mostaql,
            projectId: 1300101,
            title: "تصميم موقع تعليمي تفاعلي",
            description: "أحتاج إلى تصميم وتطوير موقع تعليمي تفاعلي للدورات اونلاين مع لوحة تحكم للمدرب والطلاب، يدعم المحتوى المرئي والاختبارات والشهادات.",
            skills: ["تصميم واجهات", "تطوير ويب", "PHP"],
            ownerId: 9300101,
            ownerName: "أحمد العتيبي",
            ownerMeta: "السعودية · عضو منذ 2021",
            postedRelative: "منذ 3 دقائق",
            budget: "2500 - 5500",
            deliveryDays: 20,
            proposalCount: 69,
            isUnread: true,
            status: EnrichmentStatus.Enriched,
            discoveredAt: now.AddMinutes(-3),
            detailsDiscoveredAt: V3DetailsDiscoveredAt);

        yield return BuildV3(
            source: mostaql,
            projectId: 1300102,
            title: "كتابة محتوى تسويقي لمتجر إلكتروني",
            description: "مطلوب كتابة أوصاف منتجات احترافية وجذابة لمتجر إلكتروني متخصص في العناية بالبشرة مع تحسين المحتوى لمحركات البحث.",
            skills: ["كتابة محتوى", "سيو", "تسويق إلكتروني"],
            ownerId: 9300102,
            ownerName: "سارة المطيري",
            ownerMeta: null,
            postedRelative: "منذ 8 دقائق",
            budget: "500 - 1000",
            deliveryDays: 7,
            proposalCount: 69,
            isUnread: false,
            status: EnrichmentStatus.Pending,
            discoveredAt: now.AddMinutes(-8));

        yield return BuildV3(
            source: mostaql,
            projectId: 1300103,
            title: "تطوير تطبيق جوال لإدارة المخازن",
            description: "مطلوب تطبيق Android/iOS لنقاط البيع مع ربطه بنظام المحاسبة، ومزامنة offline للمخازن الفرعية وتقارير يومية.",
            skills: ["Flutter", "Firebase", "نقاط بيع"],
            ownerId: 9300103,
            ownerName: "خالد م",
            ownerMeta: "الإمارات · عضو منذ 2019",
            postedRelative: "منذ 18 دقيقة",
            budget: "8000 - 12000",
            deliveryDays: 14,
            proposalCount: 12,
            isUnread: true,
            status: EnrichmentStatus.Enriched,
            discoveredAt: now.AddMinutes(-18));

        yield return BuildV3(
            source: mostaql,
            projectId: 1300104,
            title: "ترجمة محتوى طبي من الإنجليزية إلى العربية",
            description: "ترجمة 12 مقالًا طبيًا (300 كلمة للمقال) بأسلوب مبسط مع الحفاظ على المصطلحات المعتمدة.",
            skills: ["ترجمة", "محتوى طبي"],
            ownerId: 9300104,
            ownerName: "رنا",
            ownerMeta: "السعودية",
            postedRelative: "منذ 40 دقيقة",
            budget: "300 - 450",
            deliveryDays: 7,
            proposalCount: 23,
            isUnread: false,
            status: EnrichmentStatus.Enriched,
            discoveredAt: now.AddMinutes(-40));

        yield return BuildV3(
            source: mostaql,
            projectId: 1300105,
            title: "إعداد حملة إعلانات ممولة على إنستغرام",
            description: "مطلوب مسوّق لجمهور مهتم باللياقة البدنية، الميزانية الإعلانية 500 ر.س لمدة أسبوعين.",
            skills: ["تسويق إلكتروني", "إعلانات ممولة"],
            ownerId: 9300105,
            ownerName: "عبدالله",
            ownerMeta: "الكويت",
            postedRelative: "منذ ساعة",
            budget: null,
            deliveryDays: null,
            proposalCount: 0,
            isUnread: false,
            status: EnrichmentStatus.Pending,
            discoveredAt: now.AddHours(-1));

        yield return BuildV3(
            source: mostaql,
            projectId: 1300106,
            title: "دورة تدريبية مسجلة لمصممين مبتدئين",
            description: "إنتاج دورة فيديو (8-10 وحدات) تغطي أساسيات تصميم واجهات المستخدم في Figma، مع ملفات تمارين للمشتركين.",
            skills: ["Figma", "تدريب", "مونتاج فيديو"],
            ownerId: 9300106,
            ownerName: "ميار",
            ownerMeta: "مصر · عضو جديد منذ 2026",
            postedRelative: "منذ ساعتين",
            budget: "1500 - 2500",
            deliveryDays: 30,
            proposalCount: 3,
            isUnread: true,
            status: EnrichmentStatus.Enriched,
            discoveredAt: now.AddHours(-2));

        yield return BuildV3(
            source: mostaql,
            projectId: 1300107,
            title: "تدقيق حساب متجر إلكتروني (زد · سلة)",
            description: "مراجعة إعدادات المتجر وتكامل الشحن والدفع، وتقرير بتحسينات متوقعة لمتوسط التحويل.",
            skills: ["تجارة إلكترونية", "محاسبة"],
            ownerId: 9300107,
            ownerName: "سلطان",
            ownerMeta: "السعودية",
            postedRelative: "منذ 3 ساعات",
            budget: null,
            deliveryDays: null,
            proposalCount: 0,
            isUnread: false,
            status: EnrichmentStatus.Failed,
            discoveredAt: now.AddHours(-3));
    }

    /// <summary>
    /// The two Nafezly cards of <c>postmvp/v3-multisource/projects.html</c>, the first with the
    /// full details project-details.html is drawn against.
    /// </summary>
    private IEnumerable<SeedProject> BuildV3NafezlyProjects(DateTimeOffset now)
    {
        var nafezly = V3Nafezly;

        yield return BuildV3(
            source: nafezly,
            projectId: 990000001,
            title: "أتمتة تحسين السير الذاتية (CV) لمرشحين التوظيف",
            description: V3NafezlyDetailsDescription,
            skills: ["تسويق إلكتروني", "أتمتة", "Python", "Excel"],
            ownerId: 991000001,
            ownerName: "دوحة",
            ownerMeta: "مصر · صاحبة مشروع \"لبسك علينا\"",
            postedRelative: "منذ ساعة",
            budget: "10 - 25",
            deliveryDays: 5,
            proposalCount: 4,
            isUnread: true,
            status: EnrichmentStatus.Enriched,
            discoveredAt: now.AddHours(-1),
            projectStatus: "مفتوح",
            executorName: "محمد. أ",
            ownerRegisteredAt: "على نفذلي منذ 2023",
            ownerOpenProjects: 7,
            ownerHiringRatePercent: 61,
            ownerRating: 4.6,
            detailsDiscoveredAt: V3DetailsDiscoveredAt);

        yield return BuildV3(
            source: nafezly,
            projectId: 990000002,
            title: "تحليل بيانات حملات التسويق وبناء لوحة تفاعلية",
            description: "تحليل بيانات حملات التسويق وبناء داشبورد تفاعلي باستخدام Tableau مع ربط المصادر وتحديث تلقائي دوري.",
            skills: ["Tableau", "تحليل بيانات"],
            ownerId: 991000002,
            ownerName: "عميل",
            ownerMeta: null,
            postedRelative: "منذ ساعتين",
            budget: null,
            deliveryDays: null,
            proposalCount: 0,
            isUnread: false,
            status: EnrichmentStatus.Failed,
            discoveredAt: now.AddHours(-2),
            projectStatus: "مفتوح");
    }

    /// <summary>Verbatim description body from project-details.html § الوصف (Nafezly CV project).</summary>
    private const string V3NafezlyDetailsDescription = """
        أحتاج إلى مسوّق إلكتروني أو جهة تسويق متخصصة لزيادة مبيعات مشروعي "لبسك علينا" الذي يضم:
        ملابس بيتي، ملابس خروج، ملابس أطفال كاجوال، وأدوات منزلية متنوعة.
        يرجى توضيح خطة التسويق، القنوات التي سيُعمل عليها، والمؤشرات التي سيتم الإبلاغ بها أسبوعيًا.
        """;

    /// <summary>
    /// v3-shaped variant of <see cref="Build"/>: the same repository models, but source-aware
    /// URLs built through the source descriptor (no literal domain), an optional owner meta line
    /// for the card, and the extra detail fields the details mockup binds (project state,
    /// executor name, owner stat cells).
    /// </summary>
    private SeedProject BuildV3(
        SourceId source,
        long projectId,
        string title,
        string description,
        string[] skills,
        long ownerId,
        string ownerName,
        string? ownerMeta,
        string postedRelative,
        string? budget,
        int? deliveryDays,
        int proposalCount,
        bool isUnread,
        EnrichmentStatus status,
        DateTimeOffset discoveredAt,
        string? projectStatus = null,
        string? executorName = null,
        string? ownerRegisteredAt = null,
        int? ownerOpenProjects = null,
        double? ownerHiringRatePercent = null,
        double? ownerRating = null,
        DateTimeOffset? detailsDiscoveredAt = null)
    {
        var descriptor = _registry.Get(source);
        var url = descriptor.BuildProjectUrl(projectId);
        var owner = new Owner
        {
            Source = source,
            OwnerId = ownerId,
            Name = ownerName,
            ProfileUrl = descriptor.BuildAbsoluteUrl($"/u/{ownerId}"),
            RegisteredAt = ownerRegisteredAt,
            HiringRatePercent = ownerHiringRatePercent,
            OpenProjectsCount = ownerOpenProjects,
            Rating = ownerRating,
        };

        return new SeedProject(
            owner,
            new ProjectSummary
            {
                Source = source,
                ProjectId = projectId,
                Title = title,
                Url = url,
                ClientName = ownerName,
                ClientMeta = ownerMeta,
                PublishTimeNumber = MostaqlK.Core.Formatting.ArabicRelativeTime.ParseRelativeNumber(postedRelative),
                PublishTimeText = postedRelative,
                ProposalCount = proposalCount,
                ProposalCountText = MostaqlK.Core.Formatting.ArabicProposalParser.Format(proposalCount),
                Budget = budget,
                DeliveryDays = deliveryDays,
                SkillsText = string.Join(", ", skills),
                Description = description,
                ProjectStatus = projectStatus,
                IsUnread = isUnread,
                EnrichmentStatus = status,
                DiscoveredAt = discoveredAt,
            },
            new ProjectDetails
            {
                Source = source,
                ProjectId = projectId,
                Title = title,
                Url = url,
                Description = description,
                Budget = budget,
                DeliveryDays = deliveryDays,
                ProjectStatus = projectStatus ?? "مفتوح",
                ExecutorName = executorName,
                PublishTimeNumber = MostaqlK.Core.Formatting.ArabicRelativeTime.ParseRelativeNumber(postedRelative),
                PublishTimeText = postedRelative,
                ProposalCount = proposalCount,
                ProposalCountText = MostaqlK.Core.Formatting.ArabicProposalParser.Format(proposalCount),
                Skills = [.. skills.Select(s => new ProjectSkill { Name = s })],
                Owner = owner,
                Attachments = [],
                EnrichmentStatus = status,
                DiscoveredAt = detailsDiscoveredAt ?? discoveredAt,
                EnrichedAt = status == EnrichmentStatus.Enriched ? discoveredAt : null,
            });
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
        double? hiringRatePercent = null,
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
                PublishTimeNumber = MostaqlK.Core.Formatting.ArabicRelativeTime.ParseRelativeNumber(postedRelative),
                PublishTimeText = postedRelative,
                ProposalCount = proposalCount,
                ProposalCountText = MostaqlK.Core.Formatting.ArabicProposalParser.Format(proposalCount),
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
                PublishTimeNumber = MostaqlK.Core.Formatting.ArabicRelativeTime.ParseRelativeNumber(postedRelative),
                PublishTimeText = postedRelative,
                ProposalCount = proposalCount,
                ProposalCountText = MostaqlK.Core.Formatting.ArabicProposalParser.Format(proposalCount),
                Skills = [.. skills.Select(s => new ProjectSkill { Name = s })],
                Owner = owner,
                Attachments = [.. attachments ?? []],
                EnrichmentStatus = status,
                DiscoveredAt = discoveredAt,
                EnrichedAt = status == EnrichmentStatus.Enriched ? enrichedAt ?? discoveredAt : null,
            });
    }

    private sealed record SeedProject(Owner Owner, ProjectSummary Summary, ProjectDetails Details);
}

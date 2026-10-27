using MostaqlK.Core.Sources;
using MostaqlK.Features.Settings;
using MostaqlK.Services;
using MostaqlK.Sources.Mostaql;
using MostaqlK.Sources.Nafezly;
using MostaqlK.UI.PlatformComponents.SettingsRow;

namespace MostaqlK.Features.Settings.Views;

/// <summary>
/// Dev-only CRAFT gallery: the MAUI twin of
/// <c>.repertoire/design/postmvp/v3-multisource/settings-shapes.html</c>. Every row is produced by
/// <see cref="SettingsRow"/> from a <see cref="SettingsDescriptor"/> alone — the page contains no
/// row markup at all, which is the point of the plugin system.
/// <para>
/// The fixture content mirrors the mockup's literal copy cell for cell (same Arabic, same order,
/// same states), because a parity score against different dummy data can never converge: the
/// renderer is what is being measured, not the wording. One cell is NOT synthetic — the
/// "partially overridden" cookie row is the real <see cref="SettingsCatalog.Compose"/> output for
/// Nafezly with a label-only override, so the layer merge is proven end to end on screen.
/// </para>
/// <para>
/// Reached only through <c>--default-page=settings-shapes</c> (the parity harness); no user-facing
/// navigation leads here. <c>--parity-shape=&lt;shape&gt;</c> and <c>--parity-state=&lt;state&gt;</c>
/// isolate one section / one cell so every gallery cell is screenshot-addressable at 1280x800
/// without scrolling, mirroring the mockup's <c>?shape=</c> / <c>?state=</c> force script.
/// </para>
/// </summary>
public partial class SettingsShapesPage : ContentPage
{
    private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);
    private readonly string? _shapeFilter;
    private readonly string? _stateFilter;

    public SettingsShapesPage()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        _shapeFilter = ParityLaunchFlags.ParityShape(args);
        _stateFilter = ParityLaunchFlags.ParityStateFilter(args);
        Build();
    }

    /// <summary>One gallery cell: a variant caption plus the descriptor that renders it.</summary>
    private sealed record Cell(
        string Caption,
        string State,
        SettingsDescriptor Descriptor,
        object? Value = null,
        string? ValueText = null,
        string? ErrorText = null,
        bool Disabled = false,
        // Only the PlatformSwitches cells carry these; every other shape leaves them empty.
        IReadOnlyList<SettingsPlatformSwitchItem>? Platforms = null);

    /// <summary>One shape section: the mockup's sticky head plus its variant cells.</summary>
    private sealed record Section(
        string Shape,
        string Eyebrow,
        string TitleAr,
        string? HintAr,
        bool UseGrid,
        IReadOnlyList<Cell> Cells);

    private void Build()
    {
        Host.Children.Clear();
        BackgroundColor = BgApp;

        Host.Children.Add(Toolbar());
        foreach (var section in Sections())
        {
            if (_shapeFilter is not null && !section.Shape.Equals(_shapeFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Host.Children.Add(SectionHead(section));
            var cells = section.Cells
                .Where(c => _stateFilter is null || c.State.Equals(_stateFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (cells.Count > 0)
            {
                Host.Children.Add(SectionPad(section.UseGrid, cells));
            }
        }

        Host.Children.Add(new BoxView { HeightRequest = 40, Color = Surface });
    }

    // ------------------------------------------------------------------ fixture

    /// <summary>
    /// The catalog the mockup draws, as data. Section order, captions and copy are the mockup's;
    /// nothing here is a production setting (the production inventory is
    /// <c>SettingsCatalog.Builtin</c>) except the composed cookie override below.
    /// </summary>
    private static IReadOnlyList<Section> Sections()
    {
        var overriddenCookie = ComposedCookieRow();

        return
        [
            new("toggle", "Toggle", "مفتاح",
                "قيمة منطقية واحدة: تشغيل/إيقاف. يُكتب فورًا عبر الطابور المؤجَّل.", true,
                [
                    new Cell("صحيح — مفعّل", "valid",
                        Toggle("gallery.toggle.on", "تنبيهات هذه المنصة") with
                        { HintAr = "لن تصلك تنبيهات من هذه المنصة أثناء الإيقاف" }, Value: true),
                    new Cell("صحيح — متوقف", "off",
                        Toggle("gallery.toggle.off", "تخطي المشاريع المغلقة"), Value: false),
                    new Cell("معطّل (لا تملكه هذه المنصة)", "disabled",
                        Toggle("gallery.toggle.disabled", "تجميع التنبيهات") with
                        { HintAr = "متاح على مستوى النظام فقط" }, Value: true, Disabled: true),
                ]),

            new("numeric", "NumericBounded", "رقم ذو حدَّين",
                "min/max/step + وحدة + رسالة خطأ من الـ descriptor نفسه (Template {0}={min} {1}={max}).", true,
                [
                    new Cell("صحيح", "valid", Numeric("gallery.numeric.interval", "فاصل الفحص", "ثانية"), Value: 30),
                    new Cell("مقصوص عند الحد", "clamped", Numeric("gallery.numeric.clamped", "حد الطلبات", "طلب/دقيقة") with
                    {
                        Max = 60,
                        HintAr = "ضُبط تلقائيًا على الحد الأقصى (60)",
                        HintTone = SettingsHintTone.Brand,
                    }, Value: 60),
                    new Cell("خطأ — خارج المدى", "error", Numeric("gallery.numeric.error", "فاصل الفحص", "ثانية"),
                        Value: 5000, ErrorText: "القيمة يجب أن تكون بين 10 و3600 ثانية — المدخل 5000."),
                    new Cell("معطّل", "disabled", Numeric("gallery.numeric.parallel", "طلبات متزامنة", "بالتوازي"),
                        Value: 4, Disabled: true),
                ]),

            new("text", "TextEntry", "نص",
                "عزل LTR إلزامي للقيم التقنية داخل واجهة عربية.", true,
                [
                    new Cell("صحيح — معزول LTR", "valid",
                        Text("gallery.text.params", "معاملات البحث") with
                        { HintAr = "تُضاف كما هي إلى رابط بحث المنصة" },
                        Value: "category=development&sort=latest"),
                    new Cell("خطأ — ترميز غير مقبول", "error", Text("gallery.text.error", "معاملات البحث"),
                        Value: "q=<script>", ErrorText: "الحرف \"<\" غير مسموح في روابط المنصات."),
                ]),

            new("enum", "EnumPicker", "قائمة اختيار",
                "خياراتها من Options في الـ descriptor — لا مصفوفات عربية في الكود.", true,
                [
                    new Cell("صحيح", "valid", GroupingMode("gallery.enum.mode"), Value: "batch"),
                    new Cell("معطّل", "disabled", GroupingMode("gallery.enum.disabled"), Value: "batch", Disabled: true),
                ]),

            new("cookie", "CookieEditor", "محرر الجلسة",
                "مركّب: حالة + رفع + حفظ + مسح، بحالات انشغال/نجاح/فشل. النص قابل للتجاوز الجزئي من كل منصة.", true,
                [
                    new Cell("مرتبط", "valid",
                        Cookie("gallery.cookie.linked", "ارفع ملف ال cookies الخاص بك") with
                        { SecondaryLabelAr = "إلغاء الربط", HintTone = SettingsHintTone.Success },
                        ValueText: "جلسة محفوظة (24 كوكيز) — آخر تحديث 2026-10-27 09:14"),
                    new Cell("قيد التحقق", "busy",
                        Cookie("gallery.cookie.busy", "ارفع ملف ال cookies الخاص بك") with
                        { HintTone = SettingsHintTone.Brand },
                        ValueText: "جارٍ التحقق من الملف وتشفيره…", Disabled: true),
                    new Cell("فشل التحليل", "error",
                        Cookie("gallery.cookie.failed", "ارفع ملف ال cookies الخاص بك") with
                        { ActionLabelAr = "أعد المحاولة" },
                        ErrorText: "لم يُعثر على أي كوكيز صالحة في الملف. صدّر الملف بصيغة Netscape من متصفحك."),
                    new Cell("نص متجاوز جزئيًا (نفذلي)", "overridden",
                        overriddenCookie with
                        { HintAr = "نفس الشكل والقيم — النص فقط تجاوزته المنصة" },
                        ValueText: null),
                ]),

            new("switches", "PlatformSwitches", "مفاتيح المنصات",
                "مركّب: مفتاح لكل منصة مسجّلة — تُسرَد المنصات من سجلّها، فلا يُسمّى أي منها في الشيفرة. الإيقاف صمتٌ شبكي لا حذف: يتوقف الفحص والإثراء والتنبيهات وتبقى المشاريع والتنقّل. آخر منصة مفعّلة تُرسم مقفلة مع سببها بدل أن يُرفض نقرها.", true,
                [
                    new Cell("كلها مفعّلة", "valid", PlatformSwitches("gallery.switches.all"),
                        Platforms: SwitchItems(secondOn: true)),
                    new Cell("منصة معطّلة — الباقية مقفلة", "locked",
                        PlatformSwitches("gallery.switches.locked") with
                        { HintAr = "منصة واحدة على الأقل يجب أن تبقى مفعّلة — لا يمكن إيقاف آخر منصة مفعّلة." },
                        Platforms: SwitchItems(secondOn: false)),
                ]),

            new("stat", "ReadOnlyStat", "إحصاء + فعل", null, false,
                [
                    new Cell("صحيح", "valid",
                        new SettingsDescriptor("gallery.stat.archive", SettingsShape.ReadOnlyStat, SettingsScope.PerSource,
                            "الأرشيف", "gallery", 0) { SecondaryLabelAr = "عرض", CommandName = "ViewArchive" },
                        ValueText: "98 مشروعًا"),
                ]),

            new("action", "ActionButton", "زر فعل", null, true,
                [
                    new Cell("أساسي / شبحي", "valid",
                        new SettingsDescriptor("gallery.action.save", SettingsShape.ActionButton, SettingsScope.Global,
                            "حفظ التغييرات", "gallery", 0)
                        { ActionLabelAr = "حفظ", SecondaryLabelAr = "إعادة تعيين", CommandName = "Save" }),
                    new Cell("معطّل (لا تغييرات)", "disabled",
                        new SettingsDescriptor("gallery.action.saveDisabled", SettingsShape.ActionButton, SettingsScope.Global,
                            "حفظ التغييرات", "gallery", 0) { ActionLabelAr = "حفظ", CommandName = "Save" },
                        Disabled: true),
                ]),

            new("link", "LinkOut", "رابط خارجي", null, false,
                [
                    new Cell("صحيح", "valid",
                        new SettingsDescriptor("gallery.link.source", SettingsShape.LinkOut, SettingsScope.PerSource,
                            "صفحة المنصة", "gallery", 0)
                        { ActionLabelAr = "عرض في مستقل", CommandName = "OpenSource" }),
                ]),

            new("info", "InfoBanner", "تنويه", null, false,
                [
                    new Cell("صحيح", "valid",
                        new SettingsDescriptor("gallery.info.v1", SettingsShape.InfoBanner, SettingsScope.Global,
                            "هذه إعدادات النسخة الأولى؛ التخصيص الذكي لكل منصة يصل لاحقًا.", "gallery", 0)),
                ]),

            // The mockup renders this shape AS its own section head, so the section has no pad.
            new("header", "SectionHeader", "رأس قسم",
                "هذا الرأس الذي تقرأه الآن هو الشكل — لاصق، بشعيرة سفلية، وأيقونة من الـ descriptor (fa-plug / fa-sliders).",
                false, []),

            new("slider", "Slider", "مؤشر متدرّج", "مدى + قيمة حية بجانب المقبض.", true,
                [
                    new Cell("صحيح", "valid", RateSlider("gallery.slider.ceiling", "سقف الطلبات في الدقيقة"), Value: 12),
                    new Cell("معطّل", "disabled", RateSlider("gallery.slider.ceilingDisabled", "سقف الطلبات"), Value: 30, Disabled: true),
                ]),

            new("chips", "MultiChoiceChips", "رقائق متعددة",
                "نفس شكل رقائق الفلاتر — مجموعة قيم مقبولة من Options.", true,
                [
                    new Cell("محدد/غير محدد", "valid", Skills("gallery.chips.skills"),
                        Value: new List<string> { "web", "design" }),
                    new Cell("لا اختيار", "error", Skills("gallery.chips.none"),
                        Value: Array.Empty<string>(), ErrorText: "اختر مهارة واحدة على الأقل."),
                ]),

            new("segmented", "SegmentedButtons", "أزرار مجزّأة", null, true,
                [
                    new Cell("ثلاث حالات", "valid", Appearance("gallery.segmented.theme"), Value: "dark"),
                    new Cell("معطّل", "disabled", Appearance("gallery.segmented.disabled"), Value: "light", Disabled: true),
                ]),

            new("file", "FilePicker", "اختيار ملف", "اسم الملف فقط يظهر؛ المحتوى لا يُنسخ لأي مكان.", true,
                [
                    new Cell("ملف مختار", "valid",
                        new SettingsDescriptor("gallery.file.picked", SettingsShape.FilePicker, SettingsScope.PerSource,
                            "ملف الجلسة", "gallery", 0) { SecondaryLabelAr = "تغيير", CommandName = "PickFile" },
                        ValueText: "cookies.txt"),
                    new Cell("فارغ", "empty",
                        new SettingsDescriptor("gallery.file.empty", SettingsShape.FilePicker, SettingsScope.PerSource,
                            "ملف الجلسة", "gallery", 0)
                        { HintAr = "لم يُختر ملف بعد", ActionLabelAr = "اختيار ملف…", CommandName = "PickFile" }),
                ]),
        ];
    }

    private static SettingsDescriptor Toggle(string key, string label) =>
        new(key, SettingsShape.Toggle, SettingsScope.PerSource, label, "gallery", 0) { Value = SettingsValueKind.Bool };

    private static SettingsDescriptor Numeric(string key, string label, string unit) =>
        new(key, SettingsShape.NumericBounded, SettingsScope.PerSource, label, "gallery", 0)
        { Min = 10, Max = 3600, UnitAr = unit, Value = SettingsValueKind.Int, Validation = SettingsValidationKind.IntRange };

    private static SettingsDescriptor Text(string key, string label) =>
        new(key, SettingsShape.TextEntry, SettingsScope.PerSource, label, "gallery", 0)
        { Value = SettingsValueKind.String, IsLtr = true };

    private static SettingsDescriptor Cookie(string key, string label) =>
        new(key, SettingsShape.CookieEditor, SettingsScope.PerSource, label, "gallery", 0)
        { ActionLabelAr = "رفع ملف", AutomationId = key };

    private static SettingsDescriptor PlatformSwitches(string key) =>
        new(key, SettingsShape.PlatformSwitches, SettingsScope.Global, "المنصات", "gallery", 0)
        {
            HintAr = "منصة واحدة على الأقل يجب أن تبقى مفعّلة. الإيقاف يوقف الفحص والإثراء والتنبيهات فقط — المشاريع المحفوظة والتنقّل والروابط تبقى كما هي.",
            AutomationId = key,
        };

    /// <summary>
    /// The gallery's switch lines, enumerated from the registered source descriptors (so the fixture
    /// names no platform either) with the lock derived exactly as <c>PlatformEnablement.LockedSource</c>
    /// derives it — the only enabled platform is the locked one — instead of hand-drawing a state the
    /// product cannot reach.
    /// </summary>
    private static IReadOnlyList<SettingsPlatformSwitchItem> SwitchItems(bool secondOn)
    {
        var sources = new SourceRegistry([MostaqlSource.Instance, NafezlySource.Instance]).All;
        var flags = sources.Select((_, index) => index == 0 || secondOn).ToList();
        var enabledCount = flags.Count(on => on);

        return sources.Select((source, index) => new SettingsPlatformSwitchItem(
            source.Id,
            source.DisplayNameAr,
            flags[index],
            IsLocked: flags[index] && enabledCount == 1,
            AutomationId: $"Settings_PlatformSwitch_{source.Id.Value}")).ToList();
    }

    private static SettingsDescriptor GroupingMode(string key) =>
        new(key, SettingsShape.EnumPicker, SettingsScope.Global, "نمط التجميع", "gallery", 0)
        {
            Value = SettingsValueKind.Enum,
            Options = [new("platform", "حسب المنصة"), new("batch", "دفعة واحدة"), new("single", "فردي")],
        };

    private static SettingsDescriptor RateSlider(string key, string label) =>
        new(key, SettingsShape.Slider, SettingsScope.PerSource, label, "gallery", 0)
        { Min = 1, Max = 60, Value = SettingsValueKind.Int };

    private static SettingsDescriptor Skills(string key) =>
        new(key, SettingsShape.MultiChoiceChips, SettingsScope.PerSource, "المهارات المتابَعة", "gallery", 0)
        {
            Value = SettingsValueKind.String,
            Options = [new("web", "تطوير الويب"), new("design", "تصميم جرافيك"), new("content", "كتابة محتوى")],
        };

    private static SettingsDescriptor Appearance(string key) =>
        new(key, SettingsShape.SegmentedButtons, SettingsScope.Global, "المظهر", "gallery", 0)
        {
            Value = SettingsValueKind.Enum,
            Options = [new("light", "فاتح"), new("dark", "داكن"), new("auto", "تلقائي")],
        };

    /// <summary>
    /// The one non-synthetic cell: the real composition path (base catalog + a Nafezly patch that
    /// sets ONLY <c>LabelAr</c>), proving the merge end to end — shape, bounds and ids stay global.
    /// </summary>
    private static SettingsDescriptor ComposedCookieRow()
    {
        var composed = SettingsCatalog.Compose(NafezlySource.Instance, [new CookieLabelOverride()]);
        return composed.Descriptors.FirstOrDefault(d => d.Key == "session-cookie")
            ?? Cookie("session-cookie", "ارفع ملف ال cookies الخاص بك لموقع نفذلي");
    }

    private sealed class CookieLabelOverride : ISourceSettingsContribution
    {
        public SourceId Source => NafezlySource.Instance.Id;

        public IReadOnlyList<SettingsDescriptor> Adds => [];

        public IReadOnlyList<SettingsDescriptorPatch> Overrides =>
        [
            new("session-cookie") { LabelAr = "ارفع ملف ال cookies الخاص بك لموقع نفذلي" },
        ];
    }

    // ------------------------------------------------------------------ chrome

    private static readonly Color Ink1Light = Color.FromArgb("#0F172A");
    private static readonly Color Ink1Dark = Color.FromArgb("#F1F5F9");
    private static readonly Color Ink2Light = Color.FromArgb("#475569");
    private static readonly Color Ink2Dark = Color.FromArgb("#CBD5E1");
    private static readonly Color Ink3Light = Color.FromArgb("#64748B");
    private static readonly Color Ink3Dark = Color.FromArgb("#94A3B8");
    private static readonly Color LineLight = Color.FromArgb("#E2E8F0");
    private static readonly Color LineDark = Color.FromArgb("#334155");
    private static readonly Color SurfaceLight = Color.FromArgb("#FFFFFF");
    private static readonly Color SurfaceDark = Color.FromArgb("#1E293B");
    private static readonly Color Surface2Light = Color.FromArgb("#F8FAFC");
    private static readonly Color Surface2Dark = Color.FromArgb("#172032");
    private static readonly Color BgAppLight = Color.FromArgb("#F1F5F9");
    private static readonly Color BgAppDark = Color.FromArgb("#0F172A");

    private static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;
    private static Color Ink1 => IsDark ? Ink1Dark : Ink1Light;
    private static Color Ink2 => IsDark ? Ink2Dark : Ink2Light;
    private static Color Ink3 => IsDark ? Ink3Dark : Ink3Light;
    private static Color Line => IsDark ? LineDark : LineLight;
    private static Color Surface => IsDark ? SurfaceDark : SurfaceLight;
    private static Color Surface2 => IsDark ? Surface2Dark : Surface2Light;
    private static Color BgApp => IsDark ? BgAppDark : BgAppLight;

    private static View Hairline() => new BoxView { HeightRequest = 1, Color = Line };

    /// <summary>The mockup's toolbar: title, one-line purpose, source tiles, theme tile.</summary>
    private static View Toolbar()
    {
        var title = new Label
        {
            Text = "كتالوج الأشكال",
            FontFamily = "TajawalBold",
            LineHeight = 1.5,
            FontSize = 14,
            TextColor = Ink1,
            VerticalTextAlignment = TextAlignment.Center,
        };

        var subtitle = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "— كل", FontFamily = "Tajawal", FontSize = 12, TextColor = Ink3 },
                Eyebrow("SettingsShape"),
                new Label
                {
                    Text = "وكل حالة، كما سيصنعها الـ renderer من الـ descriptor وحده",
                    FontFamily = "Tajawal",
                    LineHeight = 1.5,
                    FontSize = 12,
                    TextColor = Ink3,
                },
            },
        };

        var bar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 10,
            Padding = new Thickness(32, 11),
            BackgroundColor = Surface,
        };
        title.HorizontalOptions = LayoutOptions.Start;
        var mostaqlTile = SourceTile("م", MostaqlBrush, MostaqlOnBrush, isMostaqlTile: true);
        var nafezlyTile = SourceTile("ن", NafezlyBrush, NafezlyOnBrush, isMostaqlTile: false);
        var themeTile = ThemeTile();
        Grid.SetColumn(title, 0);
        Grid.SetColumn(subtitle, 1);
        Grid.SetColumn(mostaqlTile, 3);
        Grid.SetColumn(nafezlyTile, 4);
        Grid.SetColumn(themeTile, 5);
        bar.Children.Add(title);
        bar.Children.Add(subtitle);
        bar.Children.Add(mostaqlTile);
        bar.Children.Add(nafezlyTile);
        bar.Children.Add(themeTile);

        return new VerticalStackLayout { Spacing = 0, Children = { bar, Hairline() } };
    }

    // The tiles show BOTH identities at once, so their hexes are the mockup's, not the active brand.
    private static readonly Color MostaqlLight = Color.FromArgb("#2386C8");
    private static readonly Color MostaqlDark = Color.FromArgb("#5CA8DE");
    private static readonly Color NafezlyLight = Color.FromArgb("#FFA200");
    private static readonly Color NafezlyDark = Color.FromArgb("#FFB84D");

    private static Color MostaqlBrush => IsDark ? MostaqlDark : MostaqlLight;
    private static Color MostaqlOnBrush => IsDark ? Color.FromArgb("#0B1C2A") : Colors.White;
    private static Color NafezlyBrush => IsDark ? NafezlyDark : NafezlyLight;
    private static Color NafezlyOnBrush => Color.FromArgb("#3A2A00");

    /// <summary>Which tile is lit: the source the app actually launched with (the harness passes <c>--source-id</c>).</summary>
    private static bool ActiveIsMostaql
    {
        get
        {
            var active = Application.Current?.Handler?.MauiContext?.Services
                .GetService<MostaqlK.Services.GlobalAppStatusService>()?.ActiveSource.Value;
            return !string.Equals(active, "nafezly", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static View SourceTile(string glyph, Color fill, Color onFill, bool isMostaqlTile) => new Border
    {
        WidthRequest = 34,
        HeightRequest = 34,
        // Without this the auto row squashes the tile to the label's height and the toolbar
        // loses 7px the mockup's 34px tiles reserve.
        VerticalOptions = LayoutOptions.Center,
        MinimumHeightRequest = 34,
        StrokeThickness = 0,
        BackgroundColor = fill,
        Opacity = isMostaqlTile == ActiveIsMostaql ? 1 : 0.4,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 9 },
        Content = new Label
        {
            Text = glyph,
            FontFamily = "TajawalBold",
            LineHeight = 1.5,
            FontSize = 15,
            TextColor = onFill,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        },
    };

    private static View ThemeTile() => new Border
    {
        WidthRequest = 34,
        HeightRequest = 34,
        VerticalOptions = LayoutOptions.Center,
        MinimumHeightRequest = 34,
        StrokeThickness = 1,
        Stroke = Line,
        BackgroundColor = Surface2,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 9 },
        Content = new Label
        {
            Text = IsDark ? "☀" : "☾",
            FontFamily = "TajawalBold",
            LineHeight = 1.5,
            FontSize = 14,
            TextColor = Ink2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        },
    };

    /// <summary>The descriptor enum name as a mono eyebrow — the page IS the contract.</summary>
    private static View Eyebrow(string name) => new Border
    {
        StrokeThickness = 0,
        Padding = new Thickness(7, 1),
        BackgroundColor = MostaqlK.UI.DesignSystem.BrandTokens.BrandPalette.BrandSoft,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
        VerticalOptions = LayoutOptions.Center,
        Content = new Label
        {
            Text = name,
            FontFamily = "Consolas",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = MostaqlK.UI.DesignSystem.BrandTokens.BrandPalette.BrandInk,
        },
    };

    private static View SectionHead(Section section)
    {
        var head = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Eyebrow(section.Eyebrow),
                new Label
                {
                    Text = section.TitleAr,
                    FontFamily = "TajawalBold",
                    LineHeight = 1.5,
                    FontSize = 16,
                    TextColor = Ink1,
                    VerticalOptions = LayoutOptions.Center,
                },
            },
        };

        var stack = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(32, 18), BackgroundColor = Surface };
        stack.Children.Add(head);
        if (section.HintAr is not null)
        {
            stack.Children.Add(new Label
            {
                Text = section.HintAr,
                FontFamily = "Tajawal",
                LineHeight = 1.5,
                FontSize = 12,
                TextColor = Ink3,
                Margin = new Thickness(0, 4, 0, 0),
                LineBreakMode = LineBreakMode.WordWrap,
            });
        }
        stack.Children.Add(Hairline());
        return stack;
    }

    private View SectionPad(bool useGrid, IReadOnlyList<Cell> cells)
    {
        var pad = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(32, 20) };

        if (!useGrid || cells.Count == 1)
        {
            pad.Spacing = 0;
            foreach (var cell in cells)
            {
                pad.Children.Add(Row(cell));
            }
        }
        else
        {
            for (var i = 0; i < cells.Count; i += 2)
            {
                var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 16 };
                var first = Card(cells[i]);
                Grid.SetColumn(first, 0);
                grid.Children.Add(first);
                if (i + 1 < cells.Count)
                {
                    var second = Card(cells[i + 1]);
                    Grid.SetColumn(second, 1);
                    grid.Children.Add(second);
                }
                pad.Children.Add(grid);
            }
        }

        // The hairline is the pad's bottom edge, not another stacked item: inside the spaced stack
        // it would gain a 16px gap the mockup does not have.
        return new VerticalStackLayout
        {
            Spacing = 0,
            BackgroundColor = Surface,
            Children = { pad, Hairline() },
        };
    }

    /// <summary>Variant card: hairline box, uppercase caption, then the row the renderer builds.</summary>
    private View Card(Cell cell) => new Border
    {
        StrokeThickness = 1,
        Stroke = Line,
        BackgroundColor = Surface,
        Padding = new Thickness(16, 0, 16, 2),
        // .variant { margin-bottom: 1rem } — the mockup's grid pitch is gap + this margin.
        Margin = new Thickness(0, 0, 0, 16),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
        Content = CardBody(cell),
    };

    private View CardBody(Cell cell)
    {
        var stack = new VerticalStackLayout { Spacing = 0 };
        stack.Children.Add(new Label
        {
            Text = cell.Caption,
            FontFamily = "TajawalBold",
            LineHeight = 1.5,
            FontSize = 10,
            TextColor = Ink3,
            Margin = new Thickness(0, 9, 0, 0),
        });
        stack.Children.Add(Row(cell));
        return stack;
    }

    private View Row(Cell cell)
    {
        if (cell.Value is not null)
        {
            _store[cell.Descriptor.Key] = cell.Value;
        }

        var row = new SettingsRowViewModel(
            cell.Descriptor,
            read: () => _store.TryGetValue(cell.Descriptor.Key, out var v) ? v : cell.Descriptor.DefaultLiteral,
            write: v => _store[cell.Descriptor.Key] = v,
            valueText: cell.ValueText)
        {
            ShowTopDivider = false,
            PlatformItems = cell.Platforms ?? [],
        };

        // Inside a variant card the row never draws the page's inter-row hairline: the mockup's
        // `.set-row:first-child { border-top: 0 }` applies to every card, which holds one row.
        var view = new SettingsRow();
        view.SetRow(row);
        if (cell.ErrorText is not null)
        {
            row.ErrorText = cell.ErrorText;
        }

        if (cell.Disabled)
        {
            view.IsEnabled = false;
            view.Opacity = 0.45;
        }

        return view;
    }
}

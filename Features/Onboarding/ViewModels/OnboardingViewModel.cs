using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Core.Sources;
using MostaqlK.Services.Onboarding;
using MostaqlK.UI.DesignSystem.Tokens;

namespace MostaqlK.Features.Onboarding.ViewModels;

/// <summary>One platform card on the choice step: identity from the descriptor, switch state from
/// <see cref="PlatformEnablement"/>. The view-model owns every write; the view only reports toggles.
/// Colors resolve per render from the descriptor's own brand set (theme-projected like the rail
/// tiles) and the semantic token table — no hex lives in this file.</summary>
public sealed class PlatformChoiceItem : ObservableObject
{
    public PlatformChoiceItem(ISourceDescriptor descriptor, PlatformEnablement enablement, int effectivePollSeconds)
    {
        _descriptor = descriptor;
        _enablement = enablement;
        Id = descriptor.Id;
        NameAr = descriptor.DisplayNameAr;
        TaglineAr = descriptor.TaglineAr;
        DescriptionAr = descriptor.DescriptionAr;
        Glyph = descriptor.RailGlyph;
        CadenceText = $"فحص كل {effectivePollSeconds} ثانية";
    }

    private readonly ISourceDescriptor _descriptor;
    private readonly PlatformEnablement _enablement;

    public SourceId Id { get; }
    public string NameAr { get; }
    public string? TaglineAr { get; }
    public string DescriptionAr { get; }
    public string Glyph { get; }
    public string CadenceText { get; }

    public bool IsEnabled => _enablement.IsEnabled(Id);

    /// <summary>The only platform left on: its switch renders locked, mirroring the settings page.</summary>
    public bool IsLocked => _enablement.LockedSource == Id;

    public string StateText => IsEnabled ? "قيد المراقبة" : "غير مرقّبة";

    public bool HasTagline => !string.IsNullOrEmpty(TaglineAr);
    public string CardAutomationId => $"onboarding-platform-card-{Id.Value}";
    public string SwitchAutomationId => $"onboarding-platform-switch-{Id.Value}";

    public Color GlyphFill { get; private set; } = Colors.Transparent;
    public Color GlyphOnText { get; private set; } = Colors.White;
    public Color CardStroke { get; private set; } = Colors.Transparent;
    public Color StateTextColor { get; private set; } = Colors.Transparent;

    public void Refresh()
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        GlyphFill = ParseBrand(isDark ? _descriptor.Brands.BrandDark : _descriptor.Brands.Brand);
        GlyphOnText = ParseBrand(isDark ? _descriptor.Brands.OnBrandDark : _descriptor.Brands.OnBrand);
        CardStroke = IsEnabled ? GlyphFill : ThemeTokens.Get("BorderSubtle");
        StateTextColor = IsEnabled ? ThemeTokens.Get("PositiveAccent") : ThemeTokens.Get("TextMuted");

        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(GlyphFill));
        OnPropertyChanged(nameof(GlyphOnText));
        OnPropertyChanged(nameof(CardStroke));
        OnPropertyChanged(nameof(StateTextColor));
    }

    private static Color ParseBrand(string hex) => Color.TryParse(hex, out var color) ? color : Colors.Transparent;
}

/// <summary>One interest chip: a category slug every registered site carries, with the first
/// source's Arabic label (shared <see cref="SharedFilterVocabulary"/> — never a per-platform list).</summary>
public sealed partial class InterestChoiceItem : ObservableObject
{
    public InterestChoiceItem(FilterFacetOption option)
    {
        Value = option.Value;
        LabelAr = option.LabelAr;
    }

    public string Value { get; }
    public string LabelAr { get; }
    public string ChipAutomationId => $"onboarding-interest-chip-{Value}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipBackground))]
    [NotifyPropertyChangedFor(nameof(ChipBorder))]
    [NotifyPropertyChangedFor(nameof(ChipTextColor))]
    [NotifyPropertyChangedFor(nameof(ChipDisplayText))]
    public partial bool IsSelected { get; set; }

    /// <summary>The tick lives in the text itself so the chip stays a plain Button (invokable,
    /// keyboard-reachable, UIA-measurable) instead of a view-content template.</summary>
    public string ChipDisplayText => (IsSelected ? "✓ " : "") + LabelAr;
    public Color ChipBackground => IsSelected ? ThemeTokens.Get("AccentSoft") : ThemeTokens.Get("Surface");
    public Color ChipBorder => IsSelected ? ThemeTokens.Get("Accent") : ThemeTokens.Get("BorderSubtle");
    public Color ChipTextColor => IsSelected ? ThemeTokens.Get("TextPrimary") : ThemeTokens.Get("TextSecondary");

    public void RaiseColorsChanged()
    {
        OnPropertyChanged(nameof(ChipBackground));
        OnPropertyChanged(nameof(ChipBorder));
        OnPropertyChanged(nameof(ChipTextColor));
    }
}

public sealed partial class OnboardingViewModel : ObservableObject
{
    public const int TotalSteps = 6;
    private const int ChoiceStepIndex = 4;
    private const int FinalStepIndex = 5;

    private readonly OnboardingStateService _stateService;
    private readonly PlatformEnablement _enablement;

    private sealed record StepCopy(string Before, string Accent, string After, string Description, string? Badge);

    private static readonly StepCopy[] Steps =
    [
        new("نرصد مشاريع المنصات ", "لحظة نشرها", "",
            "يتفحص التطبيق منصّاتك المختارة بشكل دوري من الخلفية، فتصلك المشاريع الجديدة قبل أن يسبقك إليها أحد — دون أي تدخل منك.",
            "فحص مستمر"),
        new("تنبيهات ", "فورية", " عند وصول الجديد",
            "عند اكتشاف مشروع يطابق اهتماماتك يصلك إشعار فوري على ويندوز أو أندرويد — نظامك، في وقتك، بلا بريد مزعج.",
            "تنبيهات فورية"),
        new("أرشيفك ", "المحلي", " يبقى معك",
            "نحفظ تفاصيل المشاريع على جهازك، فتصفح سجلًا كاملًا بلا اتصال — لا يتغير ولا يُحذف من طرف آخر.",
            "أرشيف محلي"),
        new("ابحث ", "بذكاء", " عن الفرص المناسبة",
            "تصفّ مستكشف المشاريع بمحرّك تصفية يبحث في العنوان والوصف والمهارات معًا، لترى الفرص الأقرب لك أولًا.",
            "بحث ذكي"),
        new("أي المنصات ", "تراقب؟", "",
            "شغّل كل منصة تريد رصد مشاريعها، وحدّد مجالات اهتمامك — الفحص والتنبيهات ستعمل على ما يطابقها، في كل دورة.",
            "المنصات والاهتمامات"),
        new("جاهز؟ ", "لنبدأ الرصد", "",
            "كل شيء جاهز. الرصد والتنبيهات تعمل على المنصات والمجالات التي اخترتها.",
            null),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoiceStep))]
    [NotifyPropertyChangedFor(nameof(IsFinalStep))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(IsBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(NextLabel))]
    [NotifyPropertyChangedFor(nameof(CurrentIllustration))]
    [NotifyPropertyChangedFor(nameof(HasIllustration))]
    [NotifyPropertyChangedFor(nameof(CurrentTitle))]
    [NotifyPropertyChangedFor(nameof(TitleAccent))]
    [NotifyPropertyChangedFor(nameof(TitleBefore))]
    [NotifyPropertyChangedFor(nameof(TitleAfter))]
    [NotifyPropertyChangedFor(nameof(CurrentDescription))]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    public partial int CurrentStep { get; set; }

    [ObservableProperty]
    public partial bool IsTransitioning { get; set; }

    [ObservableProperty]
    public partial bool LockNoticeVisible { get; set; }

    public IReadOnlyList<PlatformChoiceItem> Platforms { get; }
    public IReadOnlyList<InterestChoiceItem> Interests { get; }

    [ObservableProperty]
    public partial string PlatformCountText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string InterestCountText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string InterestHintText { get; private set; } = string.Empty;

    /// <summary>Set by the view so the view-model can drive the directional slide choreography around
    /// the actual <see cref="CurrentStep"/> mutation, mirroring the mockup's exit-then-enter sequencing.</summary>
    public Func<bool, Task>? BeginExitAnimation { get; set; }

    /// <summary>See <see cref="BeginExitAnimation"/>.</summary>
    public Func<bool, Task>? BeginEnterAnimation { get; set; }

    public bool IsChoiceStep => CurrentStep == ChoiceStepIndex;
    public bool IsFinalStep => CurrentStep == FinalStepIndex;
    public bool CanGoBack => CurrentStep > 0;
    public bool IsBadgeVisible => !IsFinalStep;
    public string NextLabel => IsFinalStep ? "ابدأ الاستخدام" : "التالي";
    public string BadgeText => Steps[CurrentStep].Badge ?? string.Empty;
    public string CurrentTitle => Steps[CurrentStep].Before + Steps[CurrentStep].Accent + Steps[CurrentStep].After;
    public string TitleBefore => Steps[CurrentStep].Before;
    public string TitleAccent => Steps[CurrentStep].Accent;
    public string TitleAfter => Steps[CurrentStep].After;
    public string CurrentDescription => Steps[CurrentStep].Description;

    /// <summary>The choice step has no artwork — the picker itself is the illustration. The final
    /// step reuses step5.png (the owner replaced the old Mostaql-"M" art with a platform-neutral
    /// scene), exactly like the previous wizard reused its fifth image for the last step.</summary>
    public string? CurrentIllustration => CurrentStep switch
    {
        ChoiceStepIndex => null,
        FinalStepIndex => "step5.png",
        _ => $"step{CurrentStep + 1}.png",
    };

    public bool HasIllustration => CurrentStep != ChoiceStepIndex;

    /// <summary>Re-resolves every brand/token-derived color after a theme flip; the page calls this
    /// on <c>ThemeTokens.TokensChanged</c> so the cards repaint live, like the rail does.</summary>
    public void RefreshPlatformColors()
    {
        foreach (var platform in Platforms)
        {
            platform.Refresh();
        }

        foreach (var interest in Interests)
        {
            interest.RaiseColorsChanged();
        }
    }

    public OnboardingViewModel(
        OnboardingStateService stateService,
        PlatformEnablement enablement,
        SourceRegistry registry,
        ISettingsRepository settings)
    {
        _stateService = stateService;
        _enablement = enablement;
        var requestedSeconds = settings.GetGlobal(SettingsKeys.PollIntervalSeconds, SourceCadence.DefaultPollIntervalSeconds);
        Platforms = [.. enablement.AllSources.Select(id => new PlatformChoiceItem(
            registry.Get(id), enablement, registry.Get(id).ClampPollInterval(requestedSeconds)))];
        Interests = [.. SharedFilterVocabulary.CommonCategoryOptions(registry).Select(o => new InterestChoiceItem(o))];
        CurrentStep = 0;
        RefreshGroupSummaries();
    }

    /// <summary>Applied live, exactly like the settings page: the enablement store is the single
    /// authority, so skipping mid-wizard or completing it both persist what the switches show.</summary>
    public void SetPlatformEnabled(PlatformChoiceItem item, bool enabled)
    {
        var result = _enablement.TrySetEnabled(item.Id, enabled);
        LockNoticeVisible = !result.Applied && result.ReasonKey == PlatformEnablement.ReasonLastEnabledPlatform;

        foreach (var platform in Platforms)
        {
            platform.Refresh();
        }

        RefreshGroupSummaries();
    }

    [RelayCommand]
    private void ToggleInterest(InterestChoiceItem item)
    {
        item.IsSelected = !item.IsSelected;
        RefreshGroupSummaries();
    }

    [RelayCommand]
    private void Skip()
    {
        if (IsTransitioning) return;
        IsTransitioning = false;
        _stateService.Complete(SelectedInterestValues());
    }

    [RelayCommand]
    private async Task Next()
    {
        if (IsTransitioning) return;
        if (IsFinalStep)
        {
            IsTransitioning = false;
            _stateService.Complete(SelectedInterestValues());
            return;
        }

        await Advance(forward: true);
    }

    [RelayCommand]
    private async Task Back()
    {
        if (CurrentStep == 0 || IsTransitioning) return;
        await Advance(forward: false);
    }

    private IReadOnlyList<string> SelectedInterestValues() =>
        [.. Interests.Where(i => i.IsSelected).Select(i => i.Value)];

    private void RefreshGroupSummaries()
    {
        var enabledCount = Platforms.Count(p => p.IsEnabled);
        PlatformCountText = $"{enabledCount} من {Platforms.Count} مرقّبة";

        var selected = Interests.Count(i => i.IsSelected);
        InterestCountText = selected > 0 ? $"{selected} مختارة" : "الكل";
        InterestHintText = selected > 0
            ? "سيصلك الجديد في " + string.Join("، ", Interests.Where(i => i.IsSelected).Select(i => i.LabelAr)) + "."
            : "سيصلك الجديد في كل المجالات.";
    }

    private async Task Advance(bool forward)
    {
        IsTransitioning = true;
        if (BeginExitAnimation != null) await BeginExitAnimation(forward);
        CurrentStep += forward ? 1 : -1;
        if (BeginEnterAnimation != null) await BeginEnterAnimation(forward);
        IsTransitioning = false;
    }
}

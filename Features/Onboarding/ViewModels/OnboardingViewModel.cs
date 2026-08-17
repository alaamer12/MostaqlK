using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Services.Onboarding;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Onboarding.ViewModels;

public sealed partial class OnboardingViewModel : ObservableObject
{
    public const int TotalSteps = 6;
    private readonly OnboardingStateService _stateService;

    [ObservableProperty]
    public partial string QueryParams { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueryStep))]
    [NotifyPropertyChangedFor(nameof(IsFinalStep))]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    public partial int CurrentStep { get; set; }

    [ObservableProperty]
    public partial bool IsTransitioning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonLabel))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveButtonLabel))]
    public partial bool IsSaved { get; set; }

    /// <summary>Set by the view so the view-model can drive the directional slide choreography around the actual <see cref="CurrentStep"/> mutation, mirroring the mockup's exit-then-enter sequencing.</summary>
    public Func<bool, Task>? BeginExitAnimation { get; set; }

    /// <summary>See <see cref="BeginExitAnimation"/>.</summary>
    public Func<bool, Task>? BeginEnterAnimation { get; set; }

    public string SaveButtonLabel => IsSaving || IsSaved ? string.Empty : "حفظ والبدء";

    public bool IsQueryStep => CurrentStep == 4;
    public bool IsFinalStep => CurrentStep == TotalSteps - 1;
    public bool CanGoBack => CurrentStep > 0;
    public bool IsBadgeVisible => !IsFinalStep;
    public string NextLabel => IsFinalStep ? "ابدأ الاستخدام" : CurrentStep == 4 ? "تخطي والبدء" : "التالي";
    public AppIconGlyph NextIcon => IsFinalStep ? AppIconGlyph.CircleCheck : AppIconGlyph.ChevronLeft;
    public string CurrentIllustration => CurrentStep < 4 ? $"step{CurrentStep + 1}.png" : "step5.png";

    public string BadgeText => CurrentStep switch
    {
        4 => "تخصيص الخلاصة",
        _ => "فحص مستمر"
    };

    public string CurrentTitle => CurrentStep switch
    {
        0 => "نرصد مشاريع مستقل لحظة نشرها",
        1 => "تنبيهات فورية عند وصول الجديد",
        2 => "أرشيفك المحلي يبقى معك",
        3 => "ابحث بذكاء عن الفرص المناسبة",
        4 => "لنخصص خلاصتك لما يهمك",
        _ => "جاهز؟ لنبدأ الرصد"
    };

    public string TitleAccent => CurrentStep switch
    {
        0 => "لحظة نشرها",
        1 => "فورية",
        2 => "المحلي",
        3 => "بذكاء",
        4 => "لما يهمك",
        _ => "لنبدأ الرصد"
    };

    public string TitleBefore
    {
        get
        {
            var idx = CurrentTitle.IndexOf(TitleAccent, StringComparison.Ordinal);
            return idx <= 0 ? string.Empty : CurrentTitle[..idx];
        }
    }

    public string TitleAfter
    {
        get
        {
            var idx = CurrentTitle.IndexOf(TitleAccent, StringComparison.Ordinal);
            return idx < 0 ? string.Empty : CurrentTitle[(idx + TitleAccent.Length)..];
        }
    }

    public string CurrentDescription => CurrentStep switch
    {
        0 => "التطبيق يعمل في الخلفية ويفحص المشاريع الجديدة بشكل دوري دون أي تدخل منك.",
        1 => "ستصلك الإشعارات فور اكتشاف مشروع يناسبك.",
        2 => "نحفظ التفاصيل على جهازك لتعود إليها في أي وقت.",
        3 => "استكشف المشاريع بسرعة وركّز على ما يهمك.",
        4 => "اختر استعلامًا يساعدنا على عرض المشاريع الأقرب لاهتماماتك. يمكنك تغييره لاحقًا من الإعدادات.",
        _ => "يمكنك تعديل استعلام البحث وفترة الفحص لاحقًا من الإعدادات."
    };

    public OnboardingViewModel(OnboardingStateService stateService)
    {
        _stateService = stateService;
        QueryParams = stateService.QueryParams;
        CurrentStep = 0;
    }

    [RelayCommand]
    private void Skip()
    {
        if (IsTransitioning) return;
        IsTransitioning = false;
        _stateService.Complete(QueryParams);
    }

    [RelayCommand]
    private async Task Save()
    {
        if (IsTransitioning) return;
        IsTransitioning = true;
        IsSaving = true;
        await Task.Delay(520);
        IsSaving = false;
        IsSaved = true;
        await Task.Delay(700);
        
        // Signal completion first; the view/app handles window swap.
        // We set IsTransitioning to false BEFORE calling Complete to ensure
        // the property change settles before the window starts closing.
        IsTransitioning = false;
        IsSaved = false;
        _stateService.Complete(QueryParams);
    }

    [RelayCommand]
    private async Task Next()
    {
        if (IsTransitioning) return;
        if (IsQueryStep)
        {
            await Save();
            return;
        }

        if (IsFinalStep)
        {
            IsTransitioning = false;
            _stateService.Complete(QueryParams);
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

    private async Task Advance(bool forward)
    {
        IsTransitioning = true;
        if (BeginExitAnimation != null) await BeginExitAnimation(forward);
        CurrentStep += forward ? 1 : -1;
        NotifyStepChanged();
        if (BeginEnterAnimation != null) await BeginEnterAnimation(forward);
        IsTransitioning = false;
    }

    [RelayCommand]
    private void SelectPreset(string value) => QueryParams = value;

    private void NotifyStepChanged()
    {
        OnPropertyChanged(nameof(CurrentIllustration));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(TitleAccent));
        OnPropertyChanged(nameof(TitleBefore));
        OnPropertyChanged(nameof(TitleAfter));
        OnPropertyChanged(nameof(CurrentDescription));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(NextIcon));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(IsBadgeVisible));
    }
}
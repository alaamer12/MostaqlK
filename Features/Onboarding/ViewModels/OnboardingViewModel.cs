using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Services.Onboarding;

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

    public bool IsQueryStep => CurrentStep == 4;
    public bool IsFinalStep => CurrentStep == TotalSteps - 1;
    public bool CanGoBack => CurrentStep > 0;
    public string NextLabel => IsFinalStep ? "ابدأ الاستخدام" : CurrentStep == 4 ? "تخطي والبدء" : "التالي";
    public string CurrentIllustration => CurrentStep < 4 ? $"step{CurrentStep + 1}.png" : "step5.png";

    public string CurrentTitle => CurrentStep switch
    {
        0 => "رصد مستمر للمشاريع",
        1 => "تنبيهات فورية عند وصول الجديد",
        2 => "أرشيفك المحلي يبقى معك",
        3 => "ابحث بذكاء عن الفرص المناسبة",
        4 => "لنخصص خلاصتك لما يهمك",
        _ => "أنت جاهز للبدء"
    };

    public string CurrentDescription => CurrentStep switch
    {
        0 => "يراقب MostaqlK المشاريع الجديدة في الخلفية بهدوء.",
        1 => "ستصلك الإشعارات فور اكتشاف مشروع يناسبك.",
        2 => "نحفظ التفاصيل على جهازك لتعود إليها في أي وقت.",
        3 => "استكشف المشاريع بسرعة وركّز على ما يهمك.",
        4 => "اختر استعلامًا يساعدنا على عرض المشاريع الأقرب لاهتماماتك. يمكنك تغييره لاحقًا من الإعدادات.",
        _ => "ابدأ الآن ودع MostaqlK يراقب المشاريع نيابةً عنك."
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
        if (!IsTransitioning) _stateService.Complete(QueryParams);
    }

    [RelayCommand]
    private async Task Save()
    {
        if (IsTransitioning) return;
        IsTransitioning = true;
        await Task.Delay(520);
        _stateService.Complete(QueryParams);
        IsTransitioning = false;
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
            _stateService.Complete(QueryParams);
            return;
        }

        CurrentStep++;
        NotifyStepChanged();
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep == 0 || IsTransitioning) return;
        CurrentStep--;
        NotifyStepChanged();
    }

    [RelayCommand]
    private void SelectPreset(string value) => QueryParams = value;

    private void NotifyStepChanged()
    {
        OnPropertyChanged(nameof(CurrentIllustration));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentDescription));
        OnPropertyChanged(nameof(NextLabel));
    }
}
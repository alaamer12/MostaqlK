using Microsoft.Maui.Controls;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Specialization of <see cref="PlatformImage"/> for the Onboarding flow's step illustrations
/// (mirrors the <c>DebouncedEntry</c> → <c>SearchInputField</c> base-unit → specialization
/// pattern). The view-model (<see cref="MostaqlK.Features.Onboarding.ViewModels.OnboardingViewModel.CurrentIllustration"/>)
/// only knows a per-step file name (e.g. <c>"step1.png"</c>) — it has no notion of "platform"
/// at all, since illustrations vary by onboarding *step*, not by OS.
///
/// This unit is the seam where that eventually changes: today <see cref="StepImageFileName"/> is
/// resolved to the identical asset for every platform slot (<c>WindowsSource</c>/<c>MobileSource</c>/
/// <c>DefaultSource</c> in the base class), since no separate mobile-specific onboarding art exists
/// yet — inventing per-platform art without real assets would be premature per the
/// "avoid empty/premature" rule in <c>structure.md</c>. Once real Android/iOS-specific step
/// illustrations are designed, only this class needs to change (e.g. mapping the file name to a
/// different folder per platform) — <c>OnboardingPage.xaml</c> and the view-model stay untouched.
/// </summary>
public class OnboardingStepImage : PlatformImage
{
    public static readonly BindableProperty StepImageFileNameProperty =
        BindableProperty.Create(nameof(StepImageFileName), typeof(string), typeof(OnboardingStepImage), null,
            propertyChanged: OnStepImageFileNameChanged);

    public string? StepImageFileName
    {
        get => (string?)GetValue(StepImageFileNameProperty);
        set => SetValue(StepImageFileNameProperty, value);
    }

    private static void OnStepImageFileNameChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var self = (OnboardingStepImage)bindable;
        var fileName = (string?)newValue;
        var source = string.IsNullOrEmpty(fileName) ? null : ImageSource.FromFile(fileName);

        // Same asset wired to every platform slot for now (see class remarks) - the per-platform
        // resolution/caching machinery itself lives entirely in the PlatformImage base class.
        self.WindowsSource = source;
        self.MobileSource = source;
        self.DefaultSource = source;
    }
}

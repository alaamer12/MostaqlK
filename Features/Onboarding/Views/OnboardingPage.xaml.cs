using MostaqlK.Features.Onboarding.ViewModels;
using MostaqlK.UI.PlatformComponents;
using System.ComponentModel;

namespace MostaqlK.Features.Onboarding.Views;

public partial class OnboardingPage : ContentPage
{
    public OnboardingPage(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        FlowDirection = FlowDirection.RightToLeft;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OnboardingViewModel.CurrentStep)) return;

        if (MotionPreferences.IsReducedMotionRequested)
        {
            Illustration.Opacity = 1;
            return;
        }

        Illustration.TranslationX = 24;
        Illustration.Opacity = 0;
        await Task.WhenAll(
            Illustration.FadeToAsync(1, 220, Easing.CubicOut),
            Illustration.TranslateToAsync(0, 0, 260, Easing.CubicOut));
    }
}
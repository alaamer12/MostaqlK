using System.ComponentModel;
using MostaqlK.Core.Platform;
using MostaqlK.Features.Onboarding.ViewModels;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Onboarding.Views;

public partial class OnboardingPage : ContentPage
{
    private const string DotAnimationName = "OnboardingDotTransition";
    private const string SpinnerAnimationName = "OnboardingSpinnerRotate";

    private bool _syncingPlatformSwitch;

    public OnboardingViewModel ViewModel => (OnboardingViewModel)BindingContext;

    public OnboardingPage(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        Title = ChannelIdentity.AppName;
        BindingContext = viewModel;
        FlowDirection = FlowDirection.RightToLeft;

        // The choreography (exit outgoing content, mutate CurrentStep, enter incoming content) is
        // driven by the view-model so the property change and the animation stay in the right
        // order; the view only supplies how each phase actually animates.
        viewModel.BeginExitAnimation = AnimateStepExit;
        viewModel.BeginEnterAnimation = AnimateStepEnter;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        // Card/chip colors resolve from the descriptor brand sets and the token table in code, so a
        // live theme flip must repaint them too — the same hook the rail tiles listen on.
        MostaqlK.UI.DesignSystem.Tokens.ThemeTokens.TokensChanged += OnTokensChanged;
        UpdateStepDots(viewModel.CurrentStep, animate: false);
    }

    private void OnTokensChanged() => ViewModel.RefreshPlatformColors();

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        MostaqlK.UI.DesignSystem.Tokens.ThemeTokens.TokensChanged -= OnTokensChanged;
        foreach (var element in AnimatedStepElements)
        {
            element.AbortAnimation("TranslateToAsync");
            element.AbortAnimation("FadeToAsync");
        }
        NextSpinnerIcon.AbortAnimation(SpinnerAnimationName);
        NextSpinnerIcon.AbortAnimation("ScaleToAsync");
        NextSpinnerIcon.AbortAnimation("FadeToAsync");
        NextChevronIcon.AbortAnimation("ScaleToAsync");
        NextChevronIcon.AbortAnimation("FadeToAsync");
        NextCheckIcon.AbortAnimation("ScaleToAsync");
        NextCheckIcon.AbortAnimation("RotateToAsync");
        NextCheckIcon.AbortAnimation("FadeToAsync");

        var dots = new[] { StepDot0, StepDot1, StepDot2, StepDot3, StepDot4, StepDot5 };
        foreach (var dot in dots)
        {
            dot.AbortAnimation(DotAnimationName);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var viewModel = (OnboardingViewModel)BindingContext;
        switch (e.PropertyName)
        {
            case nameof(OnboardingViewModel.CurrentStep):
                UpdateStepDots(viewModel.CurrentStep);
                AnimateNextActionIcon(viewModel.IsFinalStep);
                break;
            case nameof(OnboardingViewModel.IsTransitioning):
                UpdateNextSpinner(viewModel.IsTransitioning, viewModel.IsFinalStep);
                break;
        }
    }

    /// <summary>Reports a platform switch to the view-model, then forces the control back in line
    /// with the real enablement state when the write was refused (last-enabled lock) — a OneWay
    /// binding cannot re-flip a switch the user just moved.</summary>
    private void OnPlatformToggled(object? sender, ToggledEventArgs e)
    {
        if (_syncingPlatformSwitch || sender is not Switch sw || sw.BindingContext is not PlatformChoiceItem item)
        {
            return;
        }

        ViewModel.SetPlatformEnabled(item, e.Value);
        if (sw.IsToggled != item.IsEnabled)
        {
            _syncingPlatformSwitch = true;
            sw.IsToggled = item.IsEnabled;
            _syncingPlatformSwitch = false;
        }
    }

    /// <summary>The mockup's step-panel children that slide/fade between steps (image, badge,
    /// heading, description, and the choice panel when it is visible).</summary>
    private View[] AnimatedStepElements => new View[] { ImageWrapper, BadgeBorder, HeadingLabel, DescriptionLabel, ChoicePanel };

    private async Task AnimateStepExit(bool forward)
    {
        if (MotionPreferences.IsReducedMotionRequested) return;

        var exitOffset = forward ? -30 : 30;
        var tasks = new List<Task>();
        foreach (var element in AnimatedStepElements)
        {
            if (!element.IsVisible) continue;
            tasks.Add(Task.WhenAll(
                element.TranslateToAsync(exitOffset, 0, 180, Easing.CubicIn),
                element.FadeToAsync(0, 180, Easing.CubicIn)));
        }
        await Task.WhenAll(tasks);
    }

    private async Task AnimateStepEnter(bool forward)
    {
        var elements = AnimatedStepElements;

        if (MotionPreferences.IsReducedMotionRequested)
        {
            foreach (var element in elements)
            {
                element.TranslationX = 0;
                element.Opacity = 1;
            }
            return;
        }

        var enterOffset = forward ? 34 : -34;
        var tasks = new List<Task>();
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            if (!element.IsVisible)
            {
                element.TranslationX = 0;
                element.Opacity = 1;
                continue;
            }

            element.TranslationX = enterOffset;
            element.Opacity = 0;
            tasks.Add(AnimateElementEnter(element, staggerDelayMs: i * 40));
        }
        await Task.WhenAll(tasks);
    }

    private static async Task AnimateElementEnter(View element, int staggerDelayMs)
    {
        if (staggerDelayMs > 0) await Task.Delay(staggerDelayMs);
        await Task.WhenAll(
            element.TranslateToAsync(0, 0, 260, Easing.CubicOut),
            element.FadeToAsync(1, 260, Easing.CubicOut));
    }

    /// <summary>Mirrors the mockup's `.next-spinner` continuous rotation (`animation: next-spin
    /// 0.6s linear infinite`) while the step transition (`.is-loading`) is in flight, and its
    /// icon-stack swap (icon scales/rotates away while the spinner scales/fades in).</summary>
    private void UpdateNextSpinner(bool isTransitioning, bool isFinalStep)
    {
        NextSpinnerIcon.AbortAnimation(SpinnerAnimationName);
        var activeActionIcon = isFinalStep ? NextCheckIcon : NextChevronIcon;

        if (MotionPreferences.IsReducedMotionRequested)
        {
            NextSpinnerIcon.Rotation = 0;
            NextSpinnerIcon.Scale = isTransitioning ? 1 : 0.5;
            NextSpinnerIcon.Opacity = isTransitioning ? 1 : 0;
            NextSpinnerIcon.IsVisible = isTransitioning;
            activeActionIcon.Opacity = isTransitioning ? 0 : 1;
            activeActionIcon.Scale = isTransitioning ? 0.5 : 1;
            return;
        }

        if (isTransitioning)
        {
            NextSpinnerIcon.IsVisible = true;
            NextSpinnerIcon.Rotation = 0;
            _ = Task.WhenAll(
                NextSpinnerIcon.ScaleToAsync(1, 240, Easing.SpringOut),
                NextSpinnerIcon.FadeToAsync(1, 200, Easing.CubicOut),
                activeActionIcon.ScaleToAsync(0.4, 180, Easing.CubicIn),
                activeActionIcon.FadeToAsync(0, 180, Easing.CubicIn));

            var animation = new Animation(v => NextSpinnerIcon.Rotation = v, 0, 360);
            animation.Commit(NextSpinnerIcon, SpinnerAnimationName, 16, 600, Easing.Linear,
                repeat: () => NextSpinnerIcon.IsVisible);
        }
        else
        {
            _ = Task.WhenAll(
                NextSpinnerIcon.ScaleToAsync(0.4, 180, Easing.CubicIn),
                NextSpinnerIcon.FadeToAsync(0, 180, Easing.CubicIn),
                activeActionIcon.ScaleToAsync(1.0, 240, Easing.SpringOut),
                activeActionIcon.FadeToAsync(1.0, 200, Easing.CubicOut)).ContinueWith(_ =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!((OnboardingViewModel)BindingContext).IsTransitioning)
                        {
                            NextSpinnerIcon.IsVisible = false;
                        }
                    });
                });
        }
    }

    private async void AnimateNextActionIcon(bool isFinalStep)
    {
        if (MotionPreferences.IsReducedMotionRequested)
        {
            NextChevronIcon.Scale = isFinalStep ? 0.5 : 1;
            NextChevronIcon.Opacity = isFinalStep ? 0 : 1;
            NextCheckIcon.Scale = isFinalStep ? 1 : 0.5;
            NextCheckIcon.Opacity = isFinalStep ? 1 : 0;
            NextCheckIcon.Rotation = 0;
            return;
        }

        if (isFinalStep)
        {
            _ = Task.WhenAll(
                NextChevronIcon.ScaleToAsync(0.4, 160, Easing.CubicIn),
                NextChevronIcon.FadeToAsync(0, 160, Easing.CubicIn));

            NextCheckIcon.Scale = 0.35;
            NextCheckIcon.Opacity = 0;
            NextCheckIcon.Rotation = -25;
            await Task.WhenAll(
                NextCheckIcon.FadeToAsync(1, 180, Easing.CubicOut),
                NextCheckIcon.ScaleToAsync(1.25, 240, Easing.CubicOut));
            await Task.WhenAll(
                NextCheckIcon.ScaleToAsync(1.0, 160, Easing.CubicIn),
                NextCheckIcon.RotateToAsync(0, 160, Easing.CubicIn));
        }
        else
        {
            _ = Task.WhenAll(
                NextCheckIcon.ScaleToAsync(0.4, 160, Easing.CubicIn),
                NextCheckIcon.FadeToAsync(0, 160, Easing.CubicIn));

            NextChevronIcon.Scale = 0.4;
            NextChevronIcon.Opacity = 0;
            await Task.WhenAll(
                NextChevronIcon.ScaleToAsync(1.0, 240, Easing.SpringOut),
                NextChevronIcon.FadeToAsync(1.0, 200, Easing.CubicOut));
        }
    }

    /// <summary>Mirrors the mockup's `.dot` transition (`transition: all 0.25s ease`) instead of
    /// snapping the width/color of the step indicator instantly.</summary>
    private void UpdateStepDots(int currentStep, bool animate = true)
    {
        var inactiveColor = MostaqlK.UI.DesignSystem.Tokens.ThemeTokens.Get("OnboardingDotInactive");
        var activeColor = MostaqlK.UI.DesignSystem.Tokens.ThemeTokens.Get("OnboardingDotActive");
        var dots = new[] { StepDot0, StepDot1, StepDot2, StepDot3, StepDot4, StepDot5 };

        for (var i = 0; i < dots.Length; i++)
        {
            var isActive = i == currentStep;
            var targetWidth = isActive ? 20d : 8d;
            var targetColor = isActive ? activeColor : inactiveColor;

            if (!animate || MotionPreferences.IsReducedMotionRequested)
            {
                dots[i].AbortAnimation(DotAnimationName);
                dots[i].WidthRequest = targetWidth;
                dots[i].Color = targetColor;
                continue;
            }

            AnimateDot(dots[i], targetWidth, targetColor);
        }
    }

    private static void AnimateDot(BoxView dot, double targetWidth, Color targetColor)
    {
        dot.AbortAnimation(DotAnimationName);
        var startWidth = dot.WidthRequest;
        var startColor = dot.Color ?? targetColor;

        var animation = new Animation(v =>
        {
            dot.WidthRequest = startWidth + (targetWidth - startWidth) * v;
            dot.Color = Color.FromRgba(
                startColor.Red + (targetColor.Red - startColor.Red) * v,
                startColor.Green + (targetColor.Green - startColor.Green) * v,
                startColor.Blue + (targetColor.Blue - startColor.Blue) * v,
                startColor.Alpha + (targetColor.Alpha - startColor.Alpha) * v);
        }, 0, 1);
        animation.Commit(dot, DotAnimationName, 16, 220, Easing.CubicOut);
    }
}

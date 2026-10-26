using System.ComponentModel;
using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.UI.DesignSystem.BrandTokens;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class ProjectCardWindowsLayout : ContentView
{
    private const float UnreadHoverBrandWeight = 0.09f;

    /// <summary>Length of the unread→read cross-fade (wash + dot + ribbon). Reduced motion collapses it to 0.</summary>
    private const uint ReadTransitionMs = 260;

    private ProjectCardViewModel? _boundViewModel;
    private bool _wasUnread;

    public ProjectCardWindowsLayout()
    {
        InitializeComponent();

        // projects.html hover: read rows wash to --surface-2, unread rows to a 9% brand mix.
        // A pointer recognizer (the SplitterHandle-proven Windows hover seam) drives the
        // dedicated HoverLayer, leaving the root's theme-bound BackgroundColor untouched.
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => ApplyRowHover(true);
        pointer.PointerExited += (_, _) => ApplyRowHover(false);
        ProjectCard_Root.GestureRecognizers.Add(pointer);

        // Hand cursor is declarative, not per-hover: WinUI hit-tests it from the element itself, so
        // setting ProtectedCursor once when the platform view exists survives minimize/restore and
        // pointer re-entry (an enter/exit-driven cursor goes stale when those events unbalance).
        HandlerChanged += (_, _) => SetRowCursorCore(true);
    }

    public ProjectCardWindowsLayout(ProjectCardViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }

    /// <summary>Windows half sets the row's ProtectedCursor to Hand — WinUI then shows it for the
    /// whole row subtree automatically; no-op on other platforms (this file compiles everywhere).</summary>
    partial void SetRowCursorCore(bool hand);

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _boundViewModel = BindingContext as ProjectCardViewModel;
        if (_boundViewModel is null)
        {
            return;
        }

        _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Recycled containers land here with a fresh VM: snap to its state, never animate.
        _wasUnread = _boundViewModel.IsUnread;
        ApplyUnreadVisuals(_wasUnread);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProjectCardViewModel.IsUnread) || _boundViewModel is null)
        {
            return;
        }

        var unread = _boundViewModel.IsUnread;
        if (_wasUnread && !unread)
        {
            FadeOutUnreadVisuals();
        }
        else
        {
            ApplyUnreadVisuals(unread);
        }

        _wasUnread = unread;
    }

    private void ApplyUnreadVisuals(bool unread)
    {
        // A still-running mark-read fade must not overwrite a recycled container's fresh state.
        UnreadWash.CancelAnimations();
        UnreadIndicator.CancelAnimations();

        UnreadWash.Opacity = 0.06;
        UnreadWash.IsVisible = unread;
        UnreadIndicator.Opacity = 1;
        UnreadIndicator.IsVisible = unread;
    }

    /// <summary>The professional mark-read flip: wash and unread dot fade out together (the ribbon
    /// runs its own matching fade), so "تحديد الكل كمقروء" reads as a wave across the feed instead
    /// of a hard cut. Opacity-only — no transform objects (animation-safety.md rule 2).</summary>
    private async void FadeOutUnreadVisuals()
    {
        var duration = MotionPreferences.IsReducedMotionRequested ? 0u : ReadTransitionMs;

        _ = UnreadWash.FadeToAsync(0, duration, Easing.CubicOut);
        await UnreadIndicator.FadeToAsync(0, duration, Easing.CubicOut);

        // A re-activation (unread again, or container rebind) during the fade owns the state now.
        if (_wasUnread)
        {
            return;
        }

        UnreadWash.IsVisible = false;
        UnreadIndicator.IsVisible = false;
    }

    private void ApplyRowHover(bool hovered)
    {
        if (!hovered)
        {
            HoverLayer.IsVisible = false;
            return;
        }

        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        if (BindingContext is ProjectCardViewModel { IsUnread: true })
        {
            var surface = dark ? Color.FromArgb("#1E293B") : Colors.White;
            var brand = BrandPalette.Brand;
            HoverLayer.Color = new Color(
                (brand.Red * UnreadHoverBrandWeight) + (surface.Red * (1 - UnreadHoverBrandWeight)),
                (brand.Green * UnreadHoverBrandWeight) + (surface.Green * (1 - UnreadHoverBrandWeight)),
                (brand.Blue * UnreadHoverBrandWeight) + (surface.Blue * (1 - UnreadHoverBrandWeight)));
        }
        else
        {
            HoverLayer.Color = dark ? Color.FromArgb("#172032") : Color.FromArgb("#F8FAFC");
        }

        HoverLayer.IsVisible = true;
    }
}

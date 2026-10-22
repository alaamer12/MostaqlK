using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class ProjectCardWindowsLayout : ContentView
{
    private const float UnreadHoverBrandWeight = 0.09f;

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
    }

    public ProjectCardWindowsLayout(ProjectCardViewModel viewModel) : this()
    {
        BindingContext = viewModel;
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

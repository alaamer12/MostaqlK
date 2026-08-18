using MostaqlK.Features.Projects.Views.Layouts;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Projects.Views;

public partial class AboutPage : ContentPage
{
    private readonly View? _activeLayout;

    public AboutPage()
    {
        InitializeComponent();

        var layoutFactory = PlatformSelect.For<Func<View>>(
            windows: () => new AboutPageWindowsLayout(),
            android: () => new AboutPageMobileLayout(),
            ios: () => new AboutPageMobileLayout(),
            macCatalyst: () => new AboutPageWindowsLayout()
        );
        _activeLayout = layoutFactory?.Invoke();
        Content = _activeLayout;
    }
}

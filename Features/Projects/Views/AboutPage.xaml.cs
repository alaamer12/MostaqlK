using MostaqlK.Features.Projects.Views.Layouts;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Projects.Views;

public partial class AboutPage : ContentPage
{
    private readonly View? _activeLayout;

    public AboutPage(Services.GlobalAppStatusService globalStatus)
    {
        InitializeComponent();
        BindingContext = globalStatus;

        var layoutFactory = PlatformSelect.For<Func<View>>(
            windows: () => new AboutPageWindowsLayout(globalStatus),
            android: () => new AboutPageMobileLayout(),
            ios: () => new AboutPageMobileLayout(),
            macCatalyst: () => new AboutPageWindowsLayout(globalStatus)
        );
        _activeLayout = layoutFactory?.Invoke();
        Content = _activeLayout;
    }
}

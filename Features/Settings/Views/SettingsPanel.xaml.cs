using MostaqlK.Features.Settings.ViewModels;
using MostaqlK.Features.Settings.Views.Layouts;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Settings.Views;

public partial class SettingsPanel : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private readonly View? _activeLayout;

    public SettingsPanel(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        var layoutFactory = PlatformSelect.For<Func<View>>(
            windows: () => new SettingsPanelWindowsLayout(_viewModel),
            android: () => new SettingsPanelMobileLayout(_viewModel),
            ios: () => new SettingsPanelMobileLayout(_viewModel),
            macCatalyst: () => new SettingsPanelWindowsLayout(_viewModel)
        );
        _activeLayout = layoutFactory?.Invoke();
        Content = _activeLayout;
    }
}

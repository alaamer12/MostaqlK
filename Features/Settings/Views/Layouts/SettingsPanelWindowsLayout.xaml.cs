using MostaqlK.Core.Navigation;
using MostaqlK.Features.Settings.ViewModels;

namespace MostaqlK.Features.Settings.Views.Layouts;

public partial class SettingsPanelWindowsLayout : ContentView
{
    public SettingsPanelWindowsLayout()
    {
        InitializeComponent();
    }

    public SettingsPanelWindowsLayout(SettingsViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }

    private async void OnProjectsNavClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.Projects);
    }

    private async void OnAdvancedSearchNavClicked(object? sender, EventArgs e)
    {
        await Task.CompletedTask;
    }

    private async void OnSettingsNavClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.Settings);
    }

    private async void OnAboutNavClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.About);
    }
}

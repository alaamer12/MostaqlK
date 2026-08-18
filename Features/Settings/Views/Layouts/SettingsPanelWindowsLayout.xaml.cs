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
        await Shell.Current.GoToAsync("//MainWindowPage");
    }

    private async void OnAdvancedSearchNavClicked(object? sender, EventArgs e)
    {
        await Task.CompletedTask;
    }

    private async void OnSettingsNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//SettingsPanel");
    }

    private async void OnAboutNavClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//AboutPage");
    }
}

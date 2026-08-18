using MostaqlK.Features.Settings.ViewModels;

namespace MostaqlK.Features.Settings.Views.Layouts;

public partial class SettingsPanelMobileLayout : ContentView
{
    public SettingsPanelMobileLayout()
    {
        InitializeComponent();
    }

    public SettingsPanelMobileLayout(SettingsViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainWindowPage");
    }
}

using MostaqlK.Features.Settings.ViewModels;

namespace MostaqlK.Features.Settings.Views;

public partial class SettingsPanel : ContentPage
{
    public SettingsPanel(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}

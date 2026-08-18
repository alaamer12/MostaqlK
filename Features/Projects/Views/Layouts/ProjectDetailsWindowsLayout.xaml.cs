using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class ProjectDetailsWindowsLayout : ContentView
{
    public ProjectDetailsWindowsLayout()
    {
        InitializeComponent();
    }

    public ProjectDetailsWindowsLayout(ProjectDetailsViewModel viewModel) : this()
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

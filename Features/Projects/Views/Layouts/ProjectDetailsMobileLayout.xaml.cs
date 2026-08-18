using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class ProjectDetailsMobileLayout : ContentView
{
    public ProjectDetailsMobileLayout()
    {
        InitializeComponent();
    }

    public ProjectDetailsMobileLayout(ProjectDetailsViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainWindowPage");
    }
}

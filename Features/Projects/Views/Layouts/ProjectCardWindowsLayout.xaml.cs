using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class ProjectCardWindowsLayout : ContentView
{
    public ProjectCardWindowsLayout()
    {
        InitializeComponent();
    }

    public ProjectCardWindowsLayout(ProjectCardViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }
}

using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK.Features.Projects.Views;

public partial class ProjectCard : ContentView
{
    public ProjectCard()
    {
        InitializeComponent();
    }

    public ProjectCard(ProjectCardViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }
}

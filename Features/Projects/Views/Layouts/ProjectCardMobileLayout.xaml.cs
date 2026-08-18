using MostaqlK.Features.Projects.ViewModels;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class ProjectCardMobileLayout : ContentView
{
    public ProjectCardMobileLayout()
    {
        InitializeComponent();
    }

    public ProjectCardMobileLayout(ProjectCardViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }
}

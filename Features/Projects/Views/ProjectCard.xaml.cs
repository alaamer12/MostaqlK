using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.Features.Projects.Views.Layouts;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Projects.Views;

public partial class ProjectCard : ContentView
{
    public ProjectCard()
    {
        InitializeComponent();
        var layoutFactory = PlatformSelect.For<Func<View>>(
            windows: () => new ProjectCardWindowsLayout(),
            android: () => new ProjectCardMobileLayout(),
            ios: () => new ProjectCardMobileLayout(),
            macCatalyst: () => new ProjectCardWindowsLayout()
        );
        Content = layoutFactory?.Invoke();
    }

    public ProjectCard(ProjectCardViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }
}

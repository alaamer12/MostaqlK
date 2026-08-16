using System.Windows.Input;

namespace MostaqlK.UI.PlatformComponents;

public partial class MostaqlLinkButton : ContentView
{
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(MostaqlLinkButton));

    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public MostaqlLinkButton()
    {
        InitializeComponent();
    }
}

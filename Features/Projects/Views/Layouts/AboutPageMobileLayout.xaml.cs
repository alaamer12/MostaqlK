namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class AboutPageMobileLayout : ContentView
{
    public AboutPageMobileLayout()
    {
        InitializeComponent();
        VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainWindowPage");
    }

    private async void OnMostaqlLinkTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri("https://mostaql.com"));
        }
        catch (Exception)
        {
            // Ignored
        }
    }
}

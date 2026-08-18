namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class AboutPageWindowsLayout : ContentView
{
    public AboutPageWindowsLayout()
    {
        InitializeComponent();
        VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
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

using MostaqlK.Features.Notifications.ViewModels;
using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.Features.Projects.Views.Layouts;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.Features.Projects.Views;

public partial class MainWindowPage : ContentPage
{
    private readonly ProjectFeedViewModel _viewModel;
    private readonly NotificationCenterViewModel _notificationCenterViewModel;
    private readonly Services.AppLifecycleService _appLifecycleService;
    private readonly View? _activeLayout;

    public MainWindowPage(
        ProjectFeedViewModel viewModel, 
        NotificationCenterViewModel notificationCenterViewModel,
        Services.AppLifecycleService appLifecycleService)
    {
        MostaqlK.Services.Diagnostics.InteractionLogger.Mark("MainWindowPage.Ctor", "A");
        InitializeComponent();
        _viewModel = viewModel;
        _notificationCenterViewModel = notificationCenterViewModel;
        _appLifecycleService = appLifecycleService;
        BindingContext = _viewModel;

        var layoutFactory = PlatformSelect.For<Func<View>>(
            windows: () => new MainWindowWindowsLayout(_notificationCenterViewModel),
            android: () => new MainWindowMobileLayout(_notificationCenterViewModel),
            ios: () => new MainWindowMobileLayout(_notificationCenterViewModel),
            macCatalyst: () => new MainWindowWindowsLayout(_notificationCenterViewModel)
        );
        _activeLayout = layoutFactory?.Invoke();
        Content = _activeLayout;
    }

    /// <summary>Shows the recent-notifications flyout overlay, used both by the sidebar entry and the tray icon's "Recent notifications" menu action.</summary>
    public void OpenNotificationsFlyout()
    {
        if (_activeLayout is MainWindowWindowsLayout windowsLayout)
        {
            windowsLayout.OpenNotificationsFlyout();
        }
        else if (_activeLayout is MainWindowMobileLayout mobileLayout)
        {
            mobileLayout.OpenNotificationsFlyout();
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        _appLifecycleService.IsReadyToNotify = true;
    }
}

using MostaqlK.Features.Notifications.ViewModels;

namespace MostaqlK.Features.Notifications.Views;

public partial class RecentNotificationsFlyout : ContentView
{
    public RecentNotificationsFlyout()
    {
        InitializeComponent();
    }

    public RecentNotificationsFlyout(NotificationCenterViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }
}

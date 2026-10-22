using MostaqlK.Core.Navigation;
using MostaqlK.Core.Sources;
using MostaqlK.Features.Notifications.ViewModels;
using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.UI.PlatformComponents.SourceRail;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class MainWindowWindowsLayout : ContentView
{
    private readonly NotificationCenterViewModel _notificationCenterViewModel;

    public MainWindowWindowsLayout(NotificationCenterViewModel notificationCenterViewModel)
    {
        InitializeComponent();
        _notificationCenterViewModel = notificationCenterViewModel;
        NotificationsFlyout.BindingContext = _notificationCenterViewModel;
        NotificationsFlyout.CloseRequested += OnNotificationsFlyoutCloseRequested;

        // B3 FilterModal: the unit renders + collects; persistence goes through the feed
        // view-model's single settings-mutation path (mirrors Settings.OnQueryParamsChanged).
        FiltersModal.ApplyRequested += OnFiltersApplyRequested;
        FiltersModal.CloseRequested += (_, _) => FiltersModal.IsVisible = false;
    }

    /// <summary>Opens the typed-facet dialog, pre-filled through the active descriptor's own parser.</summary>
    private void OnOpenFiltersTapped(object? sender, TappedEventArgs e)
    {
        if (BindingContext is not ProjectFeedViewModel feed
            || feed.FilterDescriptor is not { } descriptor
            || descriptor.FilterFacets.Count == 0)
        {
            // Presence-by-data: no vocabulary → no dialog (the affordance is hidden too, this is
            // the belt-and-braces guard for a morph racing the tap).
            return;
        }

        FiltersModal.Open(descriptor, feed.FilterStoredQuery);
        FiltersModal.IsVisible = true;
    }

    private void OnFiltersApplyRequested(object? sender, IReadOnlyDictionary<string, IReadOnlyList<string>> selection)
    {
        if (BindingContext is ProjectFeedViewModel feed)
        {
            feed.ApplyFilterSelection(selection);
        }

        FiltersModal.IsVisible = false;
    }

    /// <summary>Shows the recent-notifications flyout overlay, used both by the sidebar entry and the tray icon's "Recent notifications" menu action.</summary>
    public void OpenNotificationsFlyout()
    {
        SetNotificationsFlyoutVisible(true);
    }

    /// <summary>
    /// Opening the notifications menu marks every notification as read (the unread badge, not
    /// the individual project cards in the feed - those keep their own separate read state).
    /// Closing it back does nothing: only the act of opening should count as "seen".
    /// </summary>
    private void SetNotificationsFlyoutVisible(bool visible)
    {
        NotificationsFlyout.IsVisible = visible;
        NotificationsBackdrop.IsVisible = visible;
        if (visible)
        {
            _notificationCenterViewModel.MarkAllAsSeen();
        }
    }

    private void OnNotificationsFlyoutCloseRequested(object? sender, EventArgs e) =>
        SetNotificationsFlyoutVisible(false);

    private void OnNotificationsBackdropTapped(object? sender, TappedEventArgs e) =>
        SetNotificationsFlyoutVisible(false);

    /// <summary>
    /// Keeps the pipeline panel's resize range honest: the panel may never grow past the point
    /// where the project feed would drop under its own minimum width, so panning simply stops
    /// there instead of squeezing the cards.
    /// </summary>
    private void OnRootSizeChanged(object? sender, EventArgs e)
    {
        const double minimumFeedWidth = 520;

        // The rail width is the shared RailWidths token, not a local copy of the old 256px sidebar:
        // resizing the shell strip changes ONE constant (UI/PlatformComponents/SourceRail/RailWidths.cs)
        // and this clamp follows automatically.
        var available = Root.Width - RailWidths.Width - minimumFeedWidth;
        PanelSplitter.Maximum = Math.Max(PanelSplitter.Minimum, available);
    }

    private void OnPanelSplitterDragCompleted(object? sender, double width) =>
        PipelinePanel.PersistWidth();

    private void OnProjectsNavClicked(object? sender, EventArgs e)
    {
        // Already on the projects feed — no-op for now.
    }

    /// <summary>
    /// The global morph projection (persist + status facade + brand swap) already ran inside the
    /// rail; here the only host-specific work is re-pointing this page's feed at the freshly-active
    /// source so the list shows that source's rows only.
    /// </summary>
    private async void OnSourceChanged(object? sender, SourceId source)
    {
        if (BindingContext is ProjectFeedViewModel feed)
        {
            await feed.SwitchSourceAsync(source);
        }
    }

    private async void OnAdvancedSearchNavClicked(object? sender, EventArgs e)
    {
        // TODO: navigate to the advanced search route once implemented.
        await Task.CompletedTask;
    }

    private async void OnSettingsNavClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.Settings);
    }

    private void OnNotificationsButtonTapped(object? sender, TappedEventArgs e)
    {
        SetNotificationsFlyoutVisible(!NotificationsFlyout.IsVisible);
    }

    /// <summary>
    /// Rail bell (v3 contract §3/D7 — the notification entry lives in the rail, not a page header):
    /// same toggle semantics as the in-header bell button above.
    /// </summary>
    private void OnRailNotificationsClicked(object? sender, EventArgs e)
    {
        SetNotificationsFlyoutVisible(!NotificationsFlyout.IsVisible);
    }

    private async void OnAboutNavClicked(object? sender, EventArgs e)
    {
        await AppRoutes.NavigateAsync(AppRoutes.About);
    }
}

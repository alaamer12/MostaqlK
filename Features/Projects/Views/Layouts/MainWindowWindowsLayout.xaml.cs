using MostaqlK.Core.Navigation;
using MostaqlK.Core.Sources;
using MostaqlK.Features.Notifications.ViewModels;
using MostaqlK.Features.Projects.ViewModels;
using MostaqlK.UI.PlatformComponents.ContentSettle;
using MostaqlK.UI.PlatformComponents.PipelineDashboard;
using MostaqlK.UI.PlatformComponents.SourceRail;

namespace MostaqlK.Features.Projects.Views.Layouts;

public partial class MainWindowWindowsLayout : ContentView
{
    private readonly NotificationCenterViewModel _notificationCenterViewModel;

    /// <summary>The feed VM whose <c>BatchApplied</c> event currently drives this layout's settle.</summary>
    private ProjectFeedViewModel? _settledFeed;

    public MainWindowWindowsLayout(NotificationCenterViewModel notificationCenterViewModel)
    {
        InitializeComponent();
        _notificationCenterViewModel = notificationCenterViewModel;
        NotificationsFlyout.BindingContext = _notificationCenterViewModel;

        // Bell badge = the same source-scoped inbox the flyout renders. The global
        // UnreadNotificationCount is app-wide, which showed a badge over an empty dropdown when
        // the unread items belonged to the other source.
        Rail.UnreadCount = _notificationCenterViewModel.UnreadBadgeCount;
        _notificationCenterViewModel.PropertyChanged += OnNotificationCenterPropertyChanged;
        NotificationsFlyout.CloseRequested += OnNotificationsFlyoutCloseRequested;

        // v3 mount polish: hand the feed's CollectionView to ContentSettle so a source/theme morph
        // can land the realized cards with the mockup's `card-settle` (also reachable post-reveal via
        // ContentSettle.RunForActiveFeed() without this layout exposing the control).
        ContentSettle.RegisterActiveFeed(ProjectsCollectionView);

        // B3 FilterModal: the unit renders + collects; persistence goes through the feed
        // view-model's single settings-mutation path (mirrors Settings.OnQueryParamsChanged).
        FiltersModal.ApplyRequested += OnFiltersApplyRequested;
        FiltersModal.CloseRequested += (_, _) => FiltersModal.IsVisible = false;

        // The shell columns are explicit tracks (see the XAML note): keep the panel/splitter
        // tracks in step with the panel's live width so the feed star is always resolved with the
        // rail and panel already accounted for. Without this the rail was laid out past the
        // window's physical-left edge and the feed clipped mid-content.
        SyncShellColumns();
        PipelinePanel.PropertyChanged += OnPipelinePanelPropertyChanged;
    }

    private void OnPipelinePanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(View.WidthRequest) or nameof(PipelineDashboardPanel.IsExpanded))
        {
            SyncShellColumns();
        }
    }

    private void OnNotificationCenterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationCenterViewModel.UnreadBadgeCount))
        {
            Rail.UnreadCount = _notificationCenterViewModel.UnreadBadgeCount;
        }
    }

    /// <summary>
    /// Mirrors the panel's live width (including its 240ms expand/collapse slide) onto the root
    /// grid's absolute panel/splitter tracks. The rail track is fixed from the shared
    /// <see cref="RailWidths.ColumnWidth"/> token.
    /// </summary>
    private void SyncShellColumns()
    {
        var panelWidth = PipelinePanel.WidthRequest > 0
            ? PipelinePanel.WidthRequest
            : Math.Max(PipelineDashboardPanel.RailWidth, PipelinePanel.ExpandedWidth);
        var splitterWidth = PipelinePanel.IsExpanded ? PanelSplitter.WidthRequest : 0;

        SetColumnWidth(0, panelWidth);
        SetColumnWidth(1, splitterWidth);
    }

    private void SetColumnWidth(int index, double width)
    {
        var track = new GridLength(Math.Max(0, width));
        if (!Root.ColumnDefinitions[index].Width.Equals(track))
        {
            Root.ColumnDefinitions[index].Width = track;
        }
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
        _notificationCenterViewModel.SetInboxOpen(visible);
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

    private void OnSourceChanged(object? sender, SourceId source)
    {
        TryHookSettle();
        if (BindingContext is ProjectFeedViewModel feed)
        {
            // v3 frame decoupling (Step 4-settle): the re-point is a SYNCHRONOUS memory paint —
            // SwitchSource never awaits the DB anymore, so the settle cannot hang off this call.
            // It re-times off the store's snapshot-applied batches instead (BatchApplied below);
            // the feed paints Peek(target) this frame and refines when the coalesced pass lands.
            _ = feed.SwitchSourceAsync(source);
        }
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        TryHookSettle();
    }

    /// <summary>
    /// (Re)attaches the settle trigger to the current feed VM: <c>SourceFeedStore</c> batches are
    /// the only thing that now starts a mount pass — the snapshot-applied paint-complete frame
    /// replaces the old "after the switch await" trigger (v3 plan Step 4-settle).
    /// </summary>
    private void TryHookSettle()
    {
        var feed = BindingContext as ProjectFeedViewModel;
        if (ReferenceEquals(feed, _settledFeed))
        {
            return;
        }

        if (_settledFeed is not null)
        {
            _settledFeed.BatchApplied -= OnFeedBatchApplied;
        }

        _settledFeed = feed;
        if (feed is not null)
        {
            feed.BatchApplied += OnFeedBatchApplied;
        }
    }

    private void OnFeedBatchApplied(object? sender, EventArgs e) => NotifyFeedRepopulated();

    /// <summary>
    /// Runs the one-shot <see cref="ContentSettle"/> mount pass over the feed's realized cards.
    /// Deferred one dispatcher frame so the just-applied snapshot batch has completed its first
    /// layout pass — this is now the <b>paint-complete frame</b> of a real batch (v3 plan Step 4:
    /// the settle trigger moved off the data-await + forced <c>UpdateLayout</c>; the Windows
    /// compositor pass itself realizes off <c>ListViewBase.ContainerContentChanging</c>). No-op
    /// (instant) under reduced motion / tray-hidden / non-Windows; a new call fast-finishes any
    /// in-flight pass. Public so the page (first open) and the morph presenter (post-reveal) can
    /// drive the same single entrypoint.
    /// </summary>
    public void NotifyFeedRepopulated()
    {
        if (ProjectsCollectionView is null)
        {
            return;
        }

        Dispatcher.Dispatch(() => ContentSettle.RunFor(ProjectsCollectionView));
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

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

        // v3 mount polish: the design's page-open moment is a `card-settle` — the realized cards land
        // with the transform-only rise instead of painting flat. One-shot and self-cancelling; the
        // windows layout's notify is a no-op on reduced-motion / non-Windows, so mobile is unchanged.
        if (_activeLayout is MainWindowWindowsLayout windowsLayout)
        {
            windowsLayout.NotifyFeedRepopulated();
        }

        // v3 parity harness: --parity-state=filters-open forces the feed's one interactive
        // modal open at startup, through the SAME unit entry points the real الفلاتر button
        // uses (FilterModal.Open + visibility). IDEMPOTENT: every OTHER state — default /
        // panel-collapsed / no flag — now EXPLICITLY closes the modal, so the harness's
        // single-instance redirect (a running filters-open primary handed a default activation) can
        // never leak the open dialog into a default capture. The tree walk keeps the shell/layout
        // files harness-free; with no flag this method never runs and behavior is unchanged.
        ApplyParityFiltersState();

        // Defer background pipeline startup until after UI has loaded and rendered
        if (Application.Current is App app)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(300); // 300ms breathing delay for WinUI compositor and splash dismissal
                app.EnsurePipelineStarted();
            });
        }
    }

    /// <summary>
    /// Leaves no interactive surface open behind the page: the FilterModal is hosted inside this
    /// page's layout, and a reused process (single-instance redirect, or a back-navigation to the
    /// feed later) must never present a dialog the CURRENT state never asked for. The modal itself
    /// has no persisted open-state — this is the belt closing it at the exact seam navigation
    /// happens, so the parity matrix's per-state captures (and real navigation) can never inherit
    /// an open filter dialog from an earlier activation.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_activeLayout is not null && FindFilterModal(_activeLayout) is { IsVisible: true } modal)
        {
            modal.IsVisible = false;
            MostaqlK.Services.Diagnostics.InteractionLogger.Mark("MainWindowPage.FilterModalClosedOnNav", "A");
        }
    }

    /// <summary>
    /// Forces the filters modal into exactly the state the current launch requested — open ONLY for an
    /// explicit <c>--parity-state=filters-open</c>, closed for every other value. Because the app
    /// self-registers as a single instance, a fresh default launch and a redirected default activation
    /// both re-enter here, so a dialog left open by an earlier filters-open activation is reset closed
    /// rather than latching (the root cause of the "filters modal open in a default capture" leak).
    /// </summary>
    private void ApplyParityFiltersState()
    {
        var args = Environment.GetCommandLineArgs();
        if (Services.ParityLaunchFlags.ShouldOpenFiltersModal(args))
        {
            OpenFilterModalForParity();
            return;
        }

        // Any non-filters-open launch: guarantee the dialog is not presenting. Cheap no-op when the
        // modal was never opened (a normal user launch); a real reset for a reused process.
        if (_activeLayout is not null && FindFilterModal(_activeLayout) is { } modal && modal.IsVisible)
        {
            modal.IsVisible = false;
            MostaqlK.Services.Diagnostics.InteractionLogger.Mark("MainWindowPage.ParityFiltersClosed", "A");
        }
    }


    private void OpenFilterModalForParity()
    {
        if (_activeLayout is null)
        {
            return;
        }

        if (FindFilterModal(_activeLayout) is not { } modal)
        {
            MostaqlK.Services.Diagnostics.InteractionLogger.Mark("MainWindowPage.ParityFiltersOpen", "B", new { reason = "modal not found in layout" });
            return;
        }

        if (_viewModel.FilterDescriptor is not { } descriptor)
        {
            return;
        }

        modal.Open(descriptor, _viewModel.FilterStoredQuery);
        modal.IsVisible = true;
        MostaqlK.Services.Diagnostics.InteractionLogger.Mark("MainWindowPage.ParityFiltersOpen", "A");
    }

    /// <summary>
    /// Finds the <see cref="MostaqlK.UI.PlatformComponents.FilterModal.FilterModal"/> instance the
    /// active layout hosts. Walks the element tree through the standard MAUI container properties
    /// (<c>Content</c> / <c>Children</c> / <c>Views</c>) via reflection so this page does not
    /// depend on any layout-specific public surface the shell wave might reshape.
    /// </summary>
    private static MostaqlK.UI.PlatformComponents.FilterModal.FilterModal? FindFilterModal(object? root, int depth = 0)
    {
        if (root is null || depth > 24)
        {
            return null;
        }

        if (root is MostaqlK.UI.PlatformComponents.FilterModal.FilterModal modal)
        {
            return modal;
        }

        var type = root.GetType();
        var nested = type.GetProperty("Content", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?.GetValue(root);
        if (FindFilterModal(nested, depth + 1) is { } fromContent)
        {
            return fromContent;
        }

        foreach (var name in new[] { "Children", "Views" })
        {
            if (type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                ?.GetValue(root) is System.Collections.IEnumerable children)
            {
                foreach (var child in children)
                {
                    if (FindFilterModal(child, depth + 1) is { } found)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }
}

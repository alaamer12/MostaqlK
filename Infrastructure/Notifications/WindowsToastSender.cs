using MostaqlK.Core;
using MostaqlK.Models;
using MostaqlK.Services;
using MostaqlK.Services.Diagnostics;
using Microsoft.Windows.AppNotifications;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// Dual-variation toast handler that orchestrates between modern Windows App SDK
/// notifications and robust WinRT fallbacks.
/// Part of the "winToast-handler" logic mapping.
/// </summary>
public sealed class WindowsToastSender
{
    private static readonly object InitializeLock = new();
    private static IToastVariation? _activeVariation;
    private static bool _initialized;

    private readonly AppLifecycleService _appLifecycleService;

    public WindowsToastSender(AppLifecycleService appLifecycleService)
    {
        _appLifecycleService = appLifecycleService;
    }

    /// <summary>
    /// Ensures the appropriate notification backend is selected and registered.
    /// Performs a one-time check on startup to see if Windows App SDK is supported.
    /// </summary>
    public static void EnsureRegisteredEagerly() => Initialize();

    [ErrorOutcome(ErrorOutcome.Handled, Label = "Toast delivery failure surfaced as Result<bool>.Err")]
    public Task<Result<bool>> SendAsync(IReadOnlyList<ProjectSummary> projects, CancellationToken cancellationToken = default)
    {
        if (projects.Count == 0)
        {
            return Task.FromResult(Result<bool>.Ok(true));
        }

        Initialize();

        var isInBackground = _appLifecycleService.IsInBackground;
        var isReady = _appLifecycleService.IsReadyToNotify;

        if (!isReady || !isInBackground)
        {
            InteractionLogger.Mark("WindowsToastSender.SendAsync", "C", new 
            { 
                Reason = "skip-not-in-background-or-not-ready", 
                IsReady = isReady, 
                IsInBackground = isInBackground,
                Count = projects.Count 
            });
            return Task.FromResult(Result<bool>.Ok(true));
        }

        return _activeVariation!.SendAsync(projects);
    }

    private static void Initialize()
    {
        if (_initialized) return;

        lock (InitializeLock)
        {
            if (_initialized) return;

            // Attempt to use WinAppSdk first. We check IsSupported and Setting.
            // If Setting is Unsupported, it means the Singleton package is missing/broken.
            try
            {
                var sdkSetting = AppNotificationManager.Default.Setting;
                if (sdkSetting != AppNotificationSetting.Unsupported)
                {
                    InteractionLogger.Mark("WindowsToastSender.Initialize", "A", new { Backend = "WinAppSdk", Setting = sdkSetting.ToString() });
                    _activeVariation = new WinAppSdkVariation();
                }
                else
                {
                    InteractionLogger.Mark("WindowsToastSender.Initialize", "B", new { Backend = "WinRt", Reason = "WinAppSdkUnsupported" });
                    _activeVariation = new WinRtVariation();
                }
            }
            catch (Exception ex)
            {
                // Catching initialization failures (e.g. missing DLLs) to ensure fallback.
                InteractionLogger.Fault("WindowsToastSender.Initialize", ex, new { BackendFallback = "WinRt" });
                _activeVariation = new WinRtVariation();
            }

            _activeVariation.EnsureRegistered();
            _initialized = true;
        }
    }
}

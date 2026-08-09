using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MostaqlK.Services;

namespace MostaqlK.Features.Settings.ViewModels;

/// <summary>
/// View-model for the settings panel (settings.html): poll interval/rate, notification
/// grouping mode/threshold, dark mode, and the "مشاريع مضافة اليوم" stat card.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private int _pollIntervalSeconds = 60;

    [ObservableProperty]
    private int _requestsPerMinute = 10;

    [ObservableProperty]
    private NotificationGroupingMode _groupingMode = NotificationGroupingMode.EndOfMinute;

    [ObservableProperty]
    private int _groupingThreshold = 5;

    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    private int _projectsAddedTodayCount;

    [RelayCommand]
    public Task SaveAsync()
    {
        // TODO: persist settings via configuration/storage layer once implemented.
        return Task.CompletedTask;
    }
}

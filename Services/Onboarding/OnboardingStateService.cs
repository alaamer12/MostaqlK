using Microsoft.Maui.Storage;
using MostaqlK.Core.Sources;
using MostaqlK.Services.Pipeline;

namespace MostaqlK.Services.Onboarding;

/// <summary>Owns the first-run onboarding completion flag and the personalization query contract.</summary>
public sealed class OnboardingStateService
{
    // Not a per-source settings key: a single app-global "has the user onboarded yet" latch, so it
    // stays on the raw flat Preferences store. The personalization query is a per-source setting
    // (SettingsKeys.QueryParams) and goes through the settings repository.
    public const string CompletionPreferenceKey = "onboarding_completed";

    private readonly IPollService _pollService;
    private readonly ISettingsRepository _settings;
    private readonly SourceId _source = SourceId.Default;
    private int _completionRecorded;

    public OnboardingStateService(IPollService pollService, ISettingsRepository settings)
    {
        _pollService = pollService;
        _settings = settings;
    }

    public bool IsCompleted => Preferences.Get(CompletionPreferenceKey, false);

    public string QueryParams => _settings.Get(_source, SettingsKeys.QueryParams, string.Empty);

    public event EventHandler? Completed;

    public void ApplySavedQuery()
    {
        _pollService.QueryParams = QueryParams;
    }

    /// <summary>
    /// Persists the optional query and completion marker exactly once, then notifies the app to open
    /// the normal Shell window. Empty query text intentionally means all projects.
    /// </summary>
    public bool Complete(string? queryParams)
    {
        if (Interlocked.Exchange(ref _completionRecorded, 1) != 0 || IsCompleted)
        {
            return false;
        }

        var normalizedQuery = queryParams?.Trim() ?? string.Empty;
        _settings.Set(_source, SettingsKeys.QueryParams, normalizedQuery);
        _pollService.QueryParams = normalizedQuery;
        Preferences.Set(CompletionPreferenceKey, true);
        _settings.SetGlobal(SettingsKeys.IsPollingActive, true);
        Completed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}

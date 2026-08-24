using Microsoft.Maui.Storage;
using MostaqlK.Services.Pipeline;

namespace MostaqlK.Services.Onboarding;

/// <summary>Owns the first-run onboarding preference and personalization query contract.</summary>
public sealed class OnboardingStateService
{
    public const string CompletionPreferenceKey = "onboarding_completed";
    public const string QueryPreferenceKey = "settings_query_params";

    private readonly IPollService _pollService;
    private int _completionRecorded;

    public OnboardingStateService(IPollService pollService)
    {
        _pollService = pollService;
    }

    public bool IsCompleted => Preferences.Get(CompletionPreferenceKey, false);

    public string QueryParams => Preferences.Get(QueryPreferenceKey, string.Empty);

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
        Preferences.Set(QueryPreferenceKey, normalizedQuery);
        _pollService.QueryParams = normalizedQuery;
        Preferences.Set(CompletionPreferenceKey, true);
        Preferences.Set("settings_is_polling_active", true);
        Completed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
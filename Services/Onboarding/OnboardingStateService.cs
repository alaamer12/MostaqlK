using Microsoft.Maui.Storage;
using MostaqlK.Core.Sources;
using MostaqlK.Services.Pipeline;

namespace MostaqlK.Services.Onboarding;

/// <summary>Owns the first-run onboarding completion flag and the per-platform personalization writes.</summary>
public sealed class OnboardingStateService
{
    // Not a per-source settings key: a single app-global "has the user onboarded yet" latch, so it
    // stays on the raw flat Preferences store. The personalization query is a per-source setting
    // (SettingsKeys.QueryParams) and goes through the settings repository.
    public const string CompletionPreferenceKey = "onboarding_completed";

    private readonly IPollService _pollService;
    private readonly ISettingsRepository _settings;
    private readonly SourceRegistry _registry;
    private readonly SourceId _source = SourceId.Default;
    private int _completionRecorded;

    public OnboardingStateService(IPollService pollService, ISettingsRepository settings, SourceRegistry registry)
    {
        _pollService = pollService;
        _settings = settings;
        _registry = registry;
    }

    public bool IsCompleted => Preferences.Get(CompletionPreferenceKey, false);

    public string QueryParams => _settings.Get(_source, SettingsKeys.QueryParams, string.Empty);

    public event EventHandler? Completed;

    public void ApplySavedQuery()
    {
        _pollService.QueryParams = QueryParams;
    }

    /// <summary>
    /// Persists the chosen interests as each platform's own query string (built by the platform's
    /// descriptor — the shared code never names a facet key or concatenates a query, contract §8),
    /// then the completion marker, exactly once. An empty selection means all projects for every
    /// platform. Platform enable/disable was already persisted live through PlatformEnablement while
    /// the wizard was open; the pipeline seeds both values per platform when it starts after this.
    /// </summary>
    public bool Complete(IReadOnlyList<string> selectedInterests)
    {
        if (Interlocked.Exchange(ref _completionRecorded, 1) != 0 || IsCompleted)
        {
            return false;
        }

        foreach (var descriptor in _registry.All)
        {
            var query = OnboardingQueryComposer.BuildQueryFor(descriptor, selectedInterests);
            _settings.Set(descriptor.Id, SettingsKeys.QueryParams, query);
            if (descriptor.Id == _source)
            {
                _pollService.QueryParams = query;
            }
        }

        Preferences.Set(CompletionPreferenceKey, true);
        _settings.SetGlobal(SettingsKeys.IsPollingActive, true);
        Completed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}

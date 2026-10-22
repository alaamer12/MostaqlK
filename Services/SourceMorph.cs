using MostaqlK.Core.Sources;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.Services;

/// <summary>
/// Performs the permanent source morph — the single canonical triple: persist
/// <see cref="SettingsKeys.ActiveSource"/>, re-point the status projection (which re-targets every
/// live pipeline consumer resolved through <c>ActivePipeline</c>), then swap the brand palette
/// (DynamicResource repaint). Both callers share it: the rail tile tap (<c>SourceSwitchTiles</c>)
/// and the Windows toast-activation path, where clicking another source's project notification
/// permanently morphs to that source before navigating (contract §6, owner decision). Kept in
/// cross-platform code so every build compiles it, not only the Windows target.
/// </summary>
public static class SourceMorph
{
    public static void Apply(
        ISettingsRepository? settings,
        GlobalAppStatusService? globalStatus,
        IBrandTokenProvider? tokenProvider,
        SourceId source)
    {
        if (tokenProvider?.CurrentSource == source)
        {
            return;
        }

        settings?.SetGlobal(SettingsKeys.ActiveSource, source.Value);
        globalStatus?.SetActiveProjection(source);
        tokenProvider?.SwapTo(source);
    }
}

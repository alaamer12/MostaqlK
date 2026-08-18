namespace MostaqlK.Services;

/// <summary>
/// Platform-neutral boundary for revealing a local file in the OS file manager (Explorer on
/// Windows, folder open on other platforms). Resolved once via
/// <see cref="Core.Platform.PlatformCapability{T}"/> so call sites never hand-roll
/// <c>#if WINDOWS</c> / <c>explorer.exe</c> themselves.
/// </summary>
public interface IFileRevealService
{
    /// <summary>
    /// Reveals <paramref name="localPath"/> in the platform file manager. On Windows this opens
    /// Explorer with the file selected; elsewhere it opens the containing folder.
    /// </summary>
    Task RevealAsync(string localPath);
}

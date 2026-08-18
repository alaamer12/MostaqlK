using MostaqlK.Core.Platform;

namespace MostaqlK.Services;

/// <summary>
/// Static accessor for the platform's <see cref="IFileRevealService"/> implementation.
/// Every platform has a shape (Explorer select / open containing folder), so this is resolved
/// via <see cref="PlatformCapability{T}.Resolve"/> rather than <c>WindowsOnly</c>.
/// </summary>
public static class FileRevealService
{
    private static readonly Lazy<IFileRevealService> Instance = new(Create);

    /// <summary>The process-wide file-reveal implementation for the current platform.</summary>
    public static IFileRevealService Current => Instance.Value;

    private static IFileRevealService Create() =>
        PlatformCapability<IFileRevealService>.Resolve(
            windows: static () => new WindowsFileRevealService(),
            android: static () => new DefaultFileRevealService(),
            ios: static () => new DefaultFileRevealService(),
            macCatalyst: static () => new DefaultFileRevealService())
        ?? new DefaultFileRevealService();
}

/// <summary>
/// Cross-platform fallback: opens the containing folder via MAUI's <see cref="Launcher"/>.
/// Used on non-Windows targets (and as a last-resort default).
/// </summary>
internal sealed class DefaultFileRevealService : IFileRevealService
{
    public async Task RevealAsync(string localPath)
    {
        var folder = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(folder))
        {
            await Launcher.Default.OpenAsync($"file://{folder}");
        }
    }
}

/// <summary>
/// Windows implementation: opens Explorer with the file selected via <c>explorer.exe /select</c>.
/// Behavior is byte-identical to the former inline call in <c>AttachmentItemViewModel.RevealAsync</c>.
/// </summary>
internal sealed class WindowsFileRevealService : IFileRevealService
{
    public Task RevealAsync(string localPath)
    {
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{localPath}\"");
        return Task.CompletedTask;
    }
}

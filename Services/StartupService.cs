namespace MostaqlK.Services;

/// <summary>
/// Platform-neutral abstraction for "launch on startup" registration. The Windows
/// implementation writes and deletes an entry in
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>; all other platforms use
/// <see cref="NullStartupService"/>, which safely no-ops.
/// </summary>
public interface IStartupService
{
    /// <summary>
    /// Returns <c>true</c> if startup registration is supported on the current platform.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Returns <c>true</c> when the startup entry is currently registered in the OS.
    /// Reads from the OS state directly (not a cached preference) so it reflects any
    /// external edits (e.g. manual registry changes or an uninstaller).
    /// </summary>
    bool IsStartupEnabled { get; }

    /// <summary>
    /// Registers or unregisters the startup entry. Returns <c>true</c> on success and
    /// <c>false</c> if the OS operation failed (e.g. registry access denied), so the
    /// caller can revert the UI toggle rather than lying to the user.
    /// </summary>
    bool SetStartup(bool enable);

    /// <summary>
    /// Alias for <see cref="SetStartup"/>.
    /// </summary>
    bool SetStartupEnabled(bool enable);
}

/// <summary>
/// No-op implementation used on all non-Windows platforms (Android, iOS, macCatalyst).
/// Always reports the feature as disabled and off; <see cref="SetStartup"/> silently
/// returns <c>false</c> so callers know the operation was not performed.
/// </summary>
internal sealed class NullStartupService : IStartupService
{
    public bool IsSupported => false;
    public bool IsStartupEnabled => false;
    public bool SetStartup(bool enable) => false;
    public bool SetStartupEnabled(bool enable) => false;
}

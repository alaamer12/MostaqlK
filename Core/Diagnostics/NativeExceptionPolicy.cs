namespace MostaqlK.Core.Diagnostics;

/// <summary>
/// Native SEH / WinUI exception policy used by <c>CrashReporter</c>.
/// Isolated so crash-path tests can assert dump vs continue-search without Win32 filters.
/// </summary>
public static class NativeExceptionPolicy
{
    public const uint ClrManagedException = 0xE0434352;
    public const uint StatusStowedException = 0xC000027B;
    public const uint DxgiErrorDeviceRemoved = 0x887A0005;
    public const uint StatusStackBufferOverrun = 0xC0000409;
    public const uint StatusAccessViolation = 0xC0000005;

    /// <summary>
    /// CLR already reports managed exceptions. Writing a second minidump from the native
    /// filter races WinUI fail-fast and can abort a process that would otherwise continue.
    /// </summary>
    public static bool ShouldWriteMinidump(uint exceptionCode) =>
        exceptionCode != ClrManagedException;

    public static string NameFor(uint exceptionCode) => exceptionCode switch
    {
        StatusAccessViolation => "STATUS_ACCESS_VIOLATION (0xC0000005)",
        0xC00000FD => "STATUS_STACK_OVERFLOW (0xC00000FD)",
        0xC0000008 => "STATUS_INVALID_HANDLE (0xC0000008)",
        0xC000001D => "STATUS_ILLEGAL_INSTRUCTION (0xC000001D)",
        0xC0000025 => "STATUS_NONCONTINUABLE_EXCEPTION (0xC0000025)",
        StatusStackBufferOverrun => "STATUS_STACK_BUFFER_OVERRUN (0xC0000409)",
        StatusStowedException => "STATUS_STOWED_EXCEPTION (0xC000027B)",
        DxgiErrorDeviceRemoved => "DXGI_ERROR_DEVICE_REMOVED (0x887A0005)",
        ClrManagedException => "CLR_EXCEPTION (0xE0434352)",
        _ => $"0x{exceptionCode:X8}"
    };
}

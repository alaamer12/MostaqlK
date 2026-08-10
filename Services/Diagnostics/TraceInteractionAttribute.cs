namespace MostaqlK.Services.Diagnostics;

/// <summary>
/// Marks a command method/handler (e.g. <c>TogglePolling</c>, <c>RefreshCommand</c>,
/// <c>SaveCommand</c>) as one whose entry/exit/exceptions must be traceable via
/// <see cref="InteractionLogger"/>. This attribute is documentation-only (no IL weaving/interceptor
/// is wired up in this codebase) — the annotated method's body is expected to call
/// <see cref="InteractionLogger.Enter"/>/<see cref="InteractionLogger.Exit"/>/
/// <see cref="InteractionLogger.Fault"/> itself (or via the
/// <see cref="TraceScope"/> helper below) so both a human and an Appium test can verify the real
/// backend call happened, not just that a UI click was registered.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TraceInteractionAttribute(string name) : Attribute
{
    /// <summary>Stable interaction name used as the <see cref="InteractionLogger"/> checkpoint key.</summary>
    public string Name { get; } = name;
}

/// <summary>
/// Convenience <c>using</c>-scope that logs Enter on construction and Exit/Fault on disposal,
/// for methods annotated with <see cref="TraceInteractionAttribute"/>.
/// </summary>
/// <example>
/// <code>
/// [TraceInteraction("TogglePolling")]
/// private void TogglePolling()
/// {
///     using var _ = TraceScope.Begin("TogglePolling", new { IsPollingActive });
///     _pollService.TogglePause();
/// }
/// </code>
/// </example>
public sealed class TraceScope : IDisposable
{
    private readonly string _name;
    private bool _faulted;

    private TraceScope(string name) => _name = name;

    public static TraceScope Begin(string name, object? parameters = null)
    {
        InteractionLogger.Enter(name, parameters);
        return new TraceScope(name);
    }

    /// <summary>Record that the traced interaction failed; still disposes normally.</summary>
    public void MarkFaulted(Exception exception, object? data = null)
    {
        _faulted = true;
        InteractionLogger.Fault(_name, exception, data);
    }

    public void Dispose()
    {
        if (!_faulted)
        {
            InteractionLogger.Exit(_name);
        }
    }
}

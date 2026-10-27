namespace MostaqlK.Core.Layers;

/// <summary>
/// One implementation of a seam, registered under the full coordinates it applies to. Registrations
/// are explicit (no scanning) and always name every coordinate they bind, so a layer that leaks
/// into the wrong build stays inert — its key can never match that build's context.
/// </summary>
public sealed record LayerRegistration<T>(LayerKey Key, T Value);

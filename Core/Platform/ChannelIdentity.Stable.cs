namespace MostaqlK.Core.Platform;

/// <summary>Stable-channel fulfilment of the <see cref="ChannelIdentity"/> seams. Compiled only when the build's <c>Channel</c> is not <c>canary</c>.</summary>
public static partial class ChannelIdentity
{
    public const string Suffix = "";

    /// <summary>Existing stable CLSID — must never change or toast clicks break for installed users.</summary>
    public const string ToastActivatorClsid = "6A6D9142-4F91-4C6A-9C7B-6B6A6E5D9A11";

    public static readonly bool IsCanary = false;
}

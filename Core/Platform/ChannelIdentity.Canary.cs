namespace MostaqlK.Core.Platform;

/// <summary>Canary-channel fulfilment of the <see cref="ChannelIdentity"/> seams. Compiled only when the build is <c>-p:Channel=canary</c>.</summary>
public static partial class ChannelIdentity
{
    public const string Suffix = ".Canary";

    /// <summary>Distinct CLSID so each channel owns its own toast-activation COM server.</summary>
    public const string ToastActivatorClsid = "A1C0D5E2-7B34-4C8F-9E6A-2D7B4F1C8E30";

    public static readonly bool IsCanary = true;
}

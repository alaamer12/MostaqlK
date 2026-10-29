using MostaqlK.Core.Notifications;
using MostaqlK.Core.Platform;
using MostaqlK.Core.Sources;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// Resolves the branded imagery a toast for one source should carry, from the places only the running
/// app knows: the source registry, the channel's cache directory, and the app's content root.
/// <para>
/// The two Windows backends (classic WinRT and the Windows App SDK) both build the same toast XML, so
/// both ask this one resolver and hand the result to <see cref="ToastBranding"/>. That is what keeps
/// "a Nafezly toast" meaning the same thing whichever API the app happens to be using; the equality is
/// asserted in tools/ParserTests rather than assumed.
/// </para>
/// <para>
/// Every failure path returns the unbranded visual rather than throwing: branding is decoration, and a
/// decoration that cannot be resolved must not cost the user the notification.
/// </para>
/// </summary>
internal static class ToastBrandingResolver
{
    private static readonly string ContentRoot = AppDomain.CurrentDomain.BaseDirectory;

    private static readonly string AppLogoPath = Path.Combine(ContentRoot, "Resources", "Images", "logo.png");

    public static ToastBrandVisual For(SourceId source)
    {
        try
        {
            ISourceDescriptor? descriptor = null;
            var registry = IPlatformApplication.Current?.Services?.GetService<SourceRegistry>();
            if (registry is not null && registry.TryGet(source, out var found))
            {
                descriptor = found;
            }

            return ToastAssets.For(
                descriptor,
                AppPaths.CacheDirectory,
                ToastBranding.ToFileUri(AppLogoPath),
                // MAUI bakes each module's vector icon into {name}.scale-200.png at the content root,
                // which is where a running unpackaged app finds its own artwork.
                ContentRoot,
                AppLogoPath);
        }
        catch (Exception ex)
        {
            InteractionLogger.Fault("ToastBrandingResolver.For", ex, new { Source = source.Value });
            return ToastBrandVisual.Neutral(ToastBranding.ToFileUri(AppLogoPath));
        }
    }
}

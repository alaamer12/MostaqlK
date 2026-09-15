using System;
using System.Collections.Generic;
using System.IO;

namespace MostaqlK.Platforms.Windows;

/// <summary>
/// Keeps the .NET single-file bundle extraction directory intact for the lifetime of the process.
///
/// The apphost extracts the bundle to disk <em>before</em> the managed entry point runs, so a
/// portable single-exe build can never move <em>itself</em> out of
/// <c>%TEMP%\.net\MostaqlK\&lt;token&gt;\</c>: only <c>DOTNET_BUNDLE_EXTRACT_BASE_DIR</c> in the
/// <em>launcher's</em> environment can, and nothing guarantees that for a file the user
/// double-clicks or a <c>HKCU\...\Run</c> entry Explorer starts. Everything the app loads by
/// absolute path at runtime therefore lives in the one directory Windows temp maintenance exists
/// to clear — the <c>icon_*.png</c> files behind <c>MostaqlK.UI.PlatformComponents.AppIcon</c>,
/// the Tajawal <c>.ttf</c> faces, and WinUI's <c>.pri</c>/<c>.mui</c> resources.
///
/// Memory-mapped images are already immune: the OS refuses to delete a mapped file, which is why
/// the reported crash could still name <c>Microsoft.UI.Xaml.dll</c> by full path in the same folder
/// a <see cref="FileNotFoundException"/> had just been thrown from. Loose data files carry no such
/// protection, so they are the ones that can vanish under a long-running, tray-resident session and
/// only be missed when the window is re-shown and the visual tree renders again.
///
/// This class gives those loose files the same guarantee the loader gives the DLLs: it opens a
/// handle on every file in the extraction tree and holds it for the process lifetime.
/// <see cref="FileShare.Read"/> deliberately omits <see cref="FileShare.Delete"/>, so a later
/// <c>DeleteFile</c> from any cleanup tool fails with a sharing violation instead of removing a
/// file the running app still needs.
/// </summary>
internal static class BundleExtractionGuard
{
    /// <summary>
    /// Upper bound on held handles, so an unexpectedly deep publish layout cannot grow without
    /// limit. A self-contained single-file build of this app is a few hundred files, well inside it.
    /// </summary>
    private const int MaxPinnedFiles = 8192;

    // Held until the process exits on purpose: never disposed, never cleared.
    private static readonly List<FileStream> Pins = new();

    private static readonly EnumerationOptions WalkOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>
    /// Opens and holds a share-read handle on every file under <paramref name="directory"/>.
    /// Returns how many files were pinned. Best-effort and never throws: a file that cannot be
    /// opened is simply not pinned, which is the state the process was already in.
    /// </summary>
    public static int PinDirectory(string directory)
    {
        try
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return 0;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*", WalkOptions))
            {
                if (Pins.Count >= MaxPinnedFiles)
                {
                    break;
                }

                try
                {
                    Pins.Add(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read));
                }
                catch
                {
                    // Already open with incompatible sharing, or removed mid-enumeration.
                }
            }
        }
        catch
        {
            // Startup must never fail because a defensive measure did.
        }

        return Pins.Count;
    }
}

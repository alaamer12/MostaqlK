using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using MostaqlK.Core.Notifications;
using MostaqlK.Core.Sources;
using MostaqlK.Sources.Mostaql;
using MostaqlK.Sources.Nafezly;

namespace ToastLab;

/// <summary>
/// Fires a real Windows toast from the same code the app uses, without launching the app.
/// <para>
/// Why this exists: verifying a notification's appearance through the product means waiting for a
/// scan to discover a new project while the app sits hidden in the tray — minutes per attempt, and
/// usually a silent failure if the identity is not right. Here the same seam
/// (<see cref="ToastAssets"/> + <see cref="ToastBranding"/>) and the same descriptors produce the
/// markup, and it is on screen in a second.
/// </para>
/// <para>
/// It registers its own identity (<c>ToastLab.App</c>) and never touches MostaqlK's. That is not
/// caution for its own sake: an unpackaged app's toast identity is a Start Menu shortcut, and a
/// harness that borrowed the app's AUMID would repoint the user's shortcut at the harness exe and
/// silently break the app's real notifications — which is precisely the failure a smoke test of the
/// published layouts caused today.
/// </para>
/// </summary>
internal static class Program
{
    private const string LabAumid = "ToastLab.App";

    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options.Help)
        {
            Console.WriteLine(Options.Usage);
            return 0;
        }

        var repoRoot = FindRepoRoot();
        var appLogo = repoRoot is null
            ? null
            : ToastBranding.ToFileUri(Path.Combine(repoRoot, "Resources", "Images", "logo.png"));
        // The tile art the modules ship is baked into the app's build output, which is exactly the
        // place a dev run would find it. Missing is a supported answer, not an error.
        var glyphDirectory = repoRoot is null
            ? null
            : Path.Combine(repoRoot, "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "Resources", "Images");

        var outcomes = new List<string>();
        var dumpTargets = new List<string>();
        var runs = options.Smoke ? SmokeMatrix() : [new Run(options.Source, options.Theme, options.Neutral, options.NoHero, options.Grouped, options.Title, options.Body)];

        if (!EnsureLabIdentity())
        {
            Console.WriteLine("identity registration FAILED — Windows will drop every toast silently. "
                + "Nothing shown below proves delivery.");
        }

        foreach (var run in runs)
        {
            var label = $"{run.Source ?? "(neutral)"} {run.Theme} {(run.Grouped > 1 ? $"x{run.Grouped}" : "single")}";
            try
            {
                ISourceDescriptor? descriptor = run.Neutral || run.Source is null
                    ? null
                    : Descriptors.FirstOrDefault(d => string.Equals(d.Id.Value, run.Source, StringComparison.OrdinalIgnoreCase));

                if (!run.Neutral && run.Source is not null && descriptor is null)
                {
                    outcomes.Add($"SKIP  {label} — no such registered source");
                    continue;
                }

                var visual = ToastAssets.For(
                    descriptor,
                    run.Theme,
                    options.OutDir,
                    appLogo,
                    glyphDirectory);

                if (run.NoHero)
                {
                    visual = visual with { HeroFileUri = null };
                }

                var count = Math.Max(1, run.Grouped);
                var title = count > 1
                    ? $"يوجد {count} مشاريع جديدة — تفقدها هنا"
                    : run.Title;
                var body = count > 1 ? string.Empty : run.Body;
                var caption = descriptor is null ? "ToastLab" : descriptor.DisplayNameAr;

                var xml = ToastBranding.ApplyToBindingXml(BuildTemplateXml(title, body, caption, appLogo), visual);

                if (options.DumpXml is { } dumpPath)
                {
                    // One file per send: overwriting a single path meant the only payload left on
                    // disk was the last case — the --no-hero one — which is the least interesting.
                    var suffix = runs.Count == 1 ? string.Empty : "-" + (outcomes.Count + 1).ToString("D2", CultureInfo.InvariantCulture);
                    var target = Path.Combine(
                        Path.GetDirectoryName(dumpPath) ?? ".",
                        Path.GetFileNameWithoutExtension(dumpPath) + suffix + Path.GetExtension(dumpPath));
                    File.WriteAllText(target, xml, new UTF8Encoding(false));
                    dumpTargets.Add(target);
                }

                var document = new XmlDocument();
                document.LoadXml(xml);
                var notifier = ToastNotificationManager.CreateToastNotifier(LabAumid);
                var toast = new ToastNotification(document) { Tag = "toastlab", Group = "toastlab" };
                notifier.Show(toast);

                outcomes.Add($"SENT  {label}"
                    + $" | hero={(visual.HeroFileUri is null ? "none" : Path.GetFileName(new Uri(visual.HeroFileUri).LocalPath))}"
                    + $" | logo={(visual.LogoFileUri is null ? "none" : Path.GetFileName(new Uri(visual.LogoFileUri).LocalPath))}"
                    + (dumpTargets.Count > 0 ? $" | xml={dumpTargets[^1]}" : string.Empty));
            }
            catch (Exception ex)
            {
                outcomes.Add($"ERROR {label} — {ex.GetType().Name}: {ex.Message}");
            }
        }

        foreach (var line in outcomes)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"cache dir: {options.OutDir}");
        Console.WriteLine($"identity : {LabAumid} + {ShortcutPath()}");
        Console.WriteLine("Windows reports nothing about whether a toast was actually displayed; if nothing "
            + "appears, check Settings > Notifications for 'ToastLab' and the Action Center.");

        // A process that exits instantly can lose a toast that has not been handed over yet.
        Thread.Sleep(options.WaitMilliseconds);
        return outcomes.Any(o => o.StartsWith("ERROR", StringComparison.Ordinal)) ? 1 : 0;
    }

    private static readonly ISourceDescriptor[] Descriptors = [MostaqlSource.Instance, NafezlySource.Instance];

    private static List<Run> SmokeMatrix() =>
    [
        new(MostaqlSource.Instance.Id.Value, ToastVisualTheme.Light, false, false, 1,
            "مشروع جديد: تصميم واجهة مستخدم", "وصف مختصر لعرضه في الإشعار"),
        new(NafezlySource.Instance.Id.Value, ToastVisualTheme.Light, false, false, 1,
            "مشروع جديد: تحليل بيانات مبيعات", "وصف مختصر لعرضه في الإشعار"),
        new(MostaqlSource.Instance.Id.Value, ToastVisualTheme.Dark, false, false, 1,
            "مشروع جديد (داكن)", "نفس النص على اللوحة الداكنة"),
        new(NafezlySource.Instance.Id.Value, ToastVisualTheme.Dark, false, false, 3, string.Empty, string.Empty),
        new(null, ToastVisualTheme.Light, true, false, 1, "إشعار محايد", "الشعار العام بدون لوحة ألوان"),
        new(MostaqlSource.Instance.Id.Value, ToastVisualTheme.Light, false, true, 1, "بدون لوحة اللون", "الشعار فقط"),
    ];

    private static string BuildTemplateXml(string title, string body, string caption, string? baseLogoFileUri)
    {
        var logo = baseLogoFileUri is null
            ? string.Empty
            : $"<image placement='appLogoOverride' src='{baseLogoFileUri}' />";

        var text = new StringBuilder();
        text.Append("<text>").Append(ToastBranding.EscapeXmlText(title)).Append("</text>");
        if (!string.IsNullOrWhiteSpace(body))
        {
            text.Append("<text>").Append(ToastBranding.EscapeXmlText(body)).Append("</text>");
        }

        return "<toast lang='ar-SA' activationType='protocol' launch='toastlab=1'>"
            + "<visual lang='ar-SA'>"
            + $"<binding template='ToastGeneric'>{logo}{text}</binding>"
            + "</visual>"
            + $"<actions><action activationType='protocol' content='عرض على {ToastBranding.EscapeXmlText(caption)}' arguments='openUrl=https://example.com' /></actions>"
            + "</toast>";
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MostaqlK.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// Sets this process's AUMID and makes sure a Start Menu shortcut carrying the same AUMID points
    /// at this exe. Without it Windows drops the toast and says nothing at all.
    /// </summary>
    private static bool EnsureLabIdentity()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(LabAumid);

            var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exe))
            {
                return false;
            }

            var shortcut = ShortcutPath();
            Directory.CreateDirectory(Path.GetDirectoryName(shortcut)!);

            var link = (IShellLinkW)new CShellLink();
            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
            link.SetDescription("ToastLab — notification preview harness");

            var store = (IPropertyStore)link;
            var value = new PropVariant { vt = VT_LPWSTR, pwszVal = Marshal.StringToCoTaskMemUni(LabAumid) };
            store.SetValue(ref PkeyAppUserModelId, ref value);
            store.Commit();
            Marshal.FreeCoTaskMem(value.pwszVal);

            ((IPersistFile)link).Save(shortcut, true);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"identity error: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static string ShortcutPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ToastLab.lnk");

    private static PropertyKey PkeyAppUserModelId = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    private const short VT_LPWSTR = 31;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public int pid;

        public PropertyKey(Guid formatId, int propertyId)
        {
            fmtid = formatId;
            pid = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public short vt;
        [FieldOffset(8)] public IntPtr pwszVal;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink
    {
    }

    // The full method list, in the real vtable order. COM dispatches by slot, not by name: an
    // "abbreviated" interface that declares only the three methods this file uses makes SetPath
    // execute GetPath's slot, which is exactly the access violation the first run of this harness
    // died on. Slots the harness does not call are declared as placeholders for that reason.
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        public void GetPath(StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        public void GetIDList(out IntPtr ppidl);
        public void SetIDList(IntPtr pidl);
        public void GetDescription(StringBuilder pszName, int cchMaxName);
        public void SetDescription(string pszName);
        public void GetWorkingDirectory(StringBuilder pszDir, int cchMaxPath);
        public void SetWorkingDirectory(string pszDir);
        public void GetArguments(StringBuilder pszArgs, int cchMaxPath);
        public void SetArguments(string pszArgs);
        public void GetHotkey(out short pwHotkey);
        public void SetHotkey(short wHotkey);
        public void GetShowCmd(out int piShowCmd);
        public void SetShowCmd(int iShowCmd);
        public void GetIconLocation(StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        public void SetIconLocation(string pszIconPath, int iIcon);
        public void SetRelativePath(string pszPathRel, uint dwReserved);
        public void Resolve(IntPtr hwnd, uint fFlags);
        public void SetPath(string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    private interface IPropertyStore
    {
        public void GetCount(out uint cProps);
        public void GetAt(uint iProp, out PropertyKey pkey);
        public void GetValue(ref PropertyKey key, out PropVariant pv);
        public void SetValue(ref PropertyKey key, ref PropVariant pv);
        public void Commit();
    }

    private sealed record Run(
        string? Source,
        ToastVisualTheme Theme,
        bool Neutral,
        bool NoHero,
        int Grouped,
        string Title,
        string Body);

    private sealed record Options(
        string? Source,
        ToastVisualTheme Theme,
        bool Neutral,
        bool NoHero,
        int Grouped,
        string Title,
        string Body,
        string OutDir,
        string? DumpXml,
        bool Smoke,
        int WaitMilliseconds,
        bool Help)
    {
        public const string Usage =
            "ToastLab — fire a real Windows toast through the app's own branding code.\n"
            + "  --source <id>        registered source id (see --smoke for both)\n"
            + "  --theme light|dark   palette selection\n"
            + "  --neutral            no source branding at all (app logo only)\n"
            + "  --no-hero            keep the source logo but drop the color band\n"
            + "  --grouped N          render the multi-project (grouped) toast\n"
            + "  --title TEXT --body TEXT\n"
            + "  --out-dir PATH       where generated brand PNGs go (default: %TEMP%\\mostaqlk-toastlab)\n"
            + "  --dump-xml PATH      write the exact payload sent\n"
            + "  --smoke              both sources x both themes x individual/grouped/neutral\n"
            + "  --wait MS            stay alive long enough for Windows to take the toast\n";

        public static Options Parse(string[] args)
        {
            string? source = null;
            string? title = null;
            string? body = null;
            var theme = ToastVisualTheme.Light;
            var neutral = false;
            var noHero = false;
            var grouped = 1;
            string? dumpXml = null;
            var smoke = false;
            var wait = 2500;
            var help = false;
            var outDir = Path.Combine(Path.GetTempPath(), "mostaqlk-toastlab");

            for (var i = 0; i < args.Length; i++)
            {
                string? Next() => i + 1 < args.Length ? args[++i] : null;

                switch (args[i].ToLowerInvariant())
                {
                    case "--source": source = Next(); break;
                    case "--title": title = Next(); break;
                    case "--body": body = Next(); break;
                    case "--theme": theme = string.Equals(Next(), "dark", StringComparison.OrdinalIgnoreCase) ? ToastVisualTheme.Dark : ToastVisualTheme.Light; break;
                    case "--neutral": neutral = true; break;
                    case "--no-hero": noHero = true; break;
                    case "--grouped": { if (int.TryParse(Next(), out var g)) { grouped = g; } break; }
                    case "--out-dir": outDir = Next() ?? outDir; break;
                    case "--dump-xml": dumpXml = Next(); break;
                    case "--smoke": smoke = true; break;
                    case "--wait": { if (int.TryParse(Next(), out var w)) { wait = w; } break; }
                    case "--help": case "-h": help = true; break;
                }
            }

            return new Options(source, theme, neutral, noHero, Math.Max(1, grouped),
                title ?? "مشروع جديد من مستقل",
                body ?? "تم اكتشاف مشروع مطابق للفلاتر المحفوظة — اضغط لفتحه.",
                outDir, dumpXml, smoke, wait, help);
        }
    }
}

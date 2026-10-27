using MostaqlK.Core.Sources;
using MostaqlK.Infrastructure.Http;

namespace MostaqlK.Features.Settings;

/// <summary>
/// The one place a settings value is checked before it is persisted. Every rule is read off the
/// <see cref="SettingsDescriptor"/> — bounds, validation kind and the Arabic message template —
/// so a row's constraints live in exactly one file and a source that overrides `Min` also changes
/// what is accepted, without a code path knowing about it.
/// <para>
/// This replaces the per-property range checks the settings view-model used to hand-write (four
/// copies of "if (value &lt; min || value &gt; max)" with four different literals and four
/// different messages, which is how the screen ended up advertising bounds the services never
/// enforced).
/// </para>
/// </summary>
public static class SettingsValidator
{
    /// <summary>
    /// <c>null</c> when <paramref name="value"/> satisfies the descriptor's rule; the Arabic
    /// message to show otherwise. <c>{0}</c>=min, <c>{1}</c>=max, <c>{2}</c>=offending value.
    /// </summary>
    public static string? Validate(SettingsDescriptor descriptor, object? value) => descriptor.Validation switch
    {
        SettingsValidationKind.None => null,
        SettingsValidationKind.IntRange => IntRange(descriptor, value, descriptor.Min ?? int.MinValue, descriptor.Max ?? int.MaxValue),
        SettingsValidationKind.IntMin => IntRange(descriptor, value, descriptor.Min ?? 1, int.MaxValue),
        SettingsValidationKind.EnumIndex => EnumIndex(descriptor, value),
        SettingsValidationKind.CookieParse => CookieShape(descriptor, value),
        _ => throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.Validation,
            $"Unhandled {nameof(SettingsValidationKind)} — every kind the catalog can declare must be checked here."),
    };

    private static string? IntRange(SettingsDescriptor d, object? value, int min, int max)
    {
        var parsed = value switch
        {
            int i => i,
            string s when int.TryParse(s, out var fromText) => fromText,
            // An empty or non-numeric field is not "no change": the row must say why it refused it.
            _ => (int?)null,
        };

        if (parsed is null || parsed < min || parsed > max)
        {
            return Message(d, parsed, min, max);
        }

        return null;
    }

    private static string? EnumIndex(SettingsDescriptor d, object? value)
    {
        var text = value?.ToString();
        var options = d.Options ?? [];
        return options.Any(o => o.Value == text) ? null : Message(d, null, 0, options.Count - 1);
    }

    private static string? CookieShape(SettingsDescriptor d, object? value)
    {
        var raw = value?.ToString();
        return string.IsNullOrWhiteSpace(raw) || CookieParser.ParseFile(raw) is not null
            ? null
            : Message(d, null, 0, 0);
    }

    private static string Message(SettingsDescriptor d, int? value, int min, int max) =>
        (d.ErrorAr ?? "قيمة غير صالحة.")
            .Replace("{0}", min.ToString())
            .Replace("{1}", max.ToString())
            .Replace("{2}", value?.ToString() ?? "");
}

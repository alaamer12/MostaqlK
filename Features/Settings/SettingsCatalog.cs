using MostaqlK.Core.Layers;
using MostaqlK.Core.Sources;

namespace MostaqlK.Features.Settings;

/// <summary>The composed settings rows for one source, plus overrides that named a missing key.</summary>
public sealed record ComposedSettings(
    IReadOnlyList<SettingsDescriptor> Descriptors,
    IReadOnlyList<string> IgnoredOverrides);

/// <summary>
/// Folds the settings layers into the effective row list for the active source:
/// base built-ins at <c>{}</c>, source adds/overrides at <c>{source: X}</c>, tie-breakers at
/// multi-coordinate keys. Patches merge field-wise; a full descriptor for an existing key
/// replaces it. Two same-specificity layers touching one field fail loudly (never resolved by
/// registration order), and an override for a removed key is dropped — so deleting a descriptor
/// line from the catalog can never crash the next run.
/// </summary>
public static partial class SettingsCatalog
{
    public static ComposedSettings Compose(
        ISourceDescriptor source,
        IReadOnlyList<ISourceSettingsContribution> contributions,
        IReadOnlyList<SettingsDescriptor>? baseLayer = null)
    {
        var ctx = LayerContext.For(source.Id.Value);
        var rows = new Dictionary<string, List<LayerRegistration<SettingsDescriptor>>>();

        void Register(LayerKey key, SettingsDescriptor d)
        {
            if (!rows.TryGetValue(d.Key, out var list))
            {
                list = [];
                rows[d.Key] = list;
            }

            list.Add(new LayerRegistration<SettingsDescriptor>(key, d));
        }

        foreach (var d in baseLayer ?? Builtin)
        {
            Register(LayerKey.Base, d);
        }

        var patches = new List<LayerRegistration<SettingsDescriptorPatch>>();
        foreach (var c in contributions)
        {
            var key = LayerKey.For(LayerAxis.Source, c.Source.Value);
            foreach (var d in c.Adds)
            {
                Register(key, d);
            }

            foreach (var p in c.Overrides)
            {
                patches.Add(new LayerRegistration<SettingsDescriptorPatch>(key, p));
            }
        }

        // Overrides aimed at keys that no longer exist are dropped, not fatal: removing a
        // descriptor must not break the source that once customized it.
        var ignored = patches.Where(p => !rows.ContainsKey(p.Value.Key)).Select(p => p.Value.Key).Distinct().ToList();

        var result = new List<SettingsDescriptor>();
        foreach (var (key, registrations) in rows)
        {
            var seam = $"settings:{key}";
            var chain = LayerResolver.ResolveChain(seam, registrations, ctx);
            var descriptor = chain[0].Value;
            var fieldOwners = new Dictionary<string, (int Specificity, LayerKey Key)>();

            foreach (var layer in chain.Skip(1))
            {
                descriptor = layer.Value; // full replace wins outright
                fieldOwners.Clear();
            }

            foreach (var reg in patches.Where(p => p.Value.Key == key).OrderBy(p => p.Key.Specificity))
            {
                foreach (var field in reg.Value.SetFields())
                {
                    if (fieldOwners.TryGetValue(field, out var owner) && owner.Specificity == reg.Key.Specificity)
                    {
                        throw new AmbiguousLayerException($"{seam}.{field}", [owner.Key, reg.Key]);
                    }

                    fieldOwners[field] = (reg.Key.Specificity, reg.Key);
                }

                descriptor = reg.Value.ApplyTo(descriptor);
            }

            if (descriptor.Visible && EvaluateFlag(descriptor.VisibilityFlag, source))
            {
                result.Add(descriptor);
            }
        }

        return new ComposedSettings(
            result.OrderBy(d => d.Section, StringComparer.Ordinal).ThenBy(d => d.Order).ToList(),
            ignored);
    }

    /// <summary>Named visibility conditions evaluated against the live source descriptor.</summary>
    private static bool EvaluateFlag(string flag, ISourceDescriptor source) => flag switch
    {
        "" => true,
        "NeedsSessionForFullEnrichment" => source.Capabilities.NeedsSessionForFullEnrichment,
        "HasSupervisedFlag" => source.Capabilities.HasSupervisedFlag,
        "HasCurrencySuffix" => !string.IsNullOrEmpty(source.CurrencySuffix),
        "Never" => false,
        // An unknown flag stays visible: a typo hides nothing and fails loudly in review,
        // while silently hiding a row would look like a data-loss bug to users.
        _ => true,
    };
}

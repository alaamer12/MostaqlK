using System.Text.Json;

namespace MostaqlK.Services.Diagnostics;

/// <summary>
/// FULL-LIFECYCLE TRACE for the projects feed page (dev80 hunt).
/// <para>
/// The targeted diagnostics of dev74-dev79 each answered one question — did the data collapse, how
/// many containers exist, where are they laid out — and every one of them came back "healthy" while
/// the owner kept watching cards vanish and change identity under the scroll. The gap was coverage:
/// nobody was recording WHICH ROW MOVED TO WHICH INDEX, what the item containers were bound to over
/// time, or the scroll history. This type records the whole page lifecycle as one ordered narrative:
/// a monotonic <see cref="Next"/> sequence on every line, so a log read reconstructs what happened in
/// what order, across the page, the layout, the collection, the cards, the navigation and the store.
/// </para>
/// <para>
/// ON BY DEFAULT in the hunt build, because the owner must be able to launch normally and produce
/// evidence with no setup; <c>MOSTAQLK_TRACE_FEED=0</c> silences it. It is loud on purpose and MUST
/// be removed (or flipped to opt-in) before this ships — see <c>.iwts/state.md</c>.
/// </para>
/// </summary>
public static class FeedTrace
{
    private static int _seq;

    /// <summary>Mono counter shared by every trace line in the process, so interleaving is readable.</summary>
    public static int Next => System.Threading.Interlocked.Increment(ref _seq);

    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable("MOSTAQLK_TRACE_FEED") != "0";

    /// <summary>
    /// One trace line: <c>FeedTrace.&lt;area&gt;</c> with a variant that names the moment. Keep the
    /// payload small and comparable — ids, indexes, counts, offsets — never titles or user content.
    /// </summary>
    public static void Log(string area, string moment, object? data = null)
    {
        if (!Enabled)
        {
            return;
        }

        var envelope = new Dictionary<string, object?> { ["seq"] = Next };
        if (data is not null)
        {
            foreach (var pair in Flatten(data))
            {
                envelope[pair.Key] = pair.Value;
            }
        }

        InteractionLogger.Mark($"FeedTrace.{area}", moment, envelope);
    }

    private static IEnumerable<KeyValuePair<string, object?>> Flatten(object data)
    {
        // Anonymous objects are the call-site shape; serialize + re-read keeps this one code path
        // instead of reflection per field, and guarantees the same JSON the log already uses.
        try
        {
            var json = JsonSerializer.SerializeToElement(data);
            var pairs = new List<KeyValuePair<string, object?>>();
            foreach (var prop in json.EnumerateObject())
            {
                pairs.Add(new KeyValuePair<string, object?>(prop.Name, prop.Value.ValueKind == JsonValueKind.Undefined ? null : prop.Value.ToString()));
            }

            return pairs;
        }
        catch
        {
            return [new KeyValuePair<string, object?>("payload", data.ToString())];
        }
    }
}

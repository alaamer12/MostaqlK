namespace MostaqlK.Core.Diagnostics;

/// <summary>
/// Always-on detection of a REPEATING throw, without a first-chance log line. Isolated (no SDK,
/// no IO) so CrashPathTests can prove the arithmetic.
/// <para>
/// Why this exists: the dev69 fault was thrown, swallowed, and invisible for nine days because
/// the only probe that sees swallowed throws is an opt-in flag nobody runs day to day. Every
/// audited <c>catch</c> now logs through the funnel — but an unaudited or future catch is exactly
/// where the next IterationCount lives. A throw that repeats dozens of times in a minute is not
/// normal framework noise; it is a loop feeding on a caught failure, and the loop is the crash
/// fuel. So the cheap part (counting) is always on; the loud part (a promoted FAULT through the
/// funnel) fires once per offender.
/// </para>
/// <para>
/// Cost discipline: the handler must be microseconds — type name + trimmed message + a dictionary
/// tick, no stack capture, no allocation beyond the key string, and a hard key cap so a pathological
/// process cannot grow it without bound. The full first-chance LOG stays opt-in
/// (<c>MOSTAQLK_DIAG_COM=1</c>); this detector never writes a line, it only promotes.
/// </para>
/// </summary>
public static class FirstChanceStormPolicy
{
    /// <summary>Repeats of one (type, message-prefix) key inside <see cref="Window"/> that mark it a storm.</summary>
    public const int StormCountThreshold = 25;

    /// <summary>Sliding window a storm must stay inside before promotion; resets on promotion.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    /// <summary>Distinct keys tracked per process; beyond it new keys are ignored (the cap is the point — bounded memory).</summary>
    public const int MaxTrackedKeys = 512;

    /// <summary>How much of the message joins the type as the key — enough to group, short enough to cap cost.</summary>
    public const int MessageKeyPrefix = 120;

    private sealed class KeyState
    {
        public int Count;
        public DateTime WindowStartUtc;
        public bool Promoted;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, KeyState> Keys = new(StringComparer.Ordinal);

    /// <summary>Builds the tracking key for a throw. Trims, never throws.</summary>
    public static string KeyFor(Exception exception)
    {
        var type = exception.GetType().FullName ?? exception.GetType().Name;
        var message = exception.Message;
        if (message.Length > MessageKeyPrefix)
        {
            message = message[..MessageKeyPrefix];
        }

        return type + "|" + message;
    }

    /// <summary>
    /// Records one throw and reports whether THIS call is the moment to promote it: true exactly
    /// once per key per storm episode (when the count crosses the threshold), and the key may
    /// storm again after a fresh window + fresh threshold.
    /// </summary>
    public static bool NoteThrow(string key, DateTime utcNow)
    {
        lock (Gate)
        {
            if (!Keys.TryGetValue(key, out var state))
            {
                if (Keys.Count >= MaxTrackedKeys)
                {
                    return false; // capped: a pathological process is not given unbounded memory
                }

                state = new KeyState { WindowStartUtc = utcNow };
                Keys[key] = state;
            }

            if (utcNow - state.WindowStartUtc > Window)
            {
                state.WindowStartUtc = utcNow;
                state.Count = 0;
                state.Promoted = false;
            }

            state.Count++;
            if (state.Count >= StormCountThreshold && !state.Promoted)
            {
                state.Promoted = true;
                return true;
            }

            return false;
        }
    }

    /// <summary>Test isolation only.</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            Keys.Clear();
        }
    }
}

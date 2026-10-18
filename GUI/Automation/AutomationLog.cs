#if DEBUG
using System.Globalization;
using System.Threading;
using GUI.Utils;

namespace GUI.Automation;

/// <summary>
/// Every log line with its severity, numbered so that a caller can read only the lines after a
/// point, and filter by level. The console tab keeps only text.
/// </summary>
internal static class AutomationLog
{
    public readonly record struct Entry(DateTime Time, Log.Category Category, string Component, string Message)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Time:HH:mm:ss.fff} {"DIWE"[(int)Category]} [{Component}] {Message}");
    }

    private const int Capacity = 20_000;

    private static readonly Lock Sync = new();

    // A ring of the newest lines, where the line with sequence number n is at (n - 1) % Capacity
    private static readonly Entry[] Entries = new Entry[Capacity];
    private static long LastSequence;

    /// <summary>The sequence number of the newest line.</summary>
    public static long Cursor => Interlocked.Read(ref LastSequence);

    /// <summary>Called from whichever thread logged the line.</summary>
    public static void Add(Log.Category category, string component, string message)
    {
        if (!Automation.IsEnabled)
        {
            return;
        }

        var time = DateTime.Now;

        using var _ = Sync.EnterScope();

        Entries[LastSequence % Capacity] = new Entry(time, category, component, message);
        Interlocked.Increment(ref LastSequence);
    }

    /// <summary>
    /// The lines after <paramref name="since"/>, oldest first, and the cursor for a later read. Also
    /// how many lines after <paramref name="since"/> were logged but are no longer kept.
    /// </summary>
    public static (List<Entry> Entries, long Cursor, long Dropped) Since(long since)
    {
        using var _ = Sync.EnterScope();

        // Sequence numbers are contiguous, so the newer lines are the newest ones kept
        var kept = Math.Min(LastSequence, Capacity);
        var logged = Math.Clamp(LastSequence - since, 0, LastSequence);
        var newer = Math.Min(logged, kept);
        var lines = new List<Entry>((int)newer);

        for (var sequence = LastSequence - newer; sequence < LastSequence; sequence++)
        {
            lines.Add(Entries[sequence % Capacity]);
        }

        return (lines, LastSequence, logged - newer);
    }
}
#endif

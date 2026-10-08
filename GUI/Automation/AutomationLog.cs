#if DEBUG
using System.Linq;
using System.Threading;
using GUI.Utils;

namespace GUI.Automation;

/// <summary>
/// Every log line with its severity, counted so that a caller can read only what was logged after
/// a point, and filter by level. The console tab itself keeps nothing but text.
/// </summary>
internal static class AutomationLog
{
    public readonly record struct Entry(DateTime Time, Log.Category Category, string Component, string Message)
    {
        public override string ToString() => $"{Time:HH:mm:ss.fff} {"DIWE"[(int)Category]} [{Component}] {Message}";
    }

    private const int Capacity = 20_000;

    private static readonly Lock Sync = new();
    private static readonly Queue<Entry> Entries = new();
    private static long lastSequence;

    /// <summary>The sequence number of the newest line.</summary>
    public static long Cursor => Interlocked.Read(ref lastSequence);

    /// <summary>Called from whichever thread logged the line.</summary>
    public static void Add(Log.Category category, string component, string message)
    {
        if (!Automation.IsEnabled)
        {
            return;
        }

        var time = DateTime.Now;

        using var _ = Sync.EnterScope();

        if (Entries.Count == Capacity)
        {
            Entries.Dequeue();
        }

        Entries.Enqueue(new Entry(time, category, component, message));
        Interlocked.Increment(ref lastSequence);
    }

    /// <summary>The lines after <paramref name="since"/>, oldest first, and the cursor a later read continues from.</summary>
    public static (List<Entry> Entries, long Cursor) Since(long since)
    {
        using var _ = Sync.EnterScope();

        // Sequence numbers are contiguous, so the newer lines are the tail
        var newer = (int)Math.Min(Entries.Count, Math.Max(0, lastSequence - since));

        return ([.. Entries.Skip(Entries.Count - newer)], lastSequence);
    }
}
#endif

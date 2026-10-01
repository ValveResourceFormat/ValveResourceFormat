#if DEBUG
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using GUI.Utils;

namespace GUI.Automation;

/// <summary>
/// Every console line with its severity and a sequence number, so a caller can read only what was
/// logged after a point and filter by level. The console tab itself keeps nothing but text.
/// </summary>
internal static class AutomationLog
{
    public readonly record struct Entry(long Sequence, DateTime Time, Log.Category Category, string Component, string Message);

    private const int Capacity = 20_000;

    private static readonly Lock Sync = new();
    private static readonly Queue<Entry> Entries = new();
    private static long lastSequence;

    /// <summary>The sequence number of the newest line, which a later read can pass as its cursor.</summary>
    public static long Cursor
    {
        get
        {
            using var _ = Sync.EnterScope();
            return lastSequence;
        }
    }

    /// <summary>Called from whichever thread logged the line.</summary>
    public static void Add(Log.Category category, string component, string message)
    {
        var time = DateTime.Now;

        using var _ = Sync.EnterScope();

        if (Entries.Count == Capacity)
        {
            Entries.Dequeue();
        }

        Entries.Enqueue(new Entry(++lastSequence, time, category, component, message));
    }

    /// <summary>Drops every line. Sequence numbers keep counting, so an earlier cursor stays valid.</summary>
    public static long Clear()
    {
        using var _ = Sync.EnterScope();

        Entries.Clear();
        return lastSequence;
    }

    /// <summary>
    /// Lines after <paramref name="since"/> at or above <paramref name="level"/> that pass both
    /// patterns, oldest first, and the cursor that a later read continues from.
    /// </summary>
    public static (List<Entry> Entries, long Cursor) Read(long since, Log.Category level, Regex? include, Regex? exclude)
    {
        Entry[] snapshot;
        long cursor;

        using (Sync.EnterScope())
        {
            snapshot = [.. Entries];
            cursor = lastSequence;
        }

        var matches = new List<Entry>();

        foreach (var entry in snapshot)
        {
            if (entry.Sequence <= since || entry.Category < level)
            {
                continue;
            }

            var text = $"[{entry.Component}] {entry.Message}";

            if ((include != null && !include.IsMatch(text)) || (exclude != null && exclude.IsMatch(text)))
            {
                continue;
            }

            matches.Add(entry);
        }

        return (matches, cursor);
    }

    /// <summary>One line of text: time, a single letter for the level, component and message.</summary>
    public static string Format(Entry entry, int repeats)
    {
        var line = new StringBuilder();

        line.Append(entry.Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
        line.Append(' ');
        line.Append(entry.Category switch
        {
            Log.Category.DEBUG => 'D',
            Log.Category.WARN => 'W',
            Log.Category.ERROR => 'E',
            _ => 'I',
        });
        line.Append(CultureInfo.InvariantCulture, $" [{entry.Component}] {entry.Message}");

        if (repeats > 1)
        {
            line.Append(CultureInfo.InvariantCulture, $" (x{repeats})");
        }

        return line.ToString();
    }
}
#endif

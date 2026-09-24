#if DEBUG
using System.Threading.Tasks;

namespace GUI.Automation;

/// <summary>
/// Unhandled exceptions raised while an agent drives the viewer. They replace the error dialog, so
/// a tool waiting on a load or a frame can fail with the exception instead of timing out.
/// </summary>
internal static class UnhandledExceptions
{
    private static readonly object Sync = new();
    private static int count;
    private static string? latest;
    private static TaskCompletionSource<string> next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static int Count
    {
        get
        {
            lock (Sync)
            {
                return count;
            }
        }
    }

    public static string? Latest
    {
        get
        {
            lock (Sync)
            {
                return latest;
            }
        }
    }

    public static void Report(Exception exception)
    {
        var text = exception.ToString();
        TaskCompletionSource<string> signal;

        lock (Sync)
        {
            count++;
            latest = text;
            signal = next;
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult(text);
    }

    /// <summary>Completes with the text of the first exception reported after this call.</summary>
    public static Task<string> NextAsync()
    {
        lock (Sync)
        {
            return next.Task;
        }
    }

    /// <summary>Shortens a report to fit in a tool error.</summary>
    public static string Summarize(string text, int maxLength = 3000)
        => text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength), " …");
}
#endif

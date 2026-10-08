#if DEBUG
using System.Threading;
using System.Threading.Tasks;

namespace GUI.Automation;

/// <summary>
/// Unhandled exceptions raised while an agent drives the viewer. They replace the error dialog, so a
/// tool waiting on a load or a frame fails with the exception instead of timing out.
/// </summary>
internal static class UnhandledExceptions
{
    private static readonly Lock Sync = new();
    private static TaskCompletionSource<string> next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static int Count { get; private set; }

    public static string? Latest { get; private set; }

    public static void Report(Exception exception)
    {
        TaskCompletionSource<string> reported;

        using (Sync.EnterScope())
        {
            Count++;
            Latest = Summarize(exception.ToString());
            reported = next;
            next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        reported.TrySetResult(Latest);
    }

    /// <summary>Completes with the first exception reported after this call.</summary>
    public static Task<string> NextAsync()
    {
        using var _ = Sync.EnterScope();
        return next.Task;
    }

    private static string Summarize(string text) => text.Length <= 3000 ? text : string.Concat(text.AsSpan(0, 3000), " ...");
}
#endif

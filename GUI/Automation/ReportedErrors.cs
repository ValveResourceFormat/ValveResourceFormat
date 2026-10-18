#if DEBUG
using System.Threading;
using System.Threading.Tasks;

namespace GUI.Automation;

/// <summary>
/// Errors raised while an agent drives the viewer, which would otherwise show the error dialog. A
/// tool waiting on a load or a frame fails with the error instead of timing out.
/// </summary>
internal static class ReportedErrors
{
    private static readonly Lock Sync = new();
    private static TaskCompletionSource<string> Next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static int Count { get; private set; }

    public static string? Latest { get; private set; }

    public static void Report(string report)
    {
        TaskCompletionSource<string> reported;

        using (Sync.EnterScope())
        {
            Count++;
            Latest = Summarize(report);
            reported = Next;
            Next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        reported.TrySetResult(Latest);
    }

    /// <summary>Completes with the first error reported after this call.</summary>
    public static Task<string> NextAsync()
    {
        using var _ = Sync.EnterScope();
        return Next.Task;
    }

    private static string Summarize(string text) => text.Length <= 3000 ? text : string.Concat(text.AsSpan(0, 3000), " ...");
}
#endif

#if DEBUG
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Automation;

/// <summary>
/// The tabs whose file is still loading, readable from any thread, so a call that finds the UI
/// thread busy can tell a load holding it apart from a modal dialog.
/// </summary>
internal static class TabLoads
{
    private static readonly Lock Sync = new();
    private static readonly Dictionary<TabPage, (string Title, TaskCompletionSource Done)> Pending = [];

    /// <summary>Follows the loads <paramref name="form"/> starts from now on. Call on the UI thread.</summary>
    public static void Track(MainForm form)
    {
        form.TabLoadStarted += OnStarted;
        form.TabLoadCompleted += OnCompleted;
    }

    private static void OnStarted(TabPage page)
    {
        using var _ = Sync.EnterScope();

        Pending[page] = (page.Text, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private static void OnCompleted(TabPage page, Exception? error)
    {
        TaskCompletionSource? done = null;

        using (Sync.EnterScope())
        {
            if (Pending.Remove(page, out var load))
            {
                done = load.Done;
            }
        }

        done?.TrySetResult();
    }

    /// <summary>Completes when the tab has loaded, or is null when it is not loading.</summary>
    public static Task? Of(TabPage page)
    {
        using var _ = Sync.EnterScope();

        return Pending.TryGetValue(page, out var load) ? load.Done.Task : null;
    }

    /// <summary>The title of a tab that is still loading, or null when none is.</summary>
    public static string? AnyTitle()
    {
        using var _ = Sync.EnterScope();

        foreach (var load in Pending.Values)
        {
            return load.Title;
        }

        return null;
    }
}
#endif

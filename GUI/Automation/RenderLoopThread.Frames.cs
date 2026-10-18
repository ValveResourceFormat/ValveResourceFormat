#if DEBUG
using System.Threading;
using System.Threading.Tasks;

namespace GUI.Types.GLViewers;

partial class RenderLoopThread
{
    private sealed class FrameRequest(GLBaseControl control, long after, int count)
    {
        public readonly GLBaseControl Control = control;

        /// <summary>Frames up to this one had started before the request, so they do not count.</summary>
        public readonly long After = after;
        public int Remaining = count;
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly Lock FrameRequestsLock = new();
    private static readonly List<FrameRequest> FrameRequests = [];
    private static int FrameRequestCount;
    private static long FramesStarted;

    // Only touched by the render thread, between the start and the end of one frame
    private static long CurrentFrame;
    private static long PaintedBeforeCurrentFrame;

    private static bool HasFrameRequests => Volatile.Read(ref FrameRequestCount) > 0;

    static partial void KeepDrawingForFrameRequests(ref bool isPaused)
    {
        if (HasFrameRequests)
        {
            isPaused = false;
            renderSignal.Set();
        }
    }

    static partial void OnFrameStarting(GLBaseControl control)
    {
        if (!Automation.Automation.IsEnabled)
        {
            return;
        }

        CurrentFrame = Interlocked.Increment(ref FramesStarted);
        PaintedBeforeCurrentFrame = control.PaintedFrames;
    }

    static partial void OnFrameDrawn(GLBaseControl control)
    {
        if (!Automation.Automation.IsEnabled)
        {
            return;
        }

        if (!HasFrameRequests || control.PaintedFrames == PaintedBeforeCurrentFrame)
        {
            return;
        }

        using var _ = FrameRequestsLock.EnterScope();

        for (var i = FrameRequests.Count - 1; i >= 0; i--)
        {
            var request = FrameRequests[i];

            if (request.Control != control || CurrentFrame <= request.After)
            {
                continue;
            }

            request.Remaining--;

            if (request.Remaining <= 0)
            {
                request.Done.TrySetResult();
                FrameRequests.RemoveAt(i);
            }
        }

        FrameRequestCount = FrameRequests.Count;
    }

    /// <summary>
    /// Draws <paramref name="count"/> more frames of <paramref name="control"/>, even while the app is
    /// in the background. Only the current control draws, so frames of another do not count. Returns
    /// false when they were not all drawn within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<bool> DrawFrames(GLBaseControl control, int count, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return true;
        }

        var request = new FrameRequest(control, Interlocked.Read(ref FramesStarted), count);

        using (FrameRequestsLock.EnterScope())
        {
            FrameRequests.Add(request);
            FrameRequestCount = FrameRequests.Count;
        }

        // With no control the loop would spin on the signal. Attaching a control sets it anyway
        if (currentGLControl != null)
        {
            renderSignal.Set();
        }

        try
        {
            await request.Done.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            using (FrameRequestsLock.EnterScope())
            {
                FrameRequests.Remove(request);
                FrameRequestCount = FrameRequests.Count;
            }
        }
    }
}
#endif

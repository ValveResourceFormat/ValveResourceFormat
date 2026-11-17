using System.Threading;

namespace GUI.Types.GLViewers;

/// <summary>
/// Guards a viewer's GL context and window. The render loop keeps the context current between frames,
/// because releasing a context makes the driver finish all the work queued for it first, which stops
/// the driver from processing a frame while the next one is prepared. A thread that needs the context
/// waits for the loop to release it, which the loop does at the end of its next frame.
/// </summary>
sealed class GLContextLock : IDisposable
{
    private readonly Lock gate = new();
    private readonly ManualResetEventSlim released = new(initialState: true);

    // Only changes under the gate
    private Thread? keptBy;

    private int waiters;

    /// <summary>Enters the lock whether or not the render loop keeps the context, for the loop itself.</summary>
    public Lock.Scope Enter() => gate.EnterScope();

    /// <summary>
    /// Enters the lock once no other thread keeps the context current, so the calling thread can make it
    /// current or destroy the window.
    /// </summary>
    public Lock.Scope EnterReleased()
    {
        var scope = gate.EnterScope();

        if (keptBy == null || keptBy == Thread.CurrentThread)
        {
            return scope;
        }

        scope.Dispose();
        Interlocked.Increment(ref waiters);

        try
        {
            while (true)
            {
                released.Wait();
                scope = gate.EnterScope();

                if (keptBy == null)
                {
                    return scope;
                }

                scope.Dispose();
            }
        }
        finally
        {
            Interlocked.Decrement(ref waiters);
        }
    }

    /// <summary>Whether the calling thread keeps the context current past leaving the lock.</summary>
    public bool IsKeptByCurrentThread => keptBy == Thread.CurrentThread;

    /// <summary>Whether a thread waits in <see cref="EnterReleased"/> for the context to be released.</summary>
    public bool IsRequested => Volatile.Read(ref waiters) > 0;

    /// <summary>Records that the calling thread keeps the context current once it leaves the lock. Call under the lock.</summary>
    public void Keep()
    {
        if (keptBy == Thread.CurrentThread)
        {
            return;
        }

        keptBy = Thread.CurrentThread;
        released.Reset();
    }

    /// <summary>Records that the context is no longer current on the thread that kept it. Call under the lock.</summary>
    public void Release()
    {
        keptBy = null;
        released.Set();
    }

    public void Dispose() => released.Dispose();
}

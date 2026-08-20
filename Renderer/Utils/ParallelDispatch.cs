using System.Runtime.ExceptionServices;
using System.Threading;

namespace ValveResourceFormat.Renderer.Utils;

/// <summary>
/// A body of work split by index across <see cref="ParallelDispatch"/>.
/// </summary>
/// <remarks>
/// Implemented by a long lived object the caller keeps next to its dispatch, so that running it costs no
/// allocation. <see cref="Execute"/> runs on several threads at once, and must only touch state owned by
/// the index it is given.
/// </remarks>
public interface IParallelWork
{
    /// <summary>Runs one item of the work.</summary>
    /// <param name="index">Item to run, within the count passed to <see cref="ParallelDispatch.Run"/>.</param>
    void Execute(int index);
}

/// <summary>
/// Runs an <see cref="IParallelWork"/> over a range of indices on the thread pool without allocating.
/// </summary>
/// <remarks>
/// Hand rolled rather than <c>Parallel.For</c>, which rebuilds its replicating tasks, range workers and
/// index ranges on every call and so allocates a few kilobytes per frame. The workers here are created
/// once and the range is handed out in chunks through a single claim word. One instance per system, and
/// one <see cref="Run"/> at a time: the calling thread works alongside the pool and returns once the
/// range is finished.
/// </remarks>
public sealed class ParallelDispatch : IDisposable
{
    private readonly int chunkSize;
    private readonly ManualResetEventSlim done = new(false);

    private Worker[]? workers;
    private IParallelWork? activeWork;
    private Exception? exception;

    /// <summary>Count in the high 32 bits, next unclaimed index in the low 32.</summary>
    private long claim;
    private int completedChunks;
    private int chunkCount;

    /// <summary>
    /// Creates a dispatch. Cheap to construct; the workers are only built on the first <see cref="Run"/>
    /// that is large enough to need them.
    /// </summary>
    /// <param name="chunkSize">
    /// Indices claimed at a time. Large enough to amortize the interlocked handoff, small enough that a
    /// few heavy items landing together do not strand a thread at the end.
    /// </param>
    public ParallelDispatch(int chunkSize = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);

        this.chunkSize = chunkSize;
    }

    /// <summary>
    /// Runs <paramref name="work"/> over <c>[0, count)</c> and returns once every index has been executed.
    /// An exception thrown by a worker is rethrown here, on the calling thread.
    /// </summary>
    /// <param name="work">Work to run. Held for the duration of the call only.</param>
    /// <param name="count">Number of indices to run.</param>
    public void Run(IParallelWork work, int count)
    {
        // Not enough to fill two chunks, so there is nothing for a second thread to take
        if (count < chunkSize * 2)
        {
            for (var i = 0; i < count; i++)
            {
                work.Execute(i);
            }

            return;
        }

        workers ??= CreateWorkers();

        activeWork = work;
        chunkCount = (count + chunkSize - 1) / chunkSize;
        completedChunks = 0;
        exception = null;
        done.Reset();

        // Armed last, and with release semantics, so a worker still draining from an earlier call reads
        // either that call's exhausted word or this one's fully published state, never a mix of the two
        Volatile.Write(ref claim, (long)count << 32);

        foreach (var worker in workers)
        {
            worker.Queue();
        }

        // The calling thread is a worker too, so a starved pool costs latency rather than a stall
        Drain();

        // Blocking, not spinning: SpinWait escalates to Thread.Sleep(1), which on Windows rounds up to the
        // scheduler tick and costs more than the work being dispatched
        done.Wait();

        activeWork = null;

        if (exception != null)
        {
            ExceptionDispatchInfo.Throw(exception);
        }
    }

    private Worker[] CreateWorkers()
    {
        // One short of the processor count, because the calling thread drains alongside them
        var created = new Worker[Math.Max(1, Environment.ProcessorCount - 1)];

        for (var i = 0; i < created.Length; i++)
        {
            created[i] = new Worker(this);
        }

        return created;
    }

    /// <summary>
    /// Claims chunks until the range is exhausted, run by the calling thread and every worker alike.
    /// Completion is counted in chunks rather than workers, so one that the pool only gets around to late
    /// is extra help rather than a miscount.
    /// </summary>
    private void Drain()
    {
        while (TryClaim(out var start, out var end))
        {
            var work = activeWork!;

            try
            {
                for (var i = start; i < end; i++)
                {
                    work.Execute(i);
                }
            }
            catch (Exception thrown)
            {
                // Rethrown by the caller. Letting one escape a pool thread would take the process down
                Interlocked.CompareExchange(ref exception, thrown, null);
            }
            finally
            {
                // Every chunk increments exactly once, so this only reaches the total after the last one
                // has run its finally: the caller cannot be released early
                if (Interlocked.Increment(ref completedChunks) == chunkCount)
                {
                    done.Set();
                }
            }
        }
    }

    /// <summary>
    /// Takes the next chunk off <see cref="claim"/>. The count is packed in alongside the index so that a
    /// claim reads both from one snapshot, and a worker that outlived its call cannot pair the index of
    /// one range with the count of another.
    /// </summary>
    private bool TryClaim(out int start, out int end)
    {
        var current = Volatile.Read(ref claim);

        while (true)
        {
            var index = (int)current;
            var count = (int)(current >> 32);

            if (index >= count)
            {
                start = 0;
                end = 0;
                return false;
            }

            end = Math.Min(index + chunkSize, count);

            var claimed = (current & ~0xFFFFFFFFL) | (uint)end;
            var previous = Interlocked.CompareExchange(ref claim, claimed, current);

            if (previous == current)
            {
                start = index;
                return true;
            }

            current = previous;
        }
    }

    /// <summary>
    /// A reusable thread pool work item, queued once per <see cref="Run"/>. Queuing the item itself
    /// rather than a delegate keeps the dispatch free of allocations.
    /// </summary>
    private sealed class Worker(ParallelDispatch dispatch) : IThreadPoolWorkItem
    {
        private int queued;

        /// <summary>Queues this worker unless it is still sitting unstarted in the pool from an earlier call.</summary>
        public void Queue()
        {
            if (Interlocked.Exchange(ref queued, 1) == 1)
            {
                return;
            }

            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }

        public void Execute()
        {
            Volatile.Write(ref queued, 0);

            dispatch.Drain();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => done.Dispose();
}

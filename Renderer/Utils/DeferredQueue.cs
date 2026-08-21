using System.Threading;

namespace ValveResourceFormat.Renderer.Utils;

/// <summary>
/// Work found on one thread and handed to the thread that owns it. Anything may <see cref="Post"/> to
/// it; the owner <see cref="Drain"/>s the lot at a point of its choosing and acts on them there.
/// </summary>
/// <remarks>
/// For the state a system running across the thread pool has to reach but does not own. The particle
/// systems ask for sounds through one rather than starting them, because starting a sound reaches the
/// sound player's channel table, instance pools and mixer, none of which expect a second caller.
///
/// The two buffers are kept and swapped rather than reallocated, so a steady stream of items settles at
/// its high water mark and stops allocating from then on. This is why it is not an
/// <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/>, which abandons a segment every time
/// it drains past one, nor a rented array, which would be handed back and taken again every frame.
///
/// Posting takes a lock, which is the trade worth making for work that is rare per frame and whose loss
/// would be a real defect: nothing is dropped when a burst outruns the buffer, and the owner may drain
/// whenever it likes rather than only while the producers happen to be idle.
/// </remarks>
/// <typeparam name="T">The work item. A struct keeps the queue free of per item allocations.</typeparam>
public sealed class DeferredQueue<T>
{
    private const int InitialCapacity = 16;

    private readonly Lock gate = new();
    private T[] posted = new T[InitialCapacity];
    private T[] draining = new T[InitialCapacity];
    private int count;

    /// <summary>Adds an item for the owning thread to act on, from any thread.</summary>
    /// <param name="item">The work item.</param>
    public void Post(in T item)
    {
        lock (gate)
        {
            if (count == posted.Length)
            {
                Array.Resize(ref posted, count * 2);
            }

            posted[count++] = item;
        }
    }

    /// <summary>
    /// Takes everything posted since the last call, for the owning thread to act on. Items posted while
    /// this runs are left for the next call rather than appearing halfway through this one.
    /// </summary>
    /// <returns>The drained items, valid until the next <see cref="Drain"/>.</returns>
    public ReadOnlySpan<T> Drain()
    {
        int drained;

        lock (gate)
        {
            // Swapped rather than copied out, so that acting on the items holds nothing against a poster
            (posted, draining) = (draining, posted);

            drained = count;
            count = 0;
        }

        return draining.AsSpan(0, drained);
    }
}

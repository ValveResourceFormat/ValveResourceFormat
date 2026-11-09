using System.Threading;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>
/// A persistently mapped pixel unpack buffer that background threads write upload data into, so uploads
/// copy from it on the GPU instead of from client memory on the render thread. Regions are reserved in
/// order on the render thread, and come back once their writer let go of them and the GPU finished the
/// copies made from them.
/// </summary>
internal sealed unsafe class StagingRing(string name, int size)
{
    private const int MaxRegions = 1024;
    private const int NotReleased = -1;

    private struct Region
    {
        public long End;

        // NotReleased, 0 when nothing was copied from it, or the serial of the fence covering those copies
        public int ReleasedSerial;
    }

    private readonly Region[] regions = new Region[MaxRegions];
    private readonly Queue<(int Serial, IntPtr Sync)> fences = new();

    private byte* mapped;
    private long head;
    private long tail;
    private int firstRegion;
    private int regionCount;
    private int nextSerial = 1;
    private int completedSerial;
    private bool fenceDue;
    private int writers;
    private volatile bool closed;

    /// <summary>Gets the size of the ring in bytes, the most a single region can hold.</summary>
    public int Size => size;

    /// <summary>Gets the buffer handle, 0 until the first reservation.</summary>
    public int Handle { get; private set; }

    /// <summary>Gets the number of times the buffer was deleted, so a region from before can be told apart.</summary>
    public int Generation { get; private set; }

    /// <summary>
    /// Reserves a contiguous region, creating the buffer on first use. Only the render thread reserves.
    /// </summary>
    /// <returns>Whether the region fit. It does not while older regions are in use.</returns>
    public bool TryReserve(int length, out int region, out int offset)
    {
        region = -1;
        offset = 0;

        if (length > size)
        {
            return false;
        }

        if (Handle == 0)
        {
            Create();
        }

        Reclaim();

        var start = tail;
        var physical = (int)(start % size);

        // A region never wraps, so the end of the buffer is skipped when it does not fit there
        if (physical + length > size)
        {
            start += size - physical;
        }

        if (regionCount == MaxRegions || start + length - head > size)
        {
            return false;
        }

        region = (firstRegion + regionCount) % MaxRegions;
        regions[region] = new Region { End = start + length, ReleasedSerial = NotReleased };
        regionCount++;
        tail = start + length;
        offset = (int)(start % size);

        return true;
    }

    /// <summary>Gets the mapped memory of a region.</summary>
    public Span<byte> GetSpan(int offset, int length) => new(mapped + offset, length);

    /// <summary>
    /// Marks the start of a write from any thread, failing once the buffer is being deleted.
    /// Every successful call is paired with <see cref="EndWrite"/>.
    /// </summary>
    public bool TryBeginWrite()
    {
        Interlocked.Increment(ref writers);

        if (closed)
        {
            Interlocked.Decrement(ref writers);
            return false;
        }

        return true;
    }

    /// <summary>Marks the end of a write that <see cref="TryBeginWrite"/> started.</summary>
    public void EndWrite() => Interlocked.Decrement(ref writers);

    /// <summary>
    /// Gives a region back. Any thread can give back a region nothing was copied from; one that uploads
    /// were issued from is given back on the render thread, and comes free after the next <see cref="Fence"/> passes.
    /// </summary>
    public void Release(int region, int generation, bool copiedFrom)
    {
        if (generation != Generation)
        {
            return;
        }

        if (copiedFrom)
        {
            fenceDue = true;
        }

        Volatile.Write(ref regions[region].ReleasedSerial, copiedFrom ? nextSerial : 0);
    }

    /// <summary>Fences the uploads issued from the regions given back since the last call. Render thread only.</summary>
    public void Fence()
    {
        if (!fenceDue)
        {
            return;
        }

        fences.Enqueue((nextSerial, GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None)));
        nextSerial++;
        fenceDue = false;
    }

    private void Reclaim()
    {
        while (fences.TryPeek(out var fence))
        {
            // Zero timeout only asks; the flush makes sure a fence that was never submitted gets there
            var status = GL.ClientWaitSync(fence.Sync, ClientWaitSyncFlags.SyncFlushCommandsBit, 0);

            if (status is not (WaitSyncStatus.AlreadySignaled or WaitSyncStatus.ConditionSatisfied))
            {
                break;
            }

            GL.DeleteSync(fence.Sync);
            completedSerial = fence.Serial;
            fences.Dequeue();
        }

        while (regionCount > 0)
        {
            ref var region = ref regions[firstRegion];
            var serial = Volatile.Read(ref region.ReleasedSerial);

            if (serial == NotReleased || serial > completedSerial)
            {
                break;
            }

            head = region.End;
            firstRegion = (firstRegion + 1) % MaxRegions;
            regionCount--;
        }

        if (regionCount == 0)
        {
            head = 0;
            tail = 0;
        }
    }

    private void Create()
    {
        // Readable, so the driver keeps it in cached memory: decoding in place reads back what it wrote,
        // which in write-combined memory is slow enough to stall streaming for seconds
        const BufferStorageFlags storage = BufferStorageFlags.MapWriteBit
            | BufferStorageFlags.MapReadBit
            | BufferStorageFlags.MapPersistentBit
            | BufferStorageFlags.MapCoherentBit
            | BufferStorageFlags.ClientStorageBit;

        const BufferAccessMask access = BufferAccessMask.MapWriteBit
            | BufferAccessMask.MapReadBit
            | BufferAccessMask.MapPersistentBit
            | BufferAccessMask.MapCoherentBit;

        Handle = GraphicsDevice.CreateBuffer(name);
        GL.NamedBufferStorage(Handle, size, IntPtr.Zero, storage);
        mapped = (byte*)GL.MapNamedBufferRange(Handle, IntPtr.Zero, size, access);
        closed = false;
    }

    /// <summary>
    /// Fails every write from here on and waits out the ones under way, so the buffer can go away with
    /// its context. Makes no GL calls.
    /// </summary>
    public void Close()
    {
        closed = true;

        var backoff = new SpinWait();

        while (Volatile.Read(ref writers) > 0)
        {
            backoff.SpinOnce();
        }
    }

    /// <summary>
    /// Deletes the buffer, after waiting out the writes under way. Regions handed out before are dropped
    /// when given back, and the next reservation makes a new buffer. Render thread only.
    /// </summary>
    public void Delete()
    {
        if (Handle == 0)
        {
            return;
        }

        Close();

        while (fences.TryDequeue(out var fence))
        {
            GL.DeleteSync(fence.Sync);
        }

        GL.UnmapNamedBuffer(Handle);
        GL.DeleteBuffer(Handle);

        Handle = 0;
        mapped = null;
        head = 0;
        tail = 0;
        firstRegion = 0;
        regionCount = 0;
        fenceDue = false;
        Generation++;
    }
}

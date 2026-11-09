using System.Threading;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>A reserved stretch of a <see cref="StagingRing"/>.</summary>
internal readonly record struct StagingRegion(int Index, int Generation, int Offset, int Length);

/// <summary>
/// A persistently mapped pixel unpack buffer that background threads write upload data into, so uploads
/// copy from it on the GPU instead of from client memory on the render thread. The render thread reserves
/// regions in order, and reuses them once their writer let go and the GPU finished copying from them.
/// </summary>
internal sealed unsafe class StagingRing(string name, int size)
{
    private const int MaxRegions = 1024;
    private const int InUse = -1;
    private const int NothingCopied = 0;

    private struct Slot
    {
        public long End;

        // InUse, NothingCopied, or the serial of the fence covering the uploads copied from it
        public int Released;
    }

    private readonly Slot[] slots = new Slot[MaxRegions];
    private readonly Queue<(int Serial, IntPtr Sync)> fences = new();

    private byte* mapped;
    private int generation;
    private long head;
    private long tail;
    private int firstSlot;
    private int slotCount;
    private int nextSerial = 1;
    private int completedSerial;
    private bool fenceDue;
    private int writers;
    private volatile bool closed;

    /// <summary>Gets the size of the ring in bytes, the most a region can hold.</summary>
    public int Size => size;

    /// <summary>Gets the buffer handle, 0 until the first reservation.</summary>
    public int Handle { get; private set; }

    /// <summary>Reserves a region, which fails while older ones still take up the room. Render thread only.</summary>
    public bool TryReserve(int length, out StagingRegion region)
    {
        region = default;

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
        var offset = (int)(start % size);

        // A region never wraps, so it skips the end of the buffer when it does not fit there
        if (offset + length > size)
        {
            start += size - offset;
            offset = 0;
        }

        if (slotCount == MaxRegions || start + length - head > size)
        {
            return false;
        }

        var index = (firstSlot + slotCount) % MaxRegions;

        slots[index] = new Slot { End = start + length, Released = InUse };
        slotCount++;
        tail = start + length;
        region = new(index, generation, offset, length);

        return true;
    }

    /// <summary>Starts a write to a region from any thread, failing once its buffer is going away. Pair with <see cref="EndWrite"/>.</summary>
    public bool TryBeginWrite(StagingRegion region, out Span<byte> span)
    {
        Interlocked.Increment(ref writers);

        if (closed || region.Generation != generation)
        {
            Interlocked.Decrement(ref writers);
            span = default;
            return false;
        }

        span = new(mapped + region.Offset, region.Length);
        return true;
    }

    /// <summary>Ends a write that <see cref="TryBeginWrite"/> started.</summary>
    public void EndWrite() => Interlocked.Decrement(ref writers);

    /// <summary>
    /// Gives a region back. Any thread can when nothing was copied from it; the render thread does after
    /// issuing uploads from it, and the region then stays taken until the next <see cref="Fence"/> passes.
    /// </summary>
    public void Release(StagingRegion region, bool copiedFrom)
    {
        if (region.Generation != generation)
        {
            return;
        }

        fenceDue |= copiedFrom;
        Volatile.Write(ref slots[region.Index].Released, copiedFrom ? nextSerial : NothingCopied);
    }

    /// <summary>Fences the uploads issued from the regions given back since the last call. Render thread only.</summary>
    public void Fence()
    {
        if (!fenceDue)
        {
            return;
        }

        fences.Enqueue((nextSerial++, GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None)));
        fenceDue = false;
    }

    private void Reclaim()
    {
        // Zero timeout only asks; the flush gets a fence that was never submitted to the GPU
        while (fences.TryPeek(out var fence)
            && GL.ClientWaitSync(fence.Sync, ClientWaitSyncFlags.SyncFlushCommandsBit, 0)
                is WaitSyncStatus.AlreadySignaled or WaitSyncStatus.ConditionSatisfied)
        {
            GL.DeleteSync(fence.Sync);
            completedSerial = fence.Serial;
            fences.Dequeue();
        }

        while (slotCount > 0)
        {
            ref var slot = ref slots[firstSlot];
            var released = Volatile.Read(ref slot.Released);

            if (released == InUse || released > completedSerial)
            {
                break;
            }

            head = slot.End;
            firstSlot = (firstSlot + 1) % MaxRegions;
            slotCount--;
        }

        if (slotCount == 0)
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

    /// <summary>Fails every write from here on and waits out the ones under way. Makes no GL calls.</summary>
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
    /// Deletes the buffer once the writes under way are done. Regions handed out before are dropped when
    /// given back, and the next reservation makes a new buffer. Render thread only.
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
        generation++;
        head = 0;
        tail = 0;
        firstSlot = 0;
        slotCount = 0;
        fenceDue = false;
    }
}

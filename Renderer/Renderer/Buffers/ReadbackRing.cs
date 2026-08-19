using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>
/// A ring of fenced GPU to CPU copies that never stalls the CPU. A frame copies words into a free slot
/// and fences it; a later frame picks the slot up, but only once the fence says the copy has landed. The
/// slots are persistently mapped, so consuming one is a plain memory read.
/// </summary>
internal sealed class ReadbackRing : IDisposable
{
    /// <summary>Copies in flight before the ring runs out of slots and drops frames. Matches the three
    /// frames a driver typically buffers, which is how long a fence takes to signal.</summary>
    public const int Depth = 3;

    private struct Slot
    {
        public int Handle;
        public IntPtr Mapped;
        public IntPtr Sync;
        public int Words;
    }

    private readonly Slot[] slots = new Slot[Depth];
    private readonly int capacityWords;

    private int writeCursor;
    private int readCursor;
    private int inFlight;

    /// <summary>Gets the slots holding a submitted copy that has not been consumed yet.</summary>
    public int InFlight => inFlight;

    /// <summary>Allocates the ring's mapped slots, each holding up to <paramref name="capacityWords"/> uints.</summary>
    public ReadbackRing(int capacityWords)
    {
        Debug.Assert(capacityWords > 0);

        this.capacityWords = capacityWords;

        var size = capacityWords * sizeof(uint);

        const BufferStorageFlags storage = BufferStorageFlags.MapReadBit
            | BufferStorageFlags.MapPersistentBit
            | BufferStorageFlags.MapCoherentBit;

        const BufferAccessMask access = BufferAccessMask.MapReadBit
            | BufferAccessMask.MapPersistentBit
            | BufferAccessMask.MapCoherentBit;

        var handles = new int[Depth];
        GL.CreateBuffers(Depth, handles);

        for (var i = 0; i < Depth; i++)
        {
            var handle = handles[i];

#if DEBUG
            var label = $"ReadbackRing{i}";
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, handle, label.Length, label);
#endif

            GL.NamedBufferStorage(handle, size, IntPtr.Zero, storage);

            slots[i] = new Slot
            {
                Handle = handle,
                Mapped = GL.MapNamedBufferRange(handle, IntPtr.Zero, size, access),
            };
        }
    }

    /// <summary>Copies the head of a GPU buffer into the next free slot and fences it, or does nothing
    /// while every slot is still waiting on its own fence.</summary>
    /// <param name="sourceHandle">Buffer object to copy from.</param>
    /// <param name="words">Uints to copy, at most the ring's capacity.</param>
    /// <param name="slotIndex">Slot the copy went into, for the caller to hang its own snapshot off.</param>
    /// <returns>Whether a copy was submitted.</returns>
    public bool TrySubmit(int sourceHandle, int words, out int slotIndex)
    {
        slotIndex = writeCursor;

        if (inFlight == Depth || words <= 0)
        {
            return false;
        }

        words = Math.Min(words, capacityWords);

        ref var slot = ref slots[writeCursor];
        Debug.Assert(slot.Sync == IntPtr.Zero);

        slot.Words = words;

        // The reduce pass writes the source through an SSBO, so its stores have to be visible to the copy.
        GL.MemoryBarrier(MemoryBarrierFlags.BufferUpdateBarrierBit);

        GL.CopyNamedBufferSubData(sourceHandle, slot.Handle, IntPtr.Zero, IntPtr.Zero, words * sizeof(uint));

        // Coherent mapping needs no flush, but the driver still has to be told the client will read it.
        GL.MemoryBarrier(MemoryBarrierFlags.ClientMappedBufferBarrierBit);

        slot.Sync = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);

        writeCursor = (writeCursor + 1) % Depth;
        inFlight++;

        return true;
    }

    /// <summary>Hands back the oldest copy whose fence has passed, never waiting on one that has not.
    /// Fences signal in submission order, so a pending oldest means no newer one is ready either.</summary>
    /// <param name="slotIndex">Slot the words came from, matching what <see cref="TrySubmit"/> reported.</param>
    /// <param name="words">The copied words, valid until this slot is submitted to again.</param>
    /// <returns>Whether a copy was consumed.</returns>
    public unsafe bool TryConsume(out int slotIndex, out ReadOnlySpan<uint> words)
    {
        slotIndex = readCursor;
        words = default;

        if (inFlight == 0)
        {
            return false;
        }

        ref var slot = ref slots[readCursor];
        Debug.Assert(slot.Sync != IntPtr.Zero);

        // Zero timeout: this asks whether the fence has passed, it never waits for it to.
        var status = GL.ClientWaitSync(slot.Sync, ClientWaitSyncFlags.None, 0);

        if (status is not (WaitSyncStatus.AlreadySignaled or WaitSyncStatus.ConditionSatisfied))
        {
            return false;
        }

        GL.DeleteSync(slot.Sync);
        slot.Sync = IntPtr.Zero;

        words = new ReadOnlySpan<uint>((void*)slot.Mapped, slot.Words);

        readCursor = (readCursor + 1) % Depth;
        inFlight--;

        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        for (var i = 0; i < Depth; i++)
        {
            ref var slot = ref slots[i];

            if (slot.Handle == 0)
            {
                continue;
            }

            if (slot.Sync != IntPtr.Zero)
            {
                GL.DeleteSync(slot.Sync);
                slot.Sync = IntPtr.Zero;
            }

            GL.UnmapNamedBuffer(slot.Handle);
            GL.DeleteBuffer(slot.Handle);

            slot = default;
        }

        inFlight = 0;
        writeCursor = 0;
        readCursor = 0;
    }
}

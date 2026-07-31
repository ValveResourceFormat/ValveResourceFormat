using System.Threading;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Slab allocator for decoded PCM16 samples. All cached sounds live as (offset, length) regions inside
/// a small number of large shared slabs instead of one managed array each - the per-sound arrays were
/// the single biggest allocation source in the whole audio system (large object heap churn on every
/// decode and eviction). Slabs are allocated once and recycled forever: evicting a sound returns its
/// region to a free list (coalescing with neighbors) for the next decode to reuse, so at steady state
/// decoding into the arena allocates nothing. Sounds larger than a slab get a dedicated slab of their
/// own, dropped wholesale on eviction.
/// </summary>
internal sealed class SampleArena
{
    /// <summary>Slab size in samples (32 MB): comfortably above almost every sound, small enough to not overshoot the cache budget much.</summary>
    public const int SlabLength = 1 << 24;

    private readonly Lock mutex = new();
    private readonly List<short[]?> slabs = [];
    private readonly HashSet<int> oversizedSlabs = [];
    private readonly List<Region> freeRegions = [];
    private int bumpSlabIndex = -1;
    private int bumpOffset;

    /// <summary>A leased region of a slab.</summary>
    internal readonly record struct Allocation(short[] Slab, int SlabIndex, int Offset, int Length);

    private readonly record struct Region(int SlabIndex, int Offset, int Length);

    /// <summary>Leases a region of <paramref name="length"/> samples.</summary>
    public Allocation Allocate(int length)
    {
        lock (mutex)
        {
            if (length > SlabLength)
            {
                var oversized = new short[length];
                var oversizedIndex = ReserveSlabSlot(oversized);
                oversizedSlabs.Add(oversizedIndex);
                return new Allocation(oversized, oversizedIndex, 0, length);
            }

            for (var i = 0; i < freeRegions.Count; i++)
            {
                var region = freeRegions[i];
                if (region.Length < length)
                {
                    continue;
                }

                if (region.Length == length)
                {
                    freeRegions.RemoveAt(i);
                }
                else
                {
                    freeRegions[i] = new Region(region.SlabIndex, region.Offset + length, region.Length - length);
                }

                return new Allocation(slabs[region.SlabIndex]!, region.SlabIndex, region.Offset, length);
            }

            if (bumpSlabIndex < 0 || bumpOffset + length > SlabLength)
            {
                if (bumpSlabIndex >= 0 && bumpOffset < SlabLength)
                {
                    // The tail of the outgoing bump slab is still usable space
                    InsertFree(new Region(bumpSlabIndex, bumpOffset, SlabLength - bumpOffset));
                }

                bumpSlabIndex = ReserveSlabSlot(new short[SlabLength]);
                bumpOffset = 0;
            }

            var allocation = new Allocation(slabs[bumpSlabIndex]!, bumpSlabIndex, bumpOffset, length);
            bumpOffset += length;
            return allocation;
        }
    }

    /// <summary>
    /// Returns a leased region for reuse. For an oversized (dedicated-slab) sound the whole slab is
    /// dropped regardless of the given range.
    /// </summary>
    public void Free(int slabIndex, int offset, int length)
    {
        if (length <= 0)
        {
            return;
        }

        lock (mutex)
        {
            if (oversizedSlabs.Remove(slabIndex))
            {
                // Dedicated slab: hand the memory back to the GC, the slot becomes reusable
                slabs[slabIndex] = null;
                return;
            }

            InsertFree(new Region(slabIndex, offset, length));
        }
    }

    /// <summary>
    /// Returns the unused tail of a lease that turned out longer than needed (e.g. a decode size
    /// estimate that came in high). No-op for oversized (dedicated-slab) leases, whose slack is
    /// reclaimed wholesale when the sound is evicted.
    /// </summary>
    public void FreeTail(int slabIndex, int offset, int usedLength, int leasedLength)
    {
        if (leasedLength <= usedLength)
        {
            return;
        }

        lock (mutex)
        {
            if (oversizedSlabs.Contains(slabIndex))
            {
                return;
            }

            InsertFree(new Region(slabIndex, offset + usedLength, leasedLength - usedLength));
        }
    }

    private int ReserveSlabSlot(short[] slab)
    {
        for (var i = 0; i < slabs.Count; i++)
        {
            if (slabs[i] == null)
            {
                slabs[i] = slab;
                return i;
            }
        }

        slabs.Add(slab);
        return slabs.Count - 1;
    }

    /// <summary>Inserts into the position-sorted free list, coalescing with adjacent regions of the same slab.</summary>
    private void InsertFree(Region region)
    {
        var index = freeRegions.Count;

        for (var i = 0; i < freeRegions.Count; i++)
        {
            var other = freeRegions[i];
            if (other.SlabIndex > region.SlabIndex || (other.SlabIndex == region.SlabIndex && other.Offset > region.Offset))
            {
                index = i;
                break;
            }
        }

        if (index > 0)
        {
            var previous = freeRegions[index - 1];
            if (previous.SlabIndex == region.SlabIndex && previous.Offset + previous.Length == region.Offset)
            {
                region = new Region(region.SlabIndex, previous.Offset, previous.Length + region.Length);
                freeRegions.RemoveAt(--index);
            }
        }

        if (index < freeRegions.Count)
        {
            var next = freeRegions[index];
            if (next.SlabIndex == region.SlabIndex && region.Offset + region.Length == next.Offset)
            {
                region = new Region(region.SlabIndex, region.Offset, region.Length + next.Length);
                freeRegions.RemoveAt(index);
            }
        }

        freeRegions.Insert(index, region);
    }
}

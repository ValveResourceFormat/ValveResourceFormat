using System.Runtime.InteropServices;
using ValveResourceFormat.Particles;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Entries kept per particle at the slot its handle names less one. A slot freed when its particle dies is
    /// reused by the next particle.
    /// </summary>
    internal sealed class ParticleHandlePool<T>
    {
        private readonly List<T> slots = [];
        private readonly Stack<int> freeSlots = [];

        /// <summary>The handle column of the particles holding the handles, or null before it is claimed.</summary>
        public int? Column { get; private set; }

        /// <summary>How many slots the pool holds, free or not.</summary>
        public int Count => slots.Count;

        /// <summary>The entry of every slot; a free slot holds the default.</summary>
        public ReadOnlySpan<T> Entries => CollectionsMarshal.AsSpan(slots);

        /// <summary>The entry at a slot.</summary>
        public T this[int slot]
        {
            get => slots[slot];
            set => slots[slot] = value;
        }

        /// <summary>Claims the handle column from the particles the first time, and returns it.</summary>
        public int Claim(ParticleCollection particles)
        {
            Column ??= particles.ClaimHandleColumn();
            return Column.Value;
        }

        /// <summary>
        /// Passes the entry of each particle removed since the last call to <paramref name="release"/> and
        /// frees its slot. Keep <paramref name="release"/> in a field, so the call allocates no delegate.
        /// </summary>
        public void ReleaseDead(ParticleCollection particles, int column, Action<T> release)
        {
            var released = particles.ReleasedHandles(column);

            foreach (var handle in released)
            {
                var slot = handle - 1;

                release(slots[slot]);
                Free(slot);
            }

            released.Clear();
        }

        /// <summary>Stores an entry in a free slot, or a new one, and returns the slot.</summary>
        public int Allocate(T entry)
        {
            if (freeSlots.TryPop(out var slot))
            {
                slots[slot] = entry;
                return slot;
            }

            slots.Add(entry);

            return slots.Count - 1;
        }

        /// <summary>Resets a slot to the default and makes it available to the next <see cref="Allocate"/>.</summary>
        public void Free(int slot)
        {
            slots[slot] = default!;
            freeSlots.Push(slot);
        }

        /// <summary>Removes every slot.</summary>
        public void Clear()
        {
            slots.Clear();
            freeSlots.Clear();
        }
    }
}

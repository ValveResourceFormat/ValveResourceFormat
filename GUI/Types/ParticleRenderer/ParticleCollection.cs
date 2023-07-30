using System;

namespace GUI.Types.ParticleRenderer
{
    class ParticleCollection
    {
        public const int MAX_PARTICLES = 5000;

        public Particle Constants { get; }
        public Span<Particle> Initial => new(initial, 0, Count);
        public Span<Particle> Current => new(current, 0, Count);

        private Particle[] initial;
        private Particle[] current;

        public int Capacity { get; }
        public int Count { get; private set; }

        public ParticleCollection(Particle constants, int initialCapacity)
        {
            Constants = constants;
            Capacity = initialCapacity == 0 ? MAX_PARTICLES : Math.Min(initialCapacity, MAX_PARTICLES);

            initial = new Particle[Capacity];
            Array.Fill(initial, constants);
            current = new Particle[Capacity];
        }

        public int Add()
        {
            if (Count < Capacity)
            {
                initial[Count] = Constants;
                return Count++;
            }

            return -1;
        }

        public void PruneExpired()
        {
            // TODO: This alters the order of the particles so they are no longer in creation order after something expires. Fix that.
            for (var i = 0; i < Count;)
            {
                // TODO: Do the age test only if we know that we have operators that can kill, and move the age incrementing outside of those operators.
                // That way we won't double-increment
                if (current[i].MarkedAsKilled)
                {
                    MoveParticleIndex(Count - 1, i);
                    Count--;
                }
                else
                {
                    ++i;
                }
            }
        }

        public void Clear()
        {
            Count = 0;
        }

        private void MoveParticleIndex(int currentIndex, int newIndex)
        {
            initial[newIndex] = initial[currentIndex];
            initial[newIndex].Index = newIndex;
            current[newIndex] = current[currentIndex];
            current[newIndex].Index = newIndex;
        }
    }
}

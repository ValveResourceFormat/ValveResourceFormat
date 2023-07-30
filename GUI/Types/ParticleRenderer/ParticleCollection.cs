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
            var alive = 0;
            for (var i = 0; i < Count; i++)
            {
                if (!current[i].MarkedAsKilled)
                {
                    if (i != alive)
                    {
                        MoveParticleIndex(i, alive);
                    }
                    alive++;
                }
            }
            Count = alive;
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

using System;

namespace GUI.Types.ParticleRenderer
{
    class ParticleBag
    {
        private readonly bool isGrowable;

        private Particle[] particles;

        public int Count { get; private set; }

        public Span<Particle> LiveParticles => new(particles, 0, Count);

        public ParticleBag(int initialCapacity, bool growable)
        {
            isGrowable = growable;
            particles = new Particle[initialCapacity];
        }

        public int Add()
        {
            if (Count < particles.Length)
            {
                return Count++;
            }
            else if (isGrowable)
            {
                var newSize = particles.Length < 1024 ? particles.Length * 2 : particles.Length + 1024;
                var newArray = new Particle[newSize];
                Array.Copy(particles, 0, newArray, 0, Count);
                particles = newArray;

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
                if (particles[i].MarkedAsKilled)
                {
                    particles[i] = particles[Count - 1];
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
    }
}

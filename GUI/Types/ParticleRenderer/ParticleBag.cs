using System;

namespace GUI.Types.ParticleRenderer
{
    public class ParticleBag
    {
        private readonly bool isGrowable;

        private Particle[] particles;

        public int Count { get; private set; }

        public Span<Particle> LiveParticles => new Span<Particle>(particles, 0, Count);

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
                int newSize = particles.Length < 1024 ? particles.Length * 2 : particles.Length + 1024;
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
            for (int i = 0; i < Count;)
            {
                if (particles[i].Lifetime <= 0)
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

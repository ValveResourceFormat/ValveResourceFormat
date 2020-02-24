using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GUI.Types.ParticleRenderer
{
    public class ParticleBag
    {
        private readonly bool isGrowable;

        private Particle[] particles;
        private int usedParticles = 0;

        private static int SortComparator(in Particle a, in Particle b)
        {
            return a.ParticleCount - b.ParticleCount;
        }

        public ref Particle this[int index]
        {
            get => ref particles[index];
        }

        public int Count
        {
            get => usedParticles;
        }

        public int Capacity
        {
            get => particles.Length;
        }

        public Span<Particle> LiveParticles
        {
            get => new Span<Particle>(particles, 0, usedParticles);
        }

        public ParticleBag(int initialCapacity, bool growable)
        {
            isGrowable = growable;
            particles = new Particle[initialCapacity];
        }

        public int Add()
        {
            if (usedParticles < particles.Length)
            {
                return usedParticles++;
            }
            else if (isGrowable)
            {
                int newSize = particles.Length < 1024 ? particles.Length * 2 : particles.Length + 1024;
                var newArray = new Particle[newSize];
                Array.Copy(particles, 0, newArray, 0, usedParticles);
                particles = newArray;

                return usedParticles++;
            }

            return -1;
        }

        public void PruneExpired()
        {
            bool anyRemoved = false;

            for (int i = 0; i < usedParticles;)
            {
                if (particles[i].Lifetime <= 0)
                {
                    particles[i] = particles[usedParticles - 1];
                    usedParticles--;
                    anyRemoved = true;
                }
                else
                {
                    ++i;
                }
            }

            if (anyRemoved)
            {
                Array.Sort(particles, (a, b) => a.ParticleCount - b.ParticleCount);
            }
        }

        public void Clear()
        {
            usedParticles = 0;
        }
    }
}

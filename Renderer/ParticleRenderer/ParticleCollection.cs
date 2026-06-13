using ValveResourceFormat.Renderer.Particles.Utils;

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Manages the initial and current state arrays for all live particles in a system.
    /// </summary>
    class ParticleCollection
    {
        /// <summary>The hard upper limit on the number of particles in a single collection.</summary>
        public const int MAX_PARTICLES = 5000;

        /// <summary>Gets a span over the initial (spawn-time) particle states.</summary>
        public Span<Particle> Initial => new(initial, 0, Count);
        /// <summary>Gets a span over the current (this-frame) particle states.</summary>
        public Span<Particle> Current => new(current, 0, Count);

        /// <summary>Gets the constant attribute template used when spawning new particles.</summary>
        public Particle Constants { get; }
        private readonly Particle[] initial;
        private readonly Particle[] current;

        /// <summary>Gets the maximum number of particles this collection can hold.</summary>
        public int Capacity { get; }
        /// <summary>Gets the current number of live particles.</summary>
        public int Count { get; private set; }

        /// <summary>
        /// Initializes a new <see cref="ParticleCollection"/> with the given constant particle template and capacity.
        /// </summary>
        public ParticleCollection(Particle constants, int maxParticles)
        {
            Constants = constants;
            Capacity = maxParticles == 0 ? MAX_PARTICLES : Math.Min(maxParticles, MAX_PARTICLES);

            initial = new Particle[Capacity];
            Array.Fill(initial, constants);
            current = new Particle[Capacity];
        }

        /// <summary>
        /// Adds a new particle slot and returns its index, or -1 if the collection is full.
        /// </summary>
        public int Add()
        {
            if (Count < Capacity)
            {
                initial[Count] = Constants;
                return Count++;
            }

            return -1;
        }

        /// <summary>
        /// Removes all particles that have been marked as killed, compacting the live array.
        /// </summary>
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

        /// <summary>
        /// Removes all particles from the collection without deallocating the backing arrays.
        /// </summary>
        public void Clear()
        {
            Count = 0;
        }

        /// <summary>
        /// Returns a deterministic pseudo-random float in [0, 1) for the given particle ID.
        /// </summary>
        public static float RandomSingle(int particleId)
        {
            return RandomFloats.List[particleId % RandomFloats.List.Length]; // TODO: Add seed
        }

        /// <summary>
        /// Returns a deterministic pseudo-random float in [<paramref name="min"/>, <paramref name="max"/>] for the given particle ID.
        /// </summary>
        public static float RandomBetween(int particleId, float min, float max)
        {
            return float.Lerp(min, max, RandomSingle(particleId));
        }

        /// <summary>
        /// Returns a deterministic pseudo-random vector uniformly interpolated between <paramref name="min"/> and <paramref name="max"/>.
        /// </summary>
        public static Vector3 RandomBetween(int particleId, Vector3 min, Vector3 max)
        {
            return Vector3.Lerp(min, max, RandomSingle(particleId));
        }

        /// <summary>
        /// Returns a deterministic pseudo-random vector with each component independently interpolated between the corresponding components of <paramref name="min"/> and <paramref name="max"/>.
        /// </summary>
        public static Vector3 RandomBetweenPerComponent(int particleId, Vector3 min, Vector3 max)
        {
            return new Vector3(
                RandomBetween(particleId, min.X, max.X),
                RandomBetween(particleId + 1, min.Y, max.Y),
                RandomBetween(particleId + 2, min.Z, max.Z));
        }

        /// <summary>
        /// Returns a deterministic pseudo-random float in [<paramref name="min"/>, <paramref name="max"/>] biased by raising the random value to <paramref name="exponent"/>.
        /// </summary>
        public static float RandomWithExponentBetween(int particleId, float exponent, float min, float max)
        {
            return float.Lerp(min, max, MathF.Pow(RandomSingle(particleId), exponent));
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

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
        /// Duration of the previous simulation step. <see cref="Operators.BasicMovement"/> scales the Verlet inertia
        /// term by the current-to-previous step ratio so momentum stays framerate-independent; 0 until
        /// the first step completes.
        /// </summary>
        public float PreviousFrameTime { get; internal set; }

        /// <summary>
        /// Duration of the step being simulated right now; 0 until the first step begins.
        /// </summary>
        public float CurrentFrameTime { get; internal set; }

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
        /// How many particles the last <see cref="PruneExpired"/> removed. Emitters that spawn from
        /// killed parent particles read it from their parent system.
        /// </summary>
        public int KilledLastPass { get; private set; }

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
            KilledLastPass = Count - alive;
            Count = alive;
        }

        /// <summary>
        /// Removes all particles from the collection without deallocating the backing arrays.
        /// </summary>
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

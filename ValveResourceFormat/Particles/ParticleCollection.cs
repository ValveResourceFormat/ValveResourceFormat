namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Manages the initial and current state arrays for all live particles in a system.
    /// </summary>
    public class ParticleCollection
    {
        /// <summary>The most particles a CPU simulated system can hold.</summary>
        public const int MAX_PARTICLES = 20000;

        /// <summary>The most particles a GPU simulated system can hold.</summary>
        public const int MAX_GPU_PARTICLES = 100000;

        /// <summary>Gets a span over the initial (spawn-time) particle states.</summary>
        public Span<Particle> Initial => new(initial, 0, Count);
        /// <summary>Gets a span over the current (this-frame) particle states.</summary>
        public Span<Particle> Current => new(current, 0, Count);

        /// <summary>Gets the constant attribute template used when spawning new particles.</summary>
        public Particle Constants { get; }
        private Particle[] initial;
        private Particle[] current;
        private Particle[]? previousStep;

        /// <summary>Gets the maximum number of particles this collection can hold.</summary>
        public int Capacity { get; }
        /// <summary>Gets the current number of live particles.</summary>
        public int Count { get; private set; }

        /// <summary>
        /// Whether removing particles keeps the survivors in order. Otherwise each removed particle's
        /// slot is filled with the last particle.
        /// </summary>
        public bool PreserveOrderOnKill { get; internal set; }

        /// <summary>
        /// Duration of the previous simulation step. <see cref="Operators.BasicMovement"/> scales the Verlet inertia
        /// term by the current-to-previous step ratio so momentum stays framerate-independent. After a
        /// frame it holds the whole frame time.
        /// </summary>
        public float PreviousFrameTime { get; internal set; } = 0.05f;

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
            Capacity = Math.Clamp(maxParticles, 1, MAX_GPU_PARTICLES);

            var allocated = Math.Min(Capacity, 64);
            initial = new Particle[allocated];
            current = new Particle[allocated];
        }

        /// <summary>
        /// Adds a new particle slot and returns its index, or -1 if the collection is full.
        /// </summary>
        public int Add()
        {
            if (Count >= Capacity)
            {
                return -1;
            }

            if (Count == current.Length)
            {
                var size = Math.Min(Capacity, current.Length * 2);
                Array.Resize(ref current, size);
                Array.Resize(ref initial, size);

                if (previousStep != null)
                {
                    Array.Resize(ref previousStep, size);
                }
            }

            initial[Count] = Constants;
            return Count++;
        }

        /// <summary>
        /// How many particles the last <see cref="PruneExpired"/> removed.
        /// </summary>
        public int KilledLastPass { get; private set; }

        /// <summary>
        /// Removes all particles that have been marked as killed. Survivors keep their order when
        /// <see cref="PreserveOrderOnKill"/> is set; otherwise, walking the removed slots from the front,
        /// each is filled with the last particle once trailing removed particles are dropped.
        /// </summary>
        public void PruneExpired()
        {
            var before = Count;

            if (PreserveOrderOnKill)
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
            else
            {
                var end = Count;

                for (var i = 0; i < end; i++)
                {
                    if (!current[i].MarkedAsKilled)
                    {
                        continue;
                    }

                    while (end - 1 > i && current[end - 1].MarkedAsKilled)
                    {
                        end--;
                    }

                    if (end - 1 > i)
                    {
                        MoveParticleIndex(end - 1, i);
                    }

                    end--;
                }

                Count = end;
            }

            KilledLastPass = before - Count;
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

            if (previousStep != null)
            {
                previousStep[newIndex] = previousStep[currentIndex];
                previousStep[newIndex].Index = newIndex;
            }
        }

        /// <summary>
        /// The particles as they were before the step that carried the system past the time it was
        /// asked to reach, indexed like <see cref="Current"/>. Empty unless such a step was the last one
        /// simulated. A particle created during that step holds its state at creation.
        /// </summary>
        public ReadOnlySpan<Particle> PreviousStep => previousStep == null ? default : new(previousStep, 0, Count);

        /// <summary>Copies the current particles as the state before the step about to run.</summary>
        internal void CapturePreviousStep()
        {
            previousStep ??= new Particle[current.Length];
            Array.Copy(current, previousStep, Count);
        }

        /// <summary>Drops the copy <see cref="CapturePreviousStep"/> took.</summary>
        internal void ReleasePreviousStep() => previousStep = null;

        private readonly List<(ParticleEventType Type, int Index)> pendingEvents = [];

        /// <summary>
        /// Queues an event other than a kill for the particle at <paramref name="index"/>, handed to the
        /// children that listen for it once the running function returns.
        /// </summary>
        internal void RaiseEvent(int index, ParticleEventType type) => pendingEvents.Add((type, index));

        /// <summary>The queued events, in the order they were raised.</summary>
        internal List<(ParticleEventType Type, int Index)> PendingEvents => pendingEvents;

        /// <summary>Records a particle created during the step as its own previous state.</summary>
        internal void SetPreviousStep(int index)
        {
            if (previousStep != null)
            {
                previousStep[index] = current[index];
            }
        }
    }
}

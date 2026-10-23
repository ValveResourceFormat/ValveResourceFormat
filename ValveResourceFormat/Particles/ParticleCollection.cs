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
        public ParticleSpan Initial => new(initial, 0, Count);
        /// <summary>Gets a span over the current (this-frame) particle states.</summary>
        public ParticleSpan Current => new(current, 0, Count);

        /// <summary>Gets the constant attribute template used when spawning new particles.</summary>
        public Particle Constants { get; }
        private readonly ParticleBuffer initial;
        private readonly ParticleBuffer current;
        private ParticleBuffer? previousStep;
        private bool previousStepCaptured;

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
            initial = new ParticleBuffer(allocated);
            current = new ParticleBuffer(allocated);
        }

        /// <summary>
        /// Adds a new particle slot holding the <see cref="Constants"/> and returns its index, or -1 if
        /// the collection is full. Its initial state is left unset until <see cref="SaveInitial"/>.
        /// </summary>
        public int Add()
        {
            if (Count >= Capacity)
            {
                return -1;
            }

            if (Count == current.Capacity)
            {
                var size = Math.Min(Capacity, current.Capacity * 2);
                current.Resize(size);
                initial.Resize(size);
                previousStep?.Resize(size);
            }

            current.Copy(Constants.Buffer, Constants.Index, Count);
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
                var killed = current.MarkedAsKilled;

                for (var i = 0; i < Count; i++)
                {
                    if (!killed[i])
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
                var killed = current.MarkedAsKilled;

                for (var i = 0; i < end; i++)
                {
                    if (!killed[i])
                    {
                        continue;
                    }

                    while (end - 1 > i && killed[end - 1])
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
            initial.Move(currentIndex, newIndex);
            current.Move(currentIndex, newIndex);

            if (previousStepCaptured)
            {
                previousStep!.Move(currentIndex, newIndex);
            }
        }

        /// <summary>
        /// The particles as they were before the step that carried the system past the time it was
        /// asked to reach, indexed like <see cref="Current"/>. Empty unless such a step was the last one
        /// simulated. A particle created during that step holds its state at creation. Only the
        /// attributes renderers blend are kept, see <see cref="ParticleBuffer.CopyBlendedAttributes"/>.
        /// </summary>
        public ParticleSpan PreviousStep => previousStepCaptured ? new(previousStep!, 0, Count) : default;

        /// <summary>Copies the current particles as the state before the step about to run.</summary>
        internal void CapturePreviousStep()
        {
            previousStep ??= new ParticleBuffer(current.Capacity);
            previousStep.CopyBlendedAttributes(current, 0, Count);
            previousStepCaptured = true;
        }

        /// <summary>Drops the copy <see cref="CapturePreviousStep"/> took.</summary>
        internal void ReleasePreviousStep() => previousStepCaptured = false;

        private readonly List<(ParticleEventType Type, int Index)> pendingEvents = [];

        /// <summary>
        /// Queues an event other than a kill for the particle at <paramref name="index"/>, handed to the
        /// children that listen for it once the running function returns.
        /// </summary>
        internal void RaiseEvent(int index, ParticleEventType type) => pendingEvents.Add((type, index));

        /// <summary>The queued events, in the order they were raised.</summary>
        internal List<(ParticleEventType Type, int Index)> PendingEvents => pendingEvents;

        /// <summary>
        /// Takes the current state of the <paramref name="count"/> particles from <paramref name="first"/>
        /// as their initial state.
        /// </summary>
        internal void SaveInitial(int first, int count) => initial.Copy(current, first, first, count);

        /// <summary>Records a particle created during the step as its own previous state.</summary>
        internal void SetPreviousStep(int index)
        {
            if (previousStepCaptured)
            {
                previousStep!.CopyBlendedAttributes(current, index, 1);
            }
        }
    }
}

using System.Numerics.Tensors;
using System.Runtime.InteropServices;

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

        /// <summary>
        /// Gets the vector <paramref name="field"/> held in the first slot, whether or not a live particle
        /// holds it, or the field's constant when it is not stored.
        /// </summary>
        internal Vector3 FirstSlotVector(ParticleField field)
            => Capacity > 0 ? new ParticleSpan(current, 0, 1).ReadVector(field, 0) : Vector3.Zero;

        private readonly ParticleBuffer initial;
        private readonly ParticleBuffer current;
        private ParticleBuffer? previousStep;
        private bool previousStepCaptured;
        private ParticleBuffer? blended;

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
        /// Initializes a new <see cref="ParticleCollection"/> with the values new particles start as and its capacity.
        /// </summary>
        /// <remarks>No attribute is stored until <see cref="SetStorage"/> chooses them.</remarks>
        internal ParticleCollection(ParticleBuffer.ConstantValues constants, int maxParticles)
        {
            Capacity = Math.Clamp(maxParticles, 1, MAX_GPU_PARTICLES);

            initial = new ParticleBuffer(Capacity, 0)
            {
                Constants = constants,
            };
            current = new ParticleBuffer(Capacity, 0)
            {
                Constants = constants,
            };
        }

        /// <summary>
        /// Chooses which attributes get arrays, as bit masks over <see cref="ParticleField"/>. The
        /// current particles store <paramref name="stored"/>; every other attribute reads its constant
        /// and must not be written. The initial particles keep a copy of <paramref name="initialStored"/>,
        /// read <paramref name="initialShared"/> from the current particles, and read the constant for the
        /// rest. Particles already in the collection keep their values.
        /// </summary>
        internal void SetStorage(ulong stored, ulong initialStored, ulong initialShared)
        {
            current.SetStoredFields(stored);
            initial.SetStoredFields(initialStored);
            initial.ShareFrom(current, initialShared);
            previousStep?.SetStoredFields(stored & blendedFieldMask);
            blended?.SetStoredFields(BlendedStorage(stored));
        }

        /// <summary>
        /// The bit mask of the attributes renderers blend between two steps: <see cref="ParticleBuffer.BlendedFieldMask"/>
        /// and those added by <see cref="AddBlendedFields"/>.
        /// </summary>
        private ulong blendedFieldMask = ParticleBuffer.BlendedFieldMask;

        /// <summary>Adds the attributes in the <paramref name="fields"/> bit mask to those renderers blend between two steps.</summary>
        internal void AddBlendedFields(ulong fields) => blendedFieldMask |= fields;

        /// <summary>
        /// The attributes the blended particles store: those of <paramref name="stored"/> renderers blend,
        /// and alpha always, for the particles that are not drawn yet.
        /// </summary>
        private ulong BlendedStorage(ulong stored) => (stored & blendedFieldMask) | ParticleField.Alpha.Mask();

        /// <summary>The bit mask of the attributes the current particles store.</summary>
        internal ulong StoredFields => current.OwnedFields;

        /// <summary>
        /// Adds a new particle slot holding the constant attributes and returns its index, or -1 if the
        /// collection is full. Its initial state is left unset until <see cref="SaveInitial"/>.
        /// </summary>
        internal int Add()
        {
            if (Count >= Capacity)
            {
                return -1;
            }

            current.ResetToConstants(Count);
            return Count++;
        }

        private readonly List<int> kills = [];

        /// <summary>Marks the particle at <paramref name="index"/> for removal once the running function returns.</summary>
        internal void Kill(int index) => kills.Add(index);

        /// <summary>Gets a value indicating whether any particle is marked for removal.</summary>
        internal bool HasKills => kills.Count > 0;

        /// <summary>Gets the indices of the particles marked for removal, in ascending order and each once.</summary>
        internal List<int> SortKills()
        {
            kills.Sort();

            var unique = 0;

            for (var i = 0; i < kills.Count; i++)
            {
                if (i == 0 || kills[i] != kills[unique - 1])
                {
                    kills[unique++] = kills[i];
                }
            }

            kills.RemoveRange(unique, kills.Count - unique);
            return kills;
        }

        /// <summary>
        /// Removes all particles that have been marked for removal. Survivors keep their order when
        /// <see cref="PreserveOrderOnKill"/> is set; otherwise, walking the removed slots from the front,
        /// each is filled with the last particle once trailing removed particles are dropped.
        /// </summary>
        internal void PruneExpired()
        {
            var before = Count;
            var killed = CollectionsMarshal.AsSpan(SortKills());
            moves.Clear();

            // The moves are planned first, then applied one attribute at a time in the same order
            if (killed.Length > 0 && PreserveOrderOnKill)
            {
                var alive = killed[0];
                var next = 0;

                for (var i = killed[0]; i < Count; i++)
                {
                    if (next < killed.Length && killed[next] == i)
                    {
                        next++;
                        continue;
                    }

                    moves.Add((i, alive));
                    alive++;
                }

                Count = alive;
            }
            else if (killed.Length > 0)
            {
                var end = Count;
                var back = killed.Length - 1;

                for (var front = 0; front <= back && killed[front] < end; front++)
                {
                    var index = killed[front];

                    while (back > front && killed[back] == end - 1)
                    {
                        back--;
                        end--;
                    }

                    if (end - 1 > index)
                    {
                        moves.Add((end - 1, index));
                    }

                    end--;
                }

                Count = end;
            }

            if (moves.Count > 0)
            {
                var plan = CollectionsMarshal.AsSpan(moves);
                initial.Move(plan);
                current.Move(plan);

                if (previousStepCaptured)
                {
                    previousStep!.Move(plan);
                }

                // Handles are swapped rather than copied, so the handles of the particles moved over end
                // up past the survivors to be released
                foreach (var handles in handleColumns)
                {
                    foreach (var (from, to) in plan)
                    {
                        (handles[to], handles[from]) = (handles[from], handles[to]);
                    }
                }
            }

            kills.Clear();
            ReleaseHandles(Count, before);
        }

        private readonly List<(int From, int To)> moves = [];

        /// <summary>
        /// Removes all particles from the collection without deallocating the backing arrays, as
        /// <see cref="PruneExpired"/> does once all are marked for removal.
        /// </summary>
        internal void Clear()
        {
            kills.Clear();
            ReleaseHandles(0, Count);
            Count = 0;
        }

        private readonly List<int[]> handleColumns = [];
        private readonly List<List<int>> releasedHandles = [];

        /// <summary>
        /// Adds a column in which the caller keeps a handle per particle to state of its own, and returns
        /// its index. The handle moves with the particle; new particles start with 0, and once a particle
        /// is removed its handle is handed back through <see cref="ReleasedHandles"/>.
        /// </summary>
        internal int ClaimHandleColumn()
        {
            handleColumns.Add(new int[Capacity]);
            releasedHandles.Add([]);
            return handleColumns.Count - 1;
        }

        /// <summary>Gets the handle of every live particle in <paramref name="column"/>. 0 is no handle.</summary>
        internal Span<int> Handles(int column) => handleColumns[column].AsSpan(0, Count);

        /// <summary>
        /// Gets the non-zero handles of <paramref name="column"/> that belonged to particles removed since
        /// the caller last cleared the list, for it to release.
        /// </summary>
        internal List<int> ReleasedHandles(int column) => releasedHandles[column];

        private void ReleaseHandles(int first, int end)
        {
            for (var column = 0; column < handleColumns.Count; column++)
            {
                var released = releasedHandles[column];
                var handles = handleColumns[column].AsSpan(first, end - first);

                for (var i = 0; i < handles.Length; i++)
                {
                    if (handles[i] != 0)
                    {
                        released.Add(handles[i]);
                        handles[i] = 0;
                    }
                }
            }
        }

        /// <summary>The system time particle ages are measured against.</summary>
        internal float Time
        {
            get => current.Time;
            set
            {
                current.Time = value;
                initial.Time = value;
            }
        }

        /// <summary>
        /// The particles as they were before the step that carried the system past the time it was
        /// asked to reach, indexed like <see cref="Current"/>. Empty unless such a step was the last one
        /// simulated. A particle created during that step holds its state at creation. Only the stored
        /// attributes renderers blend are kept.
        /// </summary>
        public ParticleSpan PreviousStep => previousStepCaptured ? new(previousStep!, 0, Count) : default;

        /// <summary>Copies the current particles as the state before the step about to run.</summary>
        internal void CapturePreviousStep()
        {
            if (previousStep == null)
            {
                previousStep = new ParticleBuffer(current.Capacity, StoredFields & blendedFieldMask)
                {
                    Constants = current.Constants,
                };
            }

            previousStep.Copy(current, 0, 0, Count);
            previousStep.Time = current.Time;
            previousStepCaptured = true;
        }

        /// <summary>Drops the copy <see cref="CapturePreviousStep"/> took.</summary>
        internal void ReleasePreviousStep() => previousStepCaptured = false;

        /// <summary>
        /// The particles blended <paramref name="fraction"/> of the way from <see cref="PreviousStep"/>
        /// to <see cref="Current"/>. Particles created during that step get no alpha, wherever in the step
        /// the blend lands. Only the stored attributes renderers blend are blended, every other attribute
        /// reads the current particles. The blend is redone on every call, so it sees
        /// what renderers wrote before it.
        /// </summary>
        /// <param name="fraction">0 for the previous step, 1 for the current one.</param>
        internal ParticleSpan Blended(float fraction)
        {
            blended ??= new ParticleBuffer(Capacity, BlendedStorage(StoredFields));

            var span = new ParticleSpan(blended, 0, Count);

            // Every attribute the blend leaves alone reads the current particles, and an attribute the
            // system does not store is the same constant at both steps
            blended.ShareFrom(current, current.OwnedFields);

            var currentSpan = Current;
            var previous = PreviousStep;

            for (var field = (ParticleField)0; (int)field < ParticleBuffer.FieldCount; field++)
            {
                if ((blendedFieldMask & field.Mask()) == 0)
                {
                    continue;
                }

                if (ParticleBuffer.IsVector(field))
                {
                    if (currentSpan.TryGetVectors(field, out var vectors, out _))
                    {
                        Blend(previous.Vectors(field), vectors, span.Vectors(field), fraction);
                    }
                }
                else if (currentSpan.TryGetFloats(field, out var scalars, out _))
                {
                    Blend(previous.Floats(field), scalars, span.Floats(field), fraction);
                }
            }

            blended.Time = MathUtils.Lerp(previous.Time, currentSpan.Time, fraction);

            var alpha = span.Floats(ParticleField.Alpha);

            if (!currentSpan.TryGetFloats(ParticleField.Alpha, out _, out var constantAlpha))
            {
                alpha.Fill(constantAlpha);
            }

            var creationTime = currentSpan.Floats(ParticleField.CreationTime);

            for (var i = 0; i < alpha.Length; i++)
            {
                if (creationTime[i] >= previous.Time)
                {
                    alpha[i] = 0f;
                }
            }

            return span;
        }

        private static void Blend(Span<float> previous, Span<float> current, Span<float> blended, float fraction)
            => TensorPrimitives.Lerp(previous, current, fraction, blended);

        private static void Blend(Span<Vector3> previous, Span<Vector3> current, Span<Vector3> blended, float fraction)
            => Blend(MemoryMarshal.Cast<Vector3, float>(previous), MemoryMarshal.Cast<Vector3, float>(current), MemoryMarshal.Cast<Vector3, float>(blended), fraction);

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

        /// <summary>Records the <paramref name="count"/> particles from <paramref name="first"/>, created during the step, as their own previous state.</summary>
        internal void SetPreviousStep(int first, int count)
        {
            if (previousStepCaptured)
            {
                previousStep!.Copy(current, first, first, count);
            }
        }
    }
}

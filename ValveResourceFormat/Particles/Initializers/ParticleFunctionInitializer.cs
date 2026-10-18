namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Base class for all particle initializers. Initializers run once when a particle is created
    /// and set the particle's initial attribute values.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleFunctionInitializer">CParticleFunctionInitializer</seealso>
    abstract class ParticleFunctionInitializer : ParticleFunction
    {
        protected ParticleFunctionInitializer(ParticleDefinitionParser parse) : base(parse)
        {
            AssociatedEmitterIndex = parse.Int32("m_nAssociatedEmitterIndex", AssociatedEmitterIndex);
        }

        /// <summary>
        /// The position in the emitter list of the only emitter whose particles this initializer runs
        /// on, or -1 to run on every emitter's particles and the initial ones.
        /// </summary>
        public int AssociatedEmitterIndex { get; } = -1;

        public abstract Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState);

        /// <summary>
        /// Runs the initializer over the particles one emission created, which sit at the end of
        /// <paramref name="particles"/>. Unless overridden, initializes them one by one in the calls
        /// <see cref="SplitIntoCalls"/> gives, with inputs evaluated afresh and <see cref="BeginCall"/>
        /// run for each call.
        /// </summary>
        public virtual void Initialize(Span<Particle> batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var (head, blocks, tail) = SplitIntoCalls(batch);
            var firstCall = true;

            foreach (var call in (ReadOnlySpan<Range>)[head, blocks, tail])
            {
                var run = batch[call];

                if (run.IsEmpty)
                {
                    continue;
                }

                if (!firstCall)
                {
                    particleSystemState.Random.BeginFunctionCall();
                }

                firstCall = false;
                BeginCall(ref run[0], particles, particleSystemState);
                InitializeEach(run, particles, particleSystemState);
            }
        }

        /// <summary>
        /// Sets up the state an initializer keeps for one call, before the default
        /// <see cref="Initialize(Span{Particle}, ParticleCollection, ParticleSystemState)"/> initializes
        /// the call's particles, the first of which is <paramref name="first"/>.
        /// </summary>
        protected virtual void BeginCall(ref Particle first, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
        }

        /// <summary>Initializes the particles of <paramref name="run"/> one by one, as a single call.</summary>
        protected void InitializeEach(Span<Particle> run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            foreach (ref var particle in run)
            {
                Initialize(ref particle, particles, particleSystemState);
            }
        }

        /// <summary>
        /// Splits a batch into the runs the particles are initialized in, one call each: the whole
        /// batch below 16 particles, else the particles before the first collection index that is a
        /// multiple of four, the whole blocks of four from there, and the rest. Any run can be empty;
        /// state an initializer keeps per call, such as its particle count, restarts with each run.
        /// </summary>
        protected static (Range Head, Range Blocks, Range Tail) SplitIntoCalls(Span<Particle> batch)
        {
            if (batch.Length < 16)
            {
                return (Range.All, ^0.., ^0..);
            }

            var first = batch[0].Index;
            var blocksStart = MathUtils.AlignUp(first, 4) - first;
            var blocksEnd = ((first + batch.Length) & ~3) - first;

            return (..blocksStart, blocksStart..blocksEnd, blocksEnd..);
        }

        /// <summary>Builds the per-instance running state the initializer starts with.</summary>
        public virtual void Reset()
        {
        }

        /// <summary>Builds the running state when the system is created or replayed; <see cref="Reset"/> unless overridden.</summary>
        public virtual void Initialize(ParticleSystemState particleSystemState) => Reset();

        /// <summary>Called when the system restarts; rebuilds the running state unless overridden.</summary>
        public virtual void Restart(ParticleSystemState particleSystemState) => Reset();

        /// <summary>Bit for <paramref name="field"/> in a <see cref="WrittenFields"/> mask.</summary>
        protected static ulong FieldMask(ParticleField field) => 1UL << (int)field;

        /// <summary>
        /// Whether <paramref name="field"/> is still unwritten for the particles being initialized:
        /// neither their creator nor an initializer before this one declared writing it.
        /// </summary>
        protected static bool IsUnwritten(ParticleSystemState particleSystemState, ParticleField field)
            => (particleSystemState.InitializedFields & FieldMask(field)) == 0;

        /// <summary>
        /// Places a new particle at <paramref name="position"/>, and its previous position at
        /// <paramref name="previous"/> unless a function before this one wrote that.
        /// </summary>
        protected static void SetSpawnPosition(ref Particle particle, ParticleSystemState particleSystemState, Vector3 position, Vector3 previous)
        {
            particle.Position = position;

            if (IsUnwritten(particleSystemState, ParticleField.PositionPrevious))
            {
                particle.PositionPrevious = previous;
            }
        }

        /// <summary>
        /// Adds <paramref name="velocity"/> to a new particle by moving its previous position back by one
        /// previous step of it, or by <paramref name="velocity"/> itself when that is a displacement per step.
        /// </summary>
        protected static void AddVelocity(ref Particle particle, ParticleCollection particles, Vector3 velocity, bool perStep)
            => particle.PositionPrevious -= perStep ? velocity : velocity * particles.PreviousFrameTime;

        /// <summary>
        /// Bit mask over <see cref="ParticleField"/> values of the attributes this initializer
        /// declares it writes, not the ones it happens to assign. Under the pre-version-6
        /// first-writer-wins rule an initializer runs only while some attribute it declares is
        /// still unwritten, so one declaring nothing never runs there.
        /// </summary>
        public abstract ulong WrittenFields { get; }

        /// <summary>
        /// Position of this initializer in the definition's initializer list, counting entries the
        /// renderer dropped. The pre-version-6 first-writer-wins rule stops applying at
        /// <c>m_nFirstMultipleOverride_BackwardCompat</c>, which indexes this list.
        /// </summary>
        public int DefinitionIndex { get; set; } = -1;
    }
}

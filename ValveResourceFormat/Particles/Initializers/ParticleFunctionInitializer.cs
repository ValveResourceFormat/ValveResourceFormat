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
        /// <paramref name="particles"/>. Initializes them one by one unless overridden.
        /// </summary>
        public virtual void Initialize(Span<Particle> batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            foreach (ref var particle in batch)
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

        /// <summary>
        /// Whether the initializer takes part in initializing the particles being created now. One
        /// that does not is skipped as if disabled, and counts as writing nothing.
        /// </summary>
        public virtual bool ShouldRun(ParticleSystemState particleSystemState) => true;

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

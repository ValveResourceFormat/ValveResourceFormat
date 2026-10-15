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

        /// <summary>Builds the per-instance running state the initializer starts with.</summary>
        public virtual void Reset()
        {
        }

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

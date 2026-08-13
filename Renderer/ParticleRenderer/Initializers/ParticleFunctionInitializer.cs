namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Base class for all particle initializers. Initializers run once when a particle is created
    /// and set the particle's initial attribute values.
    /// </summary>
    abstract class ParticleFunctionInitializer : ParticleFunction
    {
        protected ParticleFunctionInitializer(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public abstract Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState);

        /// <summary>Rebuilds any per-instance running state when the system starts or restarts.</summary>
        public virtual void Reset()
        {
        }

        /// <summary>Bit for <paramref name="field"/> in a <see cref="WrittenFields"/> mask.</summary>
        protected static ulong FieldMask(ParticleField field) => 1UL << (int)field;

        /// <summary>
        /// The particle attributes this initializer writes, as a bit mask over
        /// <see cref="ParticleField"/> values, matching what the engine's class declares rather
        /// than what this port happens to assign. Under the pre-version-6 first-writer-wins rule
        /// an initializer runs only while some attribute it declares is still unwritten, so a
        /// class declaring nothing never runs there.
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

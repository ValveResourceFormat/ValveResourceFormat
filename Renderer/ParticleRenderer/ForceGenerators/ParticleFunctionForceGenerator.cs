namespace ValveResourceFormat.Renderer.Particles.ForceGenerators
{
    /// <summary>
    /// Base class for particle force generators. The BasicMovement operator invokes every
    /// force generator to accumulate an acceleration into each particle's
    /// <see cref="Particle.ForceAccumulator"/>, then integrates it into velocity and clears it. They
    /// are built from the system's <c>m_ForceGenerators</c> block.
    /// </summary>
    abstract class ParticleFunctionForceGenerator : ParticleFunction
    {
        protected ParticleFunctionForceGenerator(ParticleDefinitionParser parse) : base(parse)
        {
        }

        /// <summary>
        /// Adds this generator's acceleration to each particle's <see cref="Particle.ForceAccumulator"/>,
        /// scaled by the operator fade <paramref name="strength"/>.
        /// </summary>
        public abstract void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength);
    }
}

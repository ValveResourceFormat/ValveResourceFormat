namespace ValveResourceFormat.Particles.ForceGenerators
{
    /// <summary>
    /// Base class for particle force generators. The <see cref="ValveResourceFormat.Particles.Operators.BasicMovement"/> operator invokes every
    /// force generator to accumulate an acceleration into each particle's
    /// <see cref="Particle.ForceAccumulator"/>, then integrates it into velocity and clears it. They
    /// are built from the system's <c>m_ForceGenerators</c> block.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleFunctionForce">CParticleFunctionForce</seealso>
    abstract class ParticleFunctionForceGenerator : ParticleFunction
    {
        protected ParticleFunctionForceGenerator(ParticleDefinitionParser parse) : base(parse)
        {
        }

        /// <summary>The gravity of the <c>C_OP_BasicMovement</c> running this generator, set before every call.</summary>
        public Vector3 MovementGravity { get; set; }

        /// <summary>
        /// Adds this generator's acceleration to each particle's <see cref="Particle.ForceAccumulator"/>,
        /// scaled by the operator fade <paramref name="strength"/>.
        /// </summary>
        public abstract void GenerateForces(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength);
    }
}

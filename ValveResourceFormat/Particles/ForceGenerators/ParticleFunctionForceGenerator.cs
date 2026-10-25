namespace ValveResourceFormat.Particles.ForceGenerators
{
    /// <summary>
    /// Base class for particle force generators. The <see cref="ValveResourceFormat.Particles.Operators.BasicMovement"/> operator invokes every
    /// force generator to accumulate an acceleration per particle, then integrates the sum into velocity.
    /// They are built from the system's <c>m_ForceGenerators</c> block.
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
        /// Adds this generator's acceleration for each particle to <paramref name="forces"/>, indexed like
        /// <c>particles.Current</c>, scaled by the operator fade <paramref name="strength"/>.
        /// </summary>
        public abstract void GenerateForces(ParticleCollection particles, Span<Vector3> forces, float frameTime, ParticleSystemState particleSystemState, float strength);
    }
}

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Base class for particle operators that run each simulation frame to modify particle attributes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleFunctionOperator">CParticleFunctionOperator</seealso>
    abstract class ParticleFunctionOperator : ParticleFunction
    {
        protected ParticleFunctionOperator(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public abstract void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength);

        /// <summary>Rebuilds any per-instance running state when the system starts or restarts.</summary>
        public virtual void Reset()
        {
        }

        /// <summary>
        /// Moves <paramref name="current"/> toward <paramref name="value"/> by <paramref name="strength"/>.
        /// A strength of 1 or more gives <paramref name="value"/> itself rather than overshooting it.
        /// </summary>
        protected static float BlendByStrength(float current, float value, float strength)
            => strength >= 1f ? value : (value * strength) + (current * (1f - strength));

        /// <inheritdoc cref="BlendByStrength(float, float, float)"/>
        protected static Vector3 BlendByStrength(Vector3 current, Vector3 value, float strength)
            => strength >= 1f ? value : (value * strength) + (current * (1f - strength));
    }
}

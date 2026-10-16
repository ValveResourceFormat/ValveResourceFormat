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

        /// <summary>Builds the per-instance running state the operator starts with.</summary>
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

        /// <summary>Builds the running state when the system is created or replayed; <see cref="Reset"/> unless overridden.</summary>
        public virtual void Initialize(ParticleSystemState particleSystemState) => Reset();

        /// <summary>Called when the system restarts; rebuilds the running state unless overridden.</summary>
        public virtual void Restart(ParticleSystemState particleSystemState) => Reset();
    }
}

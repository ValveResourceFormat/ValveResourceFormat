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
    }
}

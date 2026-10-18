namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Base class for pre-emission operators, which run once per step before particles are emitted
    /// and are used to modify particle system state such as control point positions and orientations.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleFunctionPreEmission">CParticleFunctionPreEmission</seealso>
    abstract class ParticleFunctionPreEmissionOperator : ParticleFunction
    {
        /// <summary>
        /// Whether this operator runs only in the system's first frame, before the initial particles,
        /// and never in the steps after it.
        /// </summary>
        public virtual bool RunOnce { get; }

        protected ParticleFunctionPreEmissionOperator(ParticleDefinitionParser parse) : base(parse)
        {
            RunOnce = parse.Boolean("m_bRunOnce", false);
        }

        // "remap average scalar value to cp" requires knowing particles as well as system status...s
        public abstract void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength);

        /// <summary>Builds the per-instance running state the operator starts with.</summary>
        public virtual void Reset()
        {
        }

        /// <summary>Builds the running state when the system is created or replayed; <see cref="Reset"/> unless overridden.</summary>
        public virtual void Initialize(ParticleSystemState particleSystemState) => Reset();

        /// <summary>Called when the system restarts; rebuilds the running state unless overridden.</summary>
        public virtual void Restart(ParticleSystemState particleSystemState) => Reset();
    }
}

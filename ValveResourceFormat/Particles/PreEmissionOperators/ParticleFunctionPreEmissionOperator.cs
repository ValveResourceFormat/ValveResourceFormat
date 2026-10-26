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
        public bool RunOnce { get; }

        protected ParticleFunctionPreEmissionOperator(ParticleDefinitionParser parse) : base(parse)
        {
            RunOnce = parse.Boolean("m_bRunOnce", false);
        }

        /// <summary>
        /// Runs the operator on the system state, before any particles are emitted or simulated this step.
        /// </summary>
        public abstract void Operate(ParticleSystemState particleSystemState, float frameTime, float strength);
    }
}

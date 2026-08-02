namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Base class for pre-emission operators, which run once per frame before particles are emitted
    /// and are used to modify particle system state such as control point positions and orientations.
    /// </summary>
    abstract class ParticleFunctionPreEmissionOperator : ParticleFunction
    {
        /// <summary>
        /// Whether this operator only runs on the system's first simulated step instead of every frame.
        /// </summary>
        public bool RunOnce { get; }

        /// <summary>
        /// Whether this operator has already run. Only consulted when <see cref="RunOnce"/> is set,
        /// and cleared when the system restarts.
        /// </summary>
        public bool HasRun { get; set; }

        protected ParticleFunctionPreEmissionOperator(ParticleDefinitionParser parse) : base(parse)
        {
            RunOnce = parse.Boolean("m_bRunOnce", false);
        }

        // "remap average scalar value to cp" requires knowing particles as well as system status...s
        public abstract void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime);
    }
}

namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Base class for pre-emission operators, which run once per frame before particles are emitted
    /// and are used to modify particle system state such as control point positions and orientations.
    /// </summary>
    abstract class ParticleFunctionPreEmissionOperator : ParticleFunction
    {
        protected ParticleFunctionPreEmissionOperator(ParticleDefinitionParser parse) : base(parse)
        {
        }

        // "remap average scalar value to cp" requires knowing particles as well as system status...s
        public abstract void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime);
    }
}

namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Base class for particle operators that run each simulation frame to modify particle attributes.
    /// </summary>
    abstract class ParticleFunctionOperator : ParticleFunction
    {
        protected ParticleFunctionOperator(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public abstract void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState);
    }
}

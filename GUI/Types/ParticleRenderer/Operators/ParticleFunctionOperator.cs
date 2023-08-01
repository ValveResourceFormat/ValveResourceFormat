namespace GUI.Types.ParticleRenderer.Operators
{
    abstract class ParticleFunctionOperator : ParticleFunction
    {
        protected ParticleFunctionOperator(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public abstract void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState);
    }
}

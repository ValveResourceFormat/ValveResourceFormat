namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    abstract class ParticleFunctionPreEmissionOperator : ParticleFunction
    {
        protected ParticleFunctionPreEmissionOperator(ParticleDefinitionParser parse) : base(parse)
        {
        }

        // "remap average scalar value to cp" requires knowing particles as well as system status...s
        public abstract void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime);
    }
}

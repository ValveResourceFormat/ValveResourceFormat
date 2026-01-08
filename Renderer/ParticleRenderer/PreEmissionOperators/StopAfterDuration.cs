namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class StopAfterDuration : ParticleFunctionPreEmissionOperator
    {
        private readonly INumberProvider duration = new LiteralNumberProvider(1.0f);
        private readonly bool destroy;

        public StopAfterDuration(ParticleDefinitionParser parse) : base(parse)
        {
            duration = parse.NumberProvider("m_flDuration", duration);
            destroy = parse.Boolean("m_bDestroyImmediately", destroy);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            particleSystemState.SetStopTime(duration.NextNumber(particleSystemState), destroy);
        }
    }
}

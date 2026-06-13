namespace ValveResourceFormat.Renderer.Particles.PreEmissionOperators
{
    /// <summary>
    /// Stops the particle system after a specified duration, optionally destroying all
    /// remaining particles immediately.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_StopAfterCPDuration">C_OP_StopAfterCPDuration</seealso>
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

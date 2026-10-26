namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Stops the particle system once its age passes a duration, optionally destroying all remaining
    /// particles immediately or playing the endcap.
    /// </summary>
    /// <remarks>
    /// The duration is evaluated every step while the system is emitting, and the stop happens in the
    /// step the age first exceeds it, before that step's emission. A system started again after the
    /// stop is stopped again.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_StopAfterCPDuration">C_OP_StopAfterCPDuration</seealso>
    class StopAfterDuration : ParticleFunctionPreEmissionOperator
    {
        private readonly INumberProvider duration = new LiteralNumberProvider(1.0f);
        private readonly bool destroy;
        private readonly bool playEndCap = true;

        public StopAfterDuration(ParticleDefinitionParser parse) : base(parse)
        {
            duration = parse.NumberProvider("m_flDuration", duration);
            destroy = parse.Boolean("m_bDestroyImmediately", destroy);
            playEndCap = parse.Boolean("m_bPlayEndCap", playEndCap);
        }

        public override void Operate(ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (particleSystemState.EmissionStopped)
            {
                return;
            }

            if (particleSystemState.Age > duration.NextNumber(particleSystemState))
            {
                particleSystemState.Data?.StopEmission(destroy, playEndCap);
            }
        }
    }
}

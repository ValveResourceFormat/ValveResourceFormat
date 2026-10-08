namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Stops the particle system once its age passes a duration, optionally destroying all remaining
    /// particles immediately or playing the endcap.
    /// </summary>
    /// <remarks>
    /// The duration is evaluated every frame and the stop happens in the frame the age first exceeds
    /// it, before that frame's emission. <c>m_bRunOnce</c> has no effect on this operator.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_StopAfterCPDuration">C_OP_StopAfterCPDuration</seealso>
    class StopAfterDuration : ParticleFunctionPreEmissionOperator
    {
        private readonly INumberProvider duration = new LiteralNumberProvider(1.0f);
        private readonly bool destroy;
        private readonly bool playEndCap = true;

        private bool stopped;

        public StopAfterDuration(ParticleDefinitionParser parse) : base(parse)
        {
            duration = parse.NumberProvider("m_flDuration", duration);
            destroy = parse.Boolean("m_bDestroyImmediately", destroy);
            playEndCap = parse.Boolean("m_bPlayEndCap", playEndCap);
            // Intentional: m_bRunOnce is ignored
            RunOnce = false;
        }

        public override void Reset()
        {
            stopped = false;
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime)
        {
            if (stopped || particleSystemState.InEndCap)
            {
                return;
            }

            if (particleSystemState.Age > duration.NextNumber(particleSystemState))
            {
                stopped = true;
                particleSystemState.Data?.StopEmission(destroy, playEndCap);
            }
        }
    }
}

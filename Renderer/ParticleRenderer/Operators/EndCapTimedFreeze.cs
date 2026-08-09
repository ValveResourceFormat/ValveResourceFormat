namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Freezes the system and everything below it once the endcap has been running for
    /// <c>m_flFreezeTime</c>. Frozen systems still draw, they just stop simulating.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_EndCapTimedFreeze">C_OP_EndCapTimedFreeze</seealso>
    class EndCapTimedFreeze : ParticleFunctionOperator
    {
        private readonly INumberProvider freezeTime = new LiteralNumberProvider(1f);

        public EndCapTimedFreeze(ParticleDefinitionParser parse) : base(parse)
        {
            freezeTime = parse.NumberProvider("m_flFreezeTime", freezeTime);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (!particleSystemState.InEndCap)
            {
                return;
            }

            if (particleSystemState.EndCapAge <= freezeTime.NextNumber(particleSystemState))
            {
                return;
            }

            particleSystemState.Frozen = true;
            particleSystemState.Data?.FreezeChildren();
        }
    }
}

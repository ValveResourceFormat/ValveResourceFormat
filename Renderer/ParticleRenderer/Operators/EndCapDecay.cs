namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Kills each particle once the endcap has been running for as long as that particle's whole
    /// lifetime, so the collection drains over the spread of its authored lifetimes rather than at once.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_EndCapDecay">C_OP_EndCapDecay</seealso>
    class EndCapDecay : ParticleFunctionOperator
    {
        public EndCapDecay(ParticleDefinitionParser parse) : base(parse)
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (!particleSystemState.InEndCap)
            {
                return;
            }

            var endCapAge = particleSystemState.EndCapAge;

            foreach (ref var particle in particles.Current)
            {
                if (particle.Lifetime <= 0f || particle.Lifetime <= endCapAge)
                {
                    particle.MarkedAsKilled = true;
                }
            }
        }
    }
}

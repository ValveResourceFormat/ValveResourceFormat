namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Kills each particle whose whole lifetime is shorter than the endcap has been running, so the
    /// collection drains over the spread of its authored lifetimes rather than at once. The engine
    /// offsets the threshold by the collection age at which the endcap started, delaying the drain.
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

            var endCapAge = particleSystemState.EndCapAge - particleSystemState.EndCapStartAge;

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

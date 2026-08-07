namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Kills a fraction of the particles partway through their lifetime, thinning out a burst as it
    /// ages instead of removing it all at once.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_Cull">C_OP_Cull</seealso>
    class Cull : ParticleFunctionOperator
    {
        // Offsets into the shared deterministic random table, so the cull draw and the cull time
        // are independent of each other and of other per-particle randomness.
        private const int CullChanceSeed = 7919;
        private const int CullTimeSeed = 104729;

        private readonly float cullPercentage = 0.5f;
        private readonly float cullStart;
        private readonly float cullEnd = 1f;
        private readonly float cullExponent = 1f;

        public Cull(ParticleDefinitionParser parse) : base(parse)
        {
            cullPercentage = parse.Float("m_flCullPerc", cullPercentage);
            cullStart = parse.Float("m_flCullStart", cullStart);
            cullEnd = parse.Float("m_flCullEnd", cullEnd);
            cullExponent = parse.Float("m_flCullExp", cullExponent);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            if (cullPercentage <= 0f)
            {
                return;
            }

            foreach (ref var particle in particles.Current)
            {
                if (ParticleCollection.RandomSingle(particle.ParticleID + CullChanceSeed) >= cullPercentage)
                {
                    continue;
                }

                var cullTime = ParticleCollection.RandomWithExponentBetween(particle.ParticleID + CullTimeSeed, cullExponent, cullStart, cullEnd);

                if (particle.NormalizedAge >= cullTime)
                {
                    particle.Kill();
                }
            }
        }
    }
}

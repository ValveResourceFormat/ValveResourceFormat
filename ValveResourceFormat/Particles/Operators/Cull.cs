namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Kills a fraction of the particles partway through their lifetime, thinning out a burst as it
    /// ages instead of removing it all at once.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_Cull">C_OP_Cull</seealso>
    class Cull : ParticleFunctionOperator
    {
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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var chance = strength * cullPercentage;

            foreach (ref var particle in particles.Current)
            {
                // Both draws are taken for every particle, whether or not it goes on to be culled
                var cullChance = particleSystemState.Random.Next();
                var cullTime = particleSystemState.Random.NextWithExponentBetween(cullExponent, cullStart, cullEnd);

                if (cullChance > chance)
                {
                    continue;
                }

                var age = particle.Age / particle.Lifetime;
                age = age >= 0f ? MathF.Min(1f, age) : 0f;

                if (age >= cullStart && cullEnd >= age && age >= cullTime)
                {
                    particle.Kill();
                }
            }
        }
    }
}

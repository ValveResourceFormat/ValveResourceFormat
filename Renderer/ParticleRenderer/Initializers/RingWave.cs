namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Positions particles in a ring pattern around a transform, with configurable initial radius, thickness, and even or random angular distribution.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RingWave">C_INIT_RingWave</seealso>
    class RingWave : ParticleFunctionInitializer
    {
        private readonly bool evenDistribution;
        private readonly INumberProvider initialRadius = new LiteralNumberProvider(0);
        private readonly INumberProvider thickness = new LiteralNumberProvider(0);
        private readonly INumberProvider particlesPerOrbit = new LiteralNumberProvider(-1);
        private readonly INumberProvider initialSpeedMin = new LiteralNumberProvider(0);
        private readonly INumberProvider initialSpeedMax = new LiteralNumberProvider(0);

        private float orbitCount;

        public RingWave(ParticleDefinitionParser parse) : base(parse)
        {
            evenDistribution = parse.Boolean("m_bEvenDistribution", evenDistribution);
            particlesPerOrbit = parse.NumberProvider("m_flParticlesPerOrbit", particlesPerOrbit);
            initialRadius = parse.NumberProvider("m_flInitialRadius", initialRadius);
            thickness = parse.NumberProvider("m_flThickness", thickness);
            initialSpeedMin = parse.NumberProvider("m_flInitialSpeedMin", initialSpeedMin);
            initialSpeedMax = parse.NumberProvider("m_flInitialSpeedMax", initialSpeedMax);

            // other properties: m_flRoll
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var thickness = this.thickness.NextNumber(ref particle, particleSystemState);
            var particlesPerOrbit = this.particlesPerOrbit.NextInt(ref particle, particleSystemState);

            var radius = initialRadius.NextNumber(ref particle, particleSystemState) + (Random.Shared.NextSingle() * thickness);

            var angle = GetNextAngle(particlesPerOrbit, particles.Capacity);
            var radialDirection = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);

            particle.Position += radius * radialDirection;

            // Initial speed pushes outward along the ring direction (positive = outward).
            var speedMin = initialSpeedMin.NextNumber(ref particle, particleSystemState);
            var speedMax = initialSpeedMax.NextNumber(ref particle, particleSystemState);
            if (speedMin != 0f || speedMax != 0f)
            {
                particle.Velocity += radialDirection * ParticleSystemRenderState.RandomFloat(speedMin, speedMax);
            }

            return particle;
        }

        private float GetNextAngle(int particlesPerOrbit, int maxParticles)
        {
            if (evenDistribution)
            {
                // Unset (-1) or invalid counts fall back to the collection's maximum particle count.
                var perOrbit = Math.Max(1, particlesPerOrbit <= 0 ? maxParticles : particlesPerOrbit);

                var offset = orbitCount / perOrbit;

                orbitCount = (orbitCount + 1) % perOrbit;

                return offset * MathF.Tau;
            }
            else
            {
                // Return a random angle between 0 and 2pi
                return MathF.Tau * Random.Shared.NextSingle();
            }
        }
    }
}

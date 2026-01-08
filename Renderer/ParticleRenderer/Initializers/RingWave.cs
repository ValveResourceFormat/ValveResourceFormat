namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class RingWave : ParticleFunctionInitializer
    {
        private readonly bool evenDistribution;
        private readonly INumberProvider initialRadius = new LiteralNumberProvider(0);
        private readonly INumberProvider thickness = new LiteralNumberProvider(0);
        private readonly INumberProvider particlesPerOrbit = new LiteralNumberProvider(-1);

        private float orbitCount;

        public RingWave(ParticleDefinitionParser parse) : base(parse)
        {
            evenDistribution = parse.Boolean("m_bEvenDistribution", evenDistribution);
            particlesPerOrbit = parse.NumberProvider("m_flParticlesPerOrbit", particlesPerOrbit);
            initialRadius = parse.NumberProvider("m_flInitialRadius", initialRadius);
            thickness = parse.NumberProvider("m_flThickness", thickness);

            // other properties: m_vInitialSpeedMin/Max, m_flRoll
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var thickness = this.thickness.NextNumber(ref particle, particleSystemState);
            var particlesPerOrbit = this.particlesPerOrbit.NextInt(ref particle, particleSystemState);

            var radius = initialRadius.NextNumber(ref particle, particleSystemState) + (Random.Shared.NextSingle() * thickness);

            var angle = GetNextAngle(particlesPerOrbit);

            particle.Position += radius * new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);

            return particle;
        }

        private float GetNextAngle(int particlesPerOrbit)
        {
            if (evenDistribution)
            {
                var offset = orbitCount / particlesPerOrbit;

                orbitCount = (orbitCount + 1) % particlesPerOrbit;

                return offset * 2 * MathF.PI;
            }
            else
            {
                // Return a random angle between 0 and 2pi
                return 2 * MathF.PI * Random.Shared.NextSingle();
            }
        }
    }
}

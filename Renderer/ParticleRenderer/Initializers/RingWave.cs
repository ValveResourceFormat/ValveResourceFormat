namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Positions particles in a ring pattern around a transform, with configurable initial radius, thickness, and even or random angular distribution.
    /// </summary>
    /// <remarks>
    /// "Position Along Ring" in the particle editor. Like "Position Within Sphere Random", it can
    /// also impart radial force to particles via the min/max initial speed. Thickness spreads
    /// particles through a ball around the ring, not just along the radius.
    /// </remarks>
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

            var thicknessOffset = SampleUnitBall(particleSystemState) * thickness;

            var radius = initialRadius.NextNumber(ref particle, particleSystemState);

            var angle = GetNextAngle(particlesPerOrbit, particles.Capacity, particleSystemState);
            var radialDirection = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);

            particle.Position += (radius * radialDirection) + thicknessOffset;

            // Initial speed pushes outward along the ring direction (positive = outward).
            var speedMin = initialSpeedMin.NextNumber(ref particle, particleSystemState);
            var speedMax = initialSpeedMax.NextNumber(ref particle, particleSystemState);
            particle.Velocity += radialDirection * particleSystemState.Random.NextBetween(speedMin, speedMax);

            return particle;
        }

        /// <summary>
        /// A point drawn uniformly through the unit ball. Consumes three random slots, in the order
        /// polar cosine, azimuth, then radius fraction.
        /// </summary>
        private static Vector3 SampleUnitBall(ParticleSystemRenderState particleSystemState)
        {
            var cosPolar = particleSystemState.Random.NextBetween(-1f, 1f);
            var azimuth = particleSystemState.Random.NextBetween(0f, MathF.Tau);
            var radius = MathF.Cbrt(particleSystemState.Random.Next());

            var sinPolar = MathF.Sqrt(MathF.Max(0f, 1f - (cosPolar * cosPolar)));
            var (sin, cos) = MathF.SinCos(azimuth);

            return new Vector3(sinPolar * cos, sinPolar * sin, cosPolar) * radius;
        }

        private float GetNextAngle(int particlesPerOrbit, int maxParticles, ParticleSystemRenderState particleSystemState)
        {
            if (evenDistribution)
            {
                // -1 is the sentinel for using the collection's maximum particle count.
                var perOrbit = Math.Max(1, particlesPerOrbit == -1 ? maxParticles : particlesPerOrbit);

                var offset = orbitCount / perOrbit;

                orbitCount = (orbitCount + 1) % perOrbit;

                return offset * MathF.Tau;
            }

            return particleSystemState.Random.NextBetween(0f, MathF.Tau);
        }
    }
}

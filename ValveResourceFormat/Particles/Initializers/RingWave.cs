using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
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

        /// <summary>Transform the ring is centred on, and whose rotation the ring plane lies in.</summary>
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();

        /// <summary>Drops the vertical part of the outward speed, leaving the ring to spread flat.</summary>
        private readonly bool xyVelocityOnly;

        private float orbitCount;

        public RingWave(ParticleDefinitionParser parse) : base(parse)
        {
            evenDistribution = parse.Boolean("m_bEvenDistribution", evenDistribution);
            particlesPerOrbit = parse.NumberProvider("m_flParticlesPerOrbit", particlesPerOrbit);
            initialRadius = parse.NumberProvider("m_flInitialRadius", initialRadius);
            thickness = parse.NumberProvider("m_flThickness", thickness);
            initialSpeedMin = parse.NumberProvider("m_flInitialSpeedMin", initialSpeedMin);
            initialSpeedMax = parse.NumberProvider("m_flInitialSpeedMax", initialSpeedMax);
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            xyVelocityOnly = parse.Boolean("m_bXYVelocityOnly", xyVelocityOnly);

            // m_flRoll, m_flPitch and m_flYaw rotate the ring circle, but not the thickness offset,
            // before the transform is applied. All three default to zero and are not read here.
        }

        public override void Reset()
        {
            orbitCount = 0;
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var thickness = this.thickness.NextNumber(ref particle, particleSystemState);
            var particlesPerOrbit = this.particlesPerOrbit.NextInt(ref particle, particleSystemState);

            var thicknessOffset = particleSystemState.Random.NextInUnitBall(out _) * thickness;

            var radius = initialRadius.NextNumber(ref particle, particleSystemState);

            var angle = GetNextAngle(particlesPerOrbit, particles.Capacity, particleSystemState);
            var radialDirection = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);

            var transform = transformInput.NextTransform(ref particle, particleSystemState);

            particle.Position = Vector3.Transform((radius * radialDirection) + thicknessOffset, transform);

            // Initial speed pushes outward, along the line from the transform to where the particle
            // actually landed, so the thickness offset tilts the direction as well as the position.
            var outward = ParticleMath.Normalize(particle.Position - transform.Translation);

            if (xyVelocityOnly)
            {
                outward.Z = 0f;
            }

            var speedMin = initialSpeedMin.NextNumber(ref particle, particleSystemState);
            var speedMax = initialSpeedMax.NextNumber(ref particle, particleSystemState);
            particle.Velocity += outward * particleSystemState.Random.NextBetween(speedMin, speedMax);

            return particle;
        }

        private float GetNextAngle(int particlesPerOrbit, int maxParticles, ParticleSystemState particleSystemState)
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

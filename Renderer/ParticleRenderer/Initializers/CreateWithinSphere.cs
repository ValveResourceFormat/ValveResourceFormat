namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Places particles at random positions within a sphere defined by a minimum and maximum radius,
    /// and assigns an initial outward velocity sampled from a speed range. An optional local
    /// coordinate system speed can add a directional bias to the velocity.
    /// </summary>
    class CreateWithinSphere : ParticleFunctionInitializer
    {
        /// <summary>Minimum distance to spawn from the center of the sphere.</summary>
        protected readonly INumberProvider radiusMin = new LiteralNumberProvider(0);

        /// <summary>Maximum distance to spawn from the center of the sphere.</summary>
        protected readonly INumberProvider radiusMax = new LiteralNumberProvider(0);

        /// <summary>Minimum initial speed of the particle emitted outward from the sphere.</summary>
        protected readonly INumberProvider speedMin = new LiteralNumberProvider(0);

        /// <summary>Maximum initial speed of the particle emitted outward from the sphere.</summary>
        protected readonly INumberProvider speedMax = new LiteralNumberProvider(0);

        /// <summary>Local space minimum initial speed of the particle in x y z.</summary>
        protected readonly IVectorProvider localCoordinateSystemSpeedMin = new LiteralVectorProvider(Vector3.Zero);

        /// <summary>Local space maximum initial speed of the particle in x y z.</summary>
        protected readonly IVectorProvider localCoordinateSystemSpeedMax = new LiteralVectorProvider(Vector3.Zero);

        /// <summary>Exponent biasing the radial speed draw. 1 is an unbiased uniform draw.</summary>
        protected readonly float speedRandExp = 1f;

        public CreateWithinSphere(ParticleDefinitionParser parse) : base(parse)
        {
            radiusMin = parse.NumberProvider("m_fRadiusMin", radiusMin);
            radiusMax = parse.NumberProvider("m_fRadiusMax", radiusMax);
            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);
            speedRandExp = Math.Clamp(parse.Float("m_fSpeedRandExp", speedRandExp), -255f, 255f);
            localCoordinateSystemSpeedMin = parse.VectorProvider("m_LocalCoordinateSystemSpeedMin", localCoordinateSystemSpeedMin);
            localCoordinateSystemSpeedMax = parse.VectorProvider("m_LocalCoordinateSystemSpeedMax", localCoordinateSystemSpeedMax);
        }

        /// <summary>
        /// A direction drawn uniformly over the unit sphere, from a uniform cosine of the polar angle
        /// and a uniform azimuth. Consumes random slots <paramref name="particleId"/> and the next one.
        /// </summary>
        protected static Vector3 SampleUnitSphereDirection(int particleId)
        {
            var cosPolar = ParticleCollection.RandomBetween(particleId, -1f, 1f);
            var azimuth = ParticleCollection.RandomBetween(particleId + 1, 0f, MathF.Tau);
            var sinPolar = MathF.Sqrt(MathF.Max(0f, 1f - (cosPolar * cosPolar)));
            var (sin, cos) = MathF.SinCos(azimuth);

            return new Vector3(sinPolar * cos, sinPolar * sin, cosPolar);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var direction = SampleUnitSphereDirection(particle.ParticleID);

            // A cube root over the radius fraction spreads particles evenly through the sphere's
            // volume; a linear draw would bunch them toward the centre
            var distance = ParticleCollection.RandomWithExponentBetween(
                particle.ParticleID + 2,
                1f / 3f,
                radiusMin.NextNumber(ref particle, particleSystemState),
                radiusMax.NextNumber(ref particle, particleSystemState));

            var speed = ParticleCollection.RandomWithExponentBetween(
                particle.ParticleID + 3,
                speedRandExp,
                speedMin.NextNumber(ref particle, particleSystemState),
                speedMax.NextNumber(ref particle, particleSystemState));

            var localCoordinateSystemSpeed = ParticleCollection.RandomBetweenPerComponent(
                particle.ParticleID + 4,
                localCoordinateSystemSpeedMin.NextVector(ref particle, particleSystemState),
                localCoordinateSystemSpeedMax.NextVector(ref particle, particleSystemState));

            particle.Position += direction * distance;
            particle.Velocity = (direction * speed) + localCoordinateSystemSpeed;

            return particle;
        }
    }
}

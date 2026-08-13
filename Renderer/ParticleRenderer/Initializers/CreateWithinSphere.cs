namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Places particles at random positions within a sphere centred on a transform, and assigns an
    /// initial outward velocity sampled from a speed range. A per-axis distance bias skews the
    /// spherical distribution, and a local coordinate system speed adds a directional component to
    /// the velocity.
    /// </summary>
    class CreateWithinSphere : ParticleFunctionInitializer
    {
        /// <summary>Transform the sphere is centred on, and whose rotation local coordinates use.</summary>
        protected readonly ITransformProvider transformInput;

        /// <summary>Minimum distance to spawn from the center of the sphere.</summary>
        protected readonly INumberProvider radiusMin = new LiteralNumberProvider(0);

        /// <summary>Maximum distance to spawn from the center of the sphere.</summary>
        protected readonly INumberProvider radiusMax = new LiteralNumberProvider(0);

        /// <summary>Minimum initial speed of the particle emitted outward from the sphere.</summary>
        protected readonly INumberProvider speedMin = new LiteralNumberProvider(0);

        /// <summary>Maximum initial speed of the particle emitted outward from the sphere.</summary>
        protected readonly INumberProvider speedMax = new LiteralNumberProvider(0);

        /// <summary>
        /// Local space minimum initial speed of the particle in x y z. It is rotated by the
        /// transform whether or not local coordinates are enabled.
        /// </summary>
        protected readonly IVectorProvider localCoordinateSystemSpeedMin = new LiteralVectorProvider(Vector3.Zero);

        /// <summary>
        /// Local space maximum initial speed of the particle in x y z. It is rotated by the
        /// transform whether or not local coordinates are enabled.
        /// </summary>
        protected readonly IVectorProvider localCoordinateSystemSpeedMax = new LiteralVectorProvider(Vector3.Zero);

        /// <summary>Exponent biasing the radial speed draw. 1 is an unbiased uniform draw.</summary>
        protected readonly float speedRandExp = 1f;

        /// <summary>Per-axis multiplier applied to the spawn direction before it is normalised.</summary>
        protected readonly IVectorProvider distanceBias = new LiteralVectorProvider(Vector3.One);

        /// <summary>
        /// Per-axis flags. A non-zero component folds the spawn direction into the positive half of
        /// that axis, turning the sphere into a hemisphere or an octant of one.
        /// </summary>
        protected readonly Vector3 distanceBiasAbs = Vector3.Zero;

        /// <summary>Whether the spawn offset is rotated by the transform.</summary>
        protected readonly bool localCoords;

        /// <summary>Control point supplying per-axis scales. Parsed, but not applied.</summary>
        protected readonly int scaleCP = -1;

        /// <summary>Attribute the spawn position is written to.</summary>
        protected readonly ParticleField outputField = ParticleField.Position;

        protected CreateWithinSphere(ParticleDefinitionParser parse, ITransformProvider transformInput) : base(parse)
        {
            this.transformInput = transformInput;
            radiusMin = parse.NumberProvider("m_fRadiusMin", radiusMin);
            radiusMax = parse.NumberProvider("m_fRadiusMax", radiusMax);
            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);
            speedRandExp = Math.Clamp(parse.Float("m_fSpeedRandExp", speedRandExp), -255f, 255f);
            localCoordinateSystemSpeedMin = parse.VectorProvider("m_LocalCoordinateSystemSpeedMin", localCoordinateSystemSpeedMin);
            localCoordinateSystemSpeedMax = parse.VectorProvider("m_LocalCoordinateSystemSpeedMax", localCoordinateSystemSpeedMax);
            distanceBias = parse.VectorProvider("m_vecDistanceBias", distanceBias);
            distanceBiasAbs = parse.Vector3("m_vecDistanceBiasAbs", distanceBiasAbs);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            scaleCP = parse.Int32("m_nScaleCP", scaleCP);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
        }

        /// <summary>
        /// A direction drawn uniformly over the unit sphere, from a uniform cosine of the polar angle
        /// and a uniform azimuth. Consumes two random slots, the polar cosine before the azimuth.
        /// </summary>
        protected static Vector3 SampleUnitSphereDirection(ParticleSystemRenderState particleSystemState)
        {
            var cosPolar = particleSystemState.Random.NextBetween(-1f, 1f);
            var azimuth = particleSystemState.Random.NextBetween(0f, MathF.Tau);
            var sinPolar = MathF.Sqrt(MathF.Max(0f, 1f - (cosPolar * cosPolar)));
            var (sin, cos) = MathF.SinCos(azimuth);

            return new Vector3(sinPolar * cos, sinPolar * sin, cosPolar);
        }

        /// <remarks>
        /// The cube root over the radius fraction spreads particles evenly through the sphere's
        /// volume; a linear draw would bunch them toward the centre.
        /// </remarks>
        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var transform = transformInput.NextTransform(ref particle, particleSystemState);
            var position = transform.Translation;

            var randomVector = SampleUnitSphereDirection(particleSystemState);

            if (distanceBiasAbs.X != 0)
            {
                randomVector.X = MathF.Abs(randomVector.X);
            }
            if (distanceBiasAbs.Y != 0)
            {
                randomVector.Y = MathF.Abs(randomVector.Y);
            }
            if (distanceBiasAbs.Z != 0)
            {
                randomVector.Z = MathF.Abs(randomVector.Z);
            }

            var bias = distanceBias.NextVector(ref particle, particleSystemState);

            var biasedDirection = Vector3.Normalize(randomVector * bias);

            var distance = particleSystemState.Random.NextWithExponentBetween(
                1f / 3f,
                radiusMin.NextNumber(ref particle, particleSystemState),
                radiusMax.NextNumber(ref particle, particleSystemState));

            var speed = particleSystemState.Random.NextWithExponentBetween(
                speedRandExp,
                speedMin.NextNumber(ref particle, particleSystemState),
                speedMax.NextNumber(ref particle, particleSystemState));

            var localCoordinateSystemSpeed = particleSystemState.Random.NextBetweenPerComponent(
                localCoordinateSystemSpeedMin.NextVector(ref particle, particleSystemState),
                localCoordinateSystemSpeedMax.NextVector(ref particle, particleSystemState));

            var offset = biasedDirection * distance;

            Vector3 worldOffset;
            if (localCoords)
            {
                worldOffset = Vector3.TransformNormal(offset, transform) + position;
            }
            else
            {
                worldOffset = position + offset;
            }

            particle.SetVector(outputField, worldOffset);

            var velocityDirection = localCoords
                ? Vector3.TransformNormal(biasedDirection, transform)
                : biasedDirection;

            var velocityLocal = Vector3.TransformNormal(localCoordinateSystemSpeed, transform);

            particle.Velocity = (velocityDirection * speed) + velocityLocal;

            return particle;
        }
    }
}
